using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Security.Token;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

builder.Services.AddDbContext<CarShowContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("CarShow"));
});

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

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "CarShow API v1");
    c.RoutePrefix = "swagger";
});

// ترتیب درست:
app.UseCors("AllowAll"); // حتماً اینجا باشد
app.UseStaticFiles();

app.UseRouting(); // این خط را حتماً اضافه کنید (اگر نبود)

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();


// One-time catalog seeding; disabled unless explicitly enabled.
// Set CatalogSeed__Enabled=true and place cars-catalog.html in Data/Seed.
if (app.Configuration.GetValue<bool>("CatalogSeed:Enabled"))
{
    var catalogPath = Path.Combine(app.Environment.ContentRootPath, "Data", "Seed", "cars-catalog.html");
    if (!File.Exists(catalogPath))
        throw new FileNotFoundException("Catalog seed file was not found.", catalogPath);

    using var scope = app.Services.CreateScope();
    var carService = scope.ServiceProvider.GetRequiredService<ICarSErvicce>();
    await using var catalogStream = File.OpenRead(catalogPath);
    var result = await carService.ImportCatalogAsync(catalogStream);
    app.Logger.LogInformation(
        "Catalog seed: total={Total}, inserted={Inserted}, existing={Existing}, rejected={Rejected}, models={Models}, companies={Companies}, tips={Tips}",
        result.TotalRows, result.ImportedCars, result.ExistingCars, result.RejectedRows,
        result.CreatedModels, result.CreatedCompanies, result.CreatedTips);
}

app.Run();
