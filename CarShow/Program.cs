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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using System.Text;
var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddPolicy("AllowAll", policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.Services.Configure<CrawlerSettings>(builder.Configuration.GetSection(CrawlerSettings.SectionName));
builder.Services.AddDbContext<CarShowContext>(options => { options.UseSqlServer(builder.Configuration.GetConnectionString("CarShow")); options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)); });
builder.Services.AddHttpClient<ICarIrCrawlerService, CarIrCrawlerService>(client => { client.Timeout = TimeSpan.FromMinutes(2); client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CarShowCrawler/1.0"); });
builder.Services.AddScoped<ICarDataSource>(sp => sp.GetRequiredService<ICarIrCrawlerService>());
builder.Services.AddScoped<ICarCrawlerSyncService, CarCrawlerSyncService>();
builder.Services.AddHostedService<GetCarsBackgroundService>();
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
var jwtSetting = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSetting["SecretKey"] ?? throw new InvalidOperationException("Jwt SecretKey is missing!"));
builder.Services.AddAuthentication(options => { options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme; options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme; options.AddPolicyScheme = null; }).AddJwtBearer(options => { options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = jwtSetting["Issuer"], ValidAudience = jwtSetting["Audience"], IssuerSigningKey = new SymmetricSecurityKey(secretKey) }; });
builder.Services.AddAuthorization();
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (SqlException ex) when (ex.Number == 2705)
    {
        logger.LogWarning(ex, "Migration encountered existing columns/tables; continuing with the already-provisioned production schema.");
    }
    await db.Database.ExecuteSqlRawAsync(@"
        IF COL_LENGTH('dbo.Cars', 'SourceUrl') IS NULL ALTER TABLE [dbo].[Cars] ADD [SourceUrl] nvarchar(max) NULL;
        IF COL_LENGTH('dbo.Cars', 'FactoryPrice') IS NULL ALTER TABLE [dbo].[Cars] ADD [FactoryPrice] decimal(18,2) NULL;
        IF COL_LENGTH('dbo.Cars', 'MarketPrice') IS NULL ALTER TABLE [dbo].[Cars] ADD [MarketPrice] decimal(18,2) NULL;
        IF COL_LENGTH('dbo.Cars', 'Slug') IS NULL ALTER TABLE [dbo].[Cars] ADD [Slug] nvarchar(450) NULL;
        IF COL_LENGTH('dbo.Cars', 'LastUpdated') IS NULL ALTER TABLE [dbo].[Cars] ADD [LastUpdated] datetime2 NULL;
        IF COL_LENGTH('dbo.Cars', 'CategoryId') IS NOT NULL ALTER TABLE [dbo].[Cars] ALTER COLUMN [CategoryId] bigint NULL;
        ");
}
app.UseSwagger();
app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "CarShow API v1"); c.RoutePrefix = "swagger"; });
app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
