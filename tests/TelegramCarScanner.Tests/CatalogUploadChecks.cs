using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CarShow.Application;
using CarShow.ApplicationService.Services;
using CarShow.Controllers;
using CarShow.Diagnostics;
using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

internal static class CatalogUploadChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("carshow-image-tests-").FullName;
        var images = Path.Combine(directory, "images");
        var logs = new RequestLogProvider();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-signing-key-32bytes"));
        const string model = "/api/admin/catalog/model-images/72373";
        const string car = "/api/admin/catalog/model-images/cars/15";

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["VehicleImages:Directory"] = images;
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            builder.Services.AddControllers().AddApplicationPart(typeof(AdminModelImagesController).Assembly);
            builder.Services.AddScoped<UploadDiagnostics>();
            builder.Services.AddScoped<CarModelImageRepository>();
            builder.Services.AddScoped<CarModelImageService>();
            builder.Services.AddDbContext<CarShowContext>(options => options
                .UseSqlite($"Data Source={Path.Combine(directory, "test.db")}")
                .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning)));
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey
                });
            builder.Services.AddAuthorization();

            await using var app = builder.Build();
            app.UseMiddleware<ApiRequestLoggingMiddleware>();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.Use(ApiRequestLoggingMiddleware.TrackAuthorizedUploadAsync);
            app.MapControllers();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
                await db.Database.EnsureCreatedAsync();
                db.CarModels.Add(new CarModel { Id = 72373, Name = "207", ImageName = "/images/cars/old.jpg" });
                db.Tips.Add(new Tip { Id = 7, Name = "دنده‌ای" });
                db.Cars.AddRange(
                    new Car
                    {
                        Id = 11, CarModelId = 72373, IsReferenceApproved = true, ImageName = "/images/cars/old.jpg",
                        Description = """{"Type":"TelegramPriceReference","Year":"2025","TipName":"اتومات"}"""
                    },
                    new Car { Id = 12, CarModelId = 72373, IsReferenceApproved = true, ImageName = "/images/cars/individual.jpg" },
                    new Car { Id = 13, CarModelId = 72373, TipId = 7, Description = "Manual description" },
                    new Car { Id = 14, CarModelId = 72373, IsReferenceApproved = true, IsDelete = true },
                    new Car { Id = 15, CarModelId = 72373, IsReferenceApproved = true });
                var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
                var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
                db.Users.AddRange(new User { Id = high }, new User { Id = low });
                await db.SaveChangesAsync();
                try
                {
                    await db.Users.Select(row => row.Id).Skip(1).Take(1).ToListAsync();
                    check(false, "Unordered SQL pagination must raise warning 10102");
                }
                catch (InvalidOperationException exception)
                    when (exception.Message.Contains(nameof(CoreEventId.RowLimitingOperationWithoutOrderByWarning)))
                {
                    check(true, "Old unordered query reproduces EF warning 10102");
                }
                var users = new UserService(new BaseRepository<Guid, User>(db),
                    null!, null!, null!, null!, null!, null!, null!, null!);
                check(users.GetAllUsers(1, 0).Single().Id == low && users.GetAllUsers(1, 1).Single().Id == high,
                    "Users have stable database pagination without EF warning 10102");
            }

            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var jwt = new JwtSecurityToken(claims: [new Claim(ClaimTypes.Role, "Admin")],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
            using (var denied = await UploadAsync(client, model))
                check(denied.StatusCode == HttpStatusCode.Unauthorized, "Upload requires admin authentication");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));

            using (var payload = JsonDocument.Parse(await client.GetStringAsync("/api/admin/catalog")))
            {
                var items = payload.RootElement.GetProperty("items").EnumerateArray()
                    .ToDictionary(row => row.GetProperty("id").GetInt64());
                check(items[11].GetProperty("startDate").GetString() == "2025" &&
                      items[11].GetProperty("tip").GetString() == "اتومات",
                    "Admin catalog returns archived year and trim for a reference without a TipId");
                check(items[13].GetProperty("startDate").GetString() == "" &&
                      items[13].GetProperty("tip").GetString() == "دنده‌ای",
                    "Admin catalog preserves linked trim for manually entered cars");
            }

            using (var invalid = await UploadAsync(client, model, new byte[12]))
                check(invalid.StatusCode == HttpStatusCode.BadRequest, "Invalid image is rejected");
            using (var missing = await UploadAsync(client, model + "0"))
                check(missing.StatusCode == HttpStatusCode.NotFound, "Missing model returns 404");
            using (var missing = await UploadAsync(client, car + "0"))
                check(missing.StatusCode == HttpStatusCode.NotFound, "Missing car returns 404");

            var storageFailureId = Guid.NewGuid().ToString("D");
            await File.WriteAllTextAsync(images, "A file at the configured directory path");
            using (var failed = await UploadAsync(client, model, requestId: storageFailureId))
                check(failed.StatusCode == HttpStatusCode.InternalServerError, "File storage failure returns 500");
            File.Delete(images);
            await VerifyFailureAsync(logs, check, storageFailureId, "FilePersistence");

            string modelPath;
            var successId = Guid.NewGuid().ToString("D");
            using (var success = await UploadAsync(client, model, requestId: successId))
            {
                check(success.StatusCode == HttpStatusCode.OK, "Model upload succeeds");
                check(success.Headers.GetValues(ApiRequestLoggingMiddleware.Header).Single() == successId,
                    "Backend echoes the end-to-end request ID");
                using var payload = JsonDocument.Parse(await success.Content.ReadAsStringAsync());
                modelPath = payload.RootElement.GetProperty("imageName").GetString()!;
                check(payload.RootElement.GetProperty("updatedCars").GetInt32() == 2
                    && File.Exists(Path.Combine(images, Path.GetFileName(modelPath))), "Model upload writes the file and updates exactly two references");
            }

            using (var success = await UploadAsync(client, car))
            {
                check(success.StatusCode == HttpStatusCode.OK, "Individual upload succeeds");
                using var payload = JsonDocument.Parse(await success.Content.ReadAsStringAsync());
                check(payload.RootElement.GetProperty("updatedReference").GetBoolean(), "Individual upload updates approved car");
            }

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
                var cars = await db.Cars.ToDictionaryAsync(row => row.Id);
                check(cars[11].ImageName == modelPath && cars[12].ImageName == "/images/cars/individual.jpg"
                    && cars[13].ImageName == "" && cars[14].ImageName == "" && cars[15].ImageName != modelPath,
                    "Reference images propagate and individual, unapproved, and deleted rows are preserved");

                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TRIGGER block_car_images BEFORE UPDATE OF ImageName ON Cars
                    BEGIN SELECT RAISE(ABORT, 'test database failure'); END;
                    """);
            }

            var filesBeforeFailure = Directory.GetFiles(images).Length;
            var dbFailureId = Guid.NewGuid().ToString("D");
            using (var failed = await UploadAsync(client, model, requestId: dbFailureId))
                check(failed.StatusCode == HttpStatusCode.InternalServerError, "Database failure returns 500");
            await VerifyFailureAsync(logs, check, dbFailureId, "DatabaseUpdate");

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
                check((await db.CarModels.SingleAsync()).ImageName == modelPath
                    && (await db.Cars.SingleAsync(row => row.Id == 11)).ImageName == modelPath
                    && Directory.GetFiles(images).Length == filesBeforeFailure,
                    "Database error rolls back model and car updates and removes the uncommitted file");

                using var cancellation = new CancellationTokenSource();
                using var stream = new CancellingUpload(cancellation);
                var service = scope.ServiceProvider.GetRequiredService<CarModelImageService>();
                try
                {
                    await service.SaveAsync(72373, stream, ".png", cancellation.Token);
                    check(false, "Cancellation must propagate");
                }
                catch (OperationCanceledException)
                {
                    check(Directory.GetFiles(images).Length == filesBeforeFailure,
                        "Cancellation during file writing removes the partial file");
                }
            }

            await app.StopAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task VerifyFailureAsync(RequestLogProvider provider, Action<bool, string> check,
        string requestId, string stage)
    {
        for (var attempt = 0; attempt < 50 && !provider.Events.Any(e => Equals(e["RequestId"], requestId)); attempt++)
            await Task.Delay(10);
        check(provider.Events.Any(e => Equals(e["RequestId"], requestId) && Equals(e["FailureStage"], stage)
            && Equals(e["StatusCode"], 500) && e["ExceptionType"] is string && e["DurationMs"] is long),
            $"Failed request log correlates and identifies {stage}");
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string path, byte[]? image = null,
        string? requestId = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(image ?? [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 0]),
            "image", "car.png");
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = form };
        if (requestId is not null)
            request.Headers.Add(ApiRequestLoggingMiddleware.Header, requestId);
        return await client.SendAsync(request);
    }

    private sealed class CancellingUpload(CancellationTokenSource cancellation) : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class RequestLogProvider : ILoggerProvider
    {
        public ConcurrentQueue<IReadOnlyDictionary<string, object?>> Events { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void Dispose() { }

        private sealed class CaptureLogger(RequestLogProvider provider, string category) : ILogger
        {
            public bool IsEnabled(LogLevel level) => category == typeof(ApiRequestLoggingMiddleware).FullName;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public void Log<TState>(LogLevel level, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(level) && state is IReadOnlyList<KeyValuePair<string, object?>> properties)
                    provider.Events.Enqueue(properties.ToDictionary(pair => pair.Key, pair => pair.Value));
            }
        }
    }
}
