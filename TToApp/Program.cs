using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Stripe;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using TToApp.Controllers;
using TToApp.Helpers;
using TToApp.Model;
using TToApp.Services;
using TToApp.Services.Audit;
using TToApp.Services.Auth;
using TToApp.Services.CommunicationRecipient;
using TToApp.Services.EarlyWarnings;
using TToApp.Services.Notifications;
using TToApp.Services.Payroll;
using TToApp.Services.Scheduled;
using TToApp.Services.Settings;
using TToApp.Services.Sms;
using TToApp.Services.Vehicle;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// STRIPE
// =====================================================

StripeConfiguration.ApiKey =
    builder.Configuration["Stripe:SecretKey"];

// =====================================================
// CONTROLLERS + JSON
// =====================================================

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());

        options.JsonSerializerOptions.NumberHandling =
            JsonNumberHandling.AllowReadingFromString;
    });

// =====================================================
// SWAGGER / OPENAPI
// =====================================================

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TTO API",
        Version = "v1",
        Description = "TTO Logistics API"
    });

    // Evita conflictos cuando existen DTOs o clases internas
    // con el mismo nombre.
    options.CustomSchemaIds(type =>
        type.FullName?.Replace("+", ".") ?? type.Name);

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",

            Description =
                "Escribe el token JWT. No es necesario escribir la palabra Bearer.",

            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

    options.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
});

// =====================================================
// CORS
// =====================================================

var allowedOrigins =
    builder.Configuration
        .GetSection("CorsSettings:AllowedOrigins")
        .Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            // Evita que el servicio falle si todavía no se
            // configuraron orígenes.
            policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddMemoryCache();

// =====================================================
// DATABASE / EF CORE
// =====================================================

var connectionString =
    builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Default' not found. " +
        "Check appsettings.{Environment}.json or environment variables.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// =====================================================
// EMAIL
// =====================================================

builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

builder.Services.AddTransient<EmailService>();

// =====================================================
// AUTOMAPPER
// =====================================================

builder.Services.AddSingleton<IMapper>(_ =>
{
    var mapperConfiguration = new MapperConfiguration(config =>
    {
        config.AddProfile<MappingProfile>();
    });

    return mapperConfiguration.CreateMapper();
});

// =====================================================
// SMS SERVICE
// =====================================================

builder.Services.AddHttpClient<ITtoSmsService, TtoSmsService>(
    (serviceProvider, client) =>
    {
        var configuration =
            serviceProvider.GetRequiredService<IConfiguration>();

        var baseUrl =
            configuration["RecruitAgent:BaseUrl"];

        var apiKey =
            configuration["BotApiKey"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "RecruitAgent:BaseUrl is missing.");
        }

        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var smsBaseUri))
        {
            throw new InvalidOperationException(
                $"RecruitAgent:BaseUrl is not a valid absolute URL: {baseUrl}");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "BotApiKey is missing.");
        }

        client.BaseAddress = smsBaseUri;
        client.Timeout = TimeSpan.FromSeconds(30);

        client.DefaultRequestHeaders.Add(
            "X-Agent-Key",
            apiKey);

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));
    });

// =====================================================
// APPLICATION SERVICES
// =====================================================

builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddScoped<
    IApplicantContactService,
    ApplicantContactService>();

builder.Services.AddHostedService<RDMonitorService>();

builder.Services.AddScoped<WhatsAppService>();

builder.Services.AddScoped<IJwtService, JwtService>();

builder.Services.AddSingleton<
    ISensitiveDataProtector,
    SensitiveDataProtector>();

builder.Services.AddScoped<
    IUserUiSettingsService,
    UserUiSettingsService>();

builder.Services.AddScoped<PayrollService>();

builder.Services.AddScoped<PayRunApprovedSender>();

builder.Services.AddScoped<IVehicleService, VehicleService>();

builder.Services.AddScoped<
    IEarlyWarningService,
    EarlyWarningService>();

builder.Services.AddScoped<
    IEarlyWarningNotificationService,
    EarlyWarningNotificationService>();

