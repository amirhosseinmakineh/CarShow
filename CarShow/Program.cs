using CarShow.Domain.Telegram;
using CarShow.BackgroundServices;
using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Infrastracture.Telegram;
using CarShow.Security.Token;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args.Contains("--telegram-probe") ? [] : args);
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
    });
});
builder.Services.AddDbContext<CarShowContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("CarShow"));
});
builder.Services.AddHttpClient("TelegramWebPreview", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CarShowTelegramPreview/1.0");
});
builder.Services.AddSingleton<ITelegramCollectionQueue, TelegramCollectionQueue>();
builder.Services.AddScoped<ITelegramChannelService, TelegramChannelService>();
builder.Services.AddSingleton<ITelegramCollector, TelegramCollector>();
builder.Services.AddScoped<ITelegramListingExtractor, TelegramListingExtractor>();
if (builder.Configuration.GetValue("Telegram:WorkersEnabled", true))
{
    builder.Services.AddHostedService<TelegramCollectionWorker>();
    builder.Services.AddHostedService<TelegramPricingWorker>();
}
builder.Services.AddScoped<ITelegramProcessingStore, TelegramProcessingStore>();
builder.Services.AddScoped<TelegramCollectionService>();
builder.Services.AddScoped<TelegramPricingService>();
builder.Services.AddScoped<ITelegramPricingQueries, TelegramPricingQueries>();
builder.Services.AddScoped(typeof(IBaseRepository<,>), typeof(BaseRepository<,>));
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITokenGenerator, TokenGenerator>();
builder.Services.AddScoped<ICarSErvicce, CarService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ITipService, TipService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IModelService, ModelService>();
builder.Services.AddScoped<IInstallmentCalculator, InstallmentCalculator>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOrderItemService, OrderItemService>();
builder.Services.AddScoped<TelegramPreviewService>();
if (args.Contains("--telegram-probe"))
{
    await using var probeApp = builder.Build();
    await using var probeScope = probeApp.Services.CreateAsyncScope();
    var channels = args.Where(x => x.StartsWith('@')).ToArray();
    if (channels.Length == 0) channels = ["@foroshekhodro", "@Baravard"];
    var report = await probeScope.ServiceProvider.GetRequiredService<TelegramPreviewService>()
        .CollectAsync(new(channels, 500), CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}
var jwtSetting = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSetting["SecretKey"] ?? throw new InvalidOperationException("Jwt SecretKey is missing!"));
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSetting["Issuer"],
        ValidAudience = jwtSetting["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(secretKey)
    };
});
if (secretKey.Length < 32) throw new InvalidOperationException("JwtSettings:SecretKey must be set to at least 32 UTF-8 bytes using environment variables.");
builder.Services.AddHealthChecks().AddDbContextCheck<CarShowContext>("sqlserver");
builder.Services.AddAuthorization();
var app = builder.Build();
if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");
    await db.Database.MigrateAsync();
    logger.LogInformation("Database migrations applied successfully.");
}
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "CarShow API v1");
    options.RoutePrefix = "swagger";
});
app.UseStaticFiles();
app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapControllers();
app.Run();
