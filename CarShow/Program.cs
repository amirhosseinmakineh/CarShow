using CarShow.BackgroundServices;
using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Services;
using CarShow.Domain.Crawling;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Infrastracture.BackgroundServices;
using CarShow.Infrastracture.Configuration;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Crawlers;
using CarShow.Infrastracture.Repository;
using CarShow.Security.Token;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi;
using CarShow.Swagger;
var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddControllers();
builder.Services.AddSingleton<TelegramScanStatus>();
builder.Services.AddScoped<TelegramScanService>();
builder.Services.AddHostedService<TelegramScanBackgroundService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    }));
builder.Services.ConfigureSwaggerGen(options => options.OperationFilter<BearerOperationFilter>());
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
}));
builder.Services.Configure<CrawlerSettings>(builder.Configuration.GetSection(CrawlerSettings.SectionName));
builder.Services.AddDbContext<CarShowContext>(options => { options.UseSqlServer(builder.Configuration.GetConnectionString("CarShow")); options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)); });
builder.Services.AddHttpClient<ICarIrCrawlerService, CarIrCrawlerService>(client => { client.Timeout = TimeSpan.FromMinutes(2); client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CarShowCrawler/1.0"); });
builder.Services.AddScoped<ICarDataSource>(sp => sp.GetRequiredService<ICarIrCrawlerService>());
builder.Services.AddScoped<ICarCrawlerSyncService, CarCrawlerSyncService>();
// Keep the pre-Telegram crawler opt-in during the code rollback. The live
// database and its configuration are not part of this rollback.
if (builder.Configuration.GetValue("Crawler:Enabled", false))
    builder.Services.AddHostedService<GetCarsBackgroundService>();
builder.Services.AddScoped(typeof(IBaseRepository<,>), typeof(BaseRepository<,>));
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITokenGenerator, TokenGenerator>();
builder.Services.AddScoped<ICarSErvicce, CarService>();
builder.Services.AddScoped<CatalogImportService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ITipService, TipService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IModelService, ModelService>();

builder.Services.AddScoped<
    IInstallmentCalculator,
    InstallmentCalculator>();

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IOrderItemService, OrderItemService>();
var jwtSetting = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSetting["SecretKey"] ?? throw new InvalidOperationException("Jwt SecretKey is missing!"));
if (secretKey.Length < 32) throw new InvalidOperationException("JwtSettings:SecretKey must be at least 32 UTF-8 bytes.");
builder.Services.AddAuthentication(options => { options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme; options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme; }).AddJwtBearer(options => { options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = jwtSetting["Issuer"], ValidAudience = jwtSetting["Audience"], IssuerSigningKey = new SymmetricSecurityKey(secretKey) }; });
builder.Services.AddAuthorization();

// ==============================
// Health Checks
// ==============================

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<CarShowContext>("sqlserver");

// ==============================
// Build
// ==============================

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "CarShow API v1"); c.RoutePrefix = "swagger"; });
app.UseCors("Frontend");
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", async (CarShowContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok("Healthy") : Results.StatusCode(503))
    .AllowAnonymous();
// Import the versioned catalog from the published repository asset before the
// Telegram background scanner starts. ImportAsync is idempotent for existing
// company/model/trim combinations and never resets verified prices.
var catalogSeedPath = Path.Combine(app.Environment.ContentRootPath, "Data", "Seed",
    "CarShow_Final_Trimmed_Cars_Catalog_1405.html");
if (!File.Exists(catalogSeedPath))
    throw new FileNotFoundException("Required car catalog seed is missing.", catalogSeedPath);
using (var catalogScope = app.Services.CreateScope())
{
    var catalogImporter = catalogScope.ServiceProvider.GetRequiredService<CatalogImportService>();
    await using var catalogStream = File.OpenRead(catalogSeedPath);
    var catalogResult = await catalogImporter.ImportAsync(catalogStream);
    app.Logger.LogInformation("Catalog seed imported from {Path}: {Result}", catalogSeedPath, catalogResult);
}
app.Run();