builder.Services.AddScoped<
    ICommunicationRecipientService,
    CommunicationRecipientService>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<AuditService>();

// =====================================================
// DATA PROTECTION
// =====================================================

var dataProtectionKeysPath =
    builder.Configuration["DataProtection:KeysPath"]
    ?? "/var/ttoapp/keys";

Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services
    .AddDataProtection()
    .SetApplicationName("TToApp")
    .PersistKeysToFileSystem(
        new DirectoryInfo(dataProtectionKeysPath));

// =====================================================
// RECRUIT AGENT SERVICE
// =====================================================

builder.Services.AddHttpClient<
    IRecruitAgentService,
    RecruitAgentService>(
    (serviceProvider, client) =>
    {
        var configuration =
            serviceProvider.GetRequiredService<IConfiguration>();

        var baseUrl =
            configuration["RecruitAgent:BaseUrl"];

        var agentKey =
            configuration["RecruitAgent:AgentKey"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "RecruitAgent:BaseUrl is missing.");
        }

        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var recruitAgentBaseUri))
        {
            throw new InvalidOperationException(
                $"RecruitAgent:BaseUrl is not a valid absolute URL: {baseUrl}");
        }

        if (string.IsNullOrWhiteSpace(agentKey))
        {
            throw new InvalidOperationException(
                "RecruitAgent:AgentKey is missing.");
        }

        client.BaseAddress = recruitAgentBaseUri;
        client.Timeout = TimeSpan.FromSeconds(30);

        client.DefaultRequestHeaders.Add(
            "X-Agent-Key",
            agentKey);

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));
    });

// =====================================================
// AUTHENTICATION / JWT
// =====================================================

var jwtSecret =
    builder.Configuration["JwtSettings:Secret"];

if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException(
        "JwtSettings:Secret is missing.");
}

var jwtIssuer =
    builder.Configuration["JwtSettings:Issuer"];

var jwtAudience =
    builder.Configuration["JwtSettings:Audience"];

if (string.IsNullOrWhiteSpace(jwtIssuer))
{
    throw new InvalidOperationException(
        "JwtSettings:Issuer is missing.");
}

if (string.IsNullOrWhiteSpace(jwtAudience))
{
    throw new InvalidOperationException(
        "JwtSettings:Audience is missing.");
}

var jwtKey =
    Encoding.UTF8.GetBytes(jwtSecret);

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,

                IssuerSigningKey =
                    new SymmetricSecurityKey(jwtKey),

                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateLifetime = true,

                ClockSkew = TimeSpan.FromMinutes(1)
            };
    });

// =====================================================
// BUILD
// =====================================================

var app = builder.Build();

// =====================================================
// SWAGGER UI
// =====================================================

app.UseSwagger();

app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";

    options.SwaggerEndpoint(
        "/swagger/v1/swagger.json",
        "TTO API v1");

    options.DocumentTitle =
        "TTO Logistics API";

    options.DisplayRequestDuration();
});

// =====================================================
// HTTP PIPELINE
// =====================================================

// En desarrollo puedes trabajar por HTTP.
// En producción se redirige a HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseCors("AppCors");

// =====================================================
// STATIC FILES
// =====================================================

var webRoot =
    app.Environment.WebRootPath
    ?? Path.Combine(
        app.Environment.ContentRootPath,
        "wwwroot");

Directory.CreateDirectory(webRoot);

var storageRoot =
    Path.Combine(webRoot, "storage");

Directory.CreateDirectory(storageRoot);

app.UseStaticFiles();

app.UseStaticFiles(
    new StaticFileOptions
    {
        FileProvider =
            new PhysicalFileProvider(storageRoot),

        RequestPath = "/storage"
    });

// =====================================================
// AUTHORIZATION
// =====================================================

app.UseAuthentication();
app.UseAuthorization();

// =====================================================
// ENDPOINTS
// =====================================================

app.MapControllers();

app.MapCompanyRevenueEndpoints();

// =====================================================
// RUN
// =====================================================

app.Run();