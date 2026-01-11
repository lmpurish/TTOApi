using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Stripe;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using TToApp.Helpers;
using TToApp.Model;
using TToApp.Services;
using TToApp.Services.Auth;
using TToApp.Services.Payroll;
using TToApp.Services.Scheduled;
using TToApp.Services.Settings;

var builder = WebApplication.CreateBuilder(args);

// ==================== Configuración de servicios ====================

// Stripe
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Controllers + JSON
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(t => t.FullName!.Replace('+', '.'));
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "API", Version = "v1" });
    c.SwaggerDoc("company-docs-v1", new OpenApiInfo { Title = "Company Docs", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Pega tu JWT (solo el token, sin 'Bearer').",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });

    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        var groupName = apiDesc.GroupName ?? "v1";
        return string.Equals(groupName, docName, StringComparison.OrdinalIgnoreCase);
    });
});

// CORS (lee orígenes desde appsettings: CorsSettings:AllowedOrigins)
var allowedOrigins = builder.Configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
builder.Services.AddMemoryCache();

// EF Core
var connectionString = builder.Configuration.GetConnectionString("DevConnection");
if (string.IsNullOrEmpty(connectionString))
    throw new Exception("Connection string 'DevConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Servicios propios
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddTransient<EmailService>();
builder.Services.AddSingleton<IMapper>(sp =>
{
    var cfg = new MapperConfiguration(mc =>
    {
        mc.AddProfile<MappingProfile>(); // tu perfil
        // mc.AddMaps(typeof(Program).Assembly); // opcional si tienes más perfiles
    });

    // Opcional: valida que los mapas sean correctos en el arranque
    

    return cfg.CreateMapper();
});
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IApplicantContactService, ApplicantContactService>();
builder.Services.AddHostedService<RDMonitorService>();
builder.Services.AddScoped<WhatsAppService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ISensitiveDataProtector, SensitiveDataProtector>();
builder.Services.AddScoped<IUserUiSettingsService, UserUiSettingsService>();
builder.Services.AddScoped<PayrollService>();

// Auth / JWT
var key = Encoding.ASCII.GetBytes(builder.Configuration["JwtSettings:Secret"]);
builder.Services.AddAuthentication(auth =>
{
    auth.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    auth.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(jwt =>
{
    jwt.RequireHttpsMetadata = false;
    jwt.SaveToken = true;
    jwt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = builder.Configuration["JwtSettings:Audience"],
        ValidateLifetime = true
    };
});

var app = builder.Build();

// ==================== Pipeline HTTP ====================

// Swagger (dev y prod)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
    c.SwaggerEndpoint("/swagger/company-docs-v1/swagger.json", "Company Docs v1");
});

// Redirección HTTPS SOLO fuera de Development (evita romper CORS en local)
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

// CORS antes de auth/static/controllers
app.UseCors("AppCors");

// Archivos estáticos: asegurar wwwroot y wwwroot/storage
var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(webRoot);
var storageRoot = Path.Combine(webRoot, "storage");
Directory.CreateDirectory(storageRoot);

// Servir wwwroot/
app.UseStaticFiles();

// Montar /storage → wwwroot/storage
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storageRoot),
    RequestPath = "/storage"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
