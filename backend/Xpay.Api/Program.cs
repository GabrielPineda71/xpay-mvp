using System.Diagnostics;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Xpay.Api.Data;
using Xpay.Api.Middleware;
using Xpay.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<XpayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("XpayConnection")));

builder.Services.AddScoped<RegistroUsuarioFinalService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<WalletOperacionService>();
builder.Services.AddScoped<PagoQrService>();
builder.Services.AddScoped<LiquidacionComercioService>();
builder.Services.AddScoped<RetiroComercioService>();
builder.Services.AddScoped<ReportesService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<AuditLogService>();

// CORS — orígenes desde configuración (Cors:AllowedOrigins o env Cors__AllowedOrigins__0 ...)
// Guard: en ambientes no Development, si no hay orígenes configurados, falla rápido en startup.
var configuredOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

string[] corsOrigins;
if (configuredOrigins.Length == 0)
{
    if (builder.Environment.IsDevelopment())
    {
        corsOrigins = new[]
        {
            "http://localhost:5173", "https://localhost:5173",
            "http://localhost:3000", "https://localhost:3000"
        };
    }
    else
    {
        throw new InvalidOperationException(
            "Cors:AllowedOrigins must be configured outside Development. " +
            "Set at least one allowed origin via Cors__AllowedOrigins__0 environment variable.");
    }
}
else
{
    corsOrigins = configuredOrigins;
}

builder.Services.AddCors(options =>
    options.AddPolicy("FrontendCorsPolicy", policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()));

var jwtSection       = builder.Configuration.GetSection("Jwt");
var clockSkewSeconds = jwtSection.GetValue("ClockSkewSeconds", defaultValue: 60);
if (clockSkewSeconds < 0) clockSkewSeconds = 60;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                                         Encoding.UTF8.GetBytes(jwtSection["Key"]!)),
            ValidateIssuer           = true,
            ValidIssuer              = jwtSection["Issuer"],
            ValidateAudience         = true,
            ValidAudience            = jwtSection["Audience"],
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(clockSkewSeconds)
        };
    });

// Rate limiting — FixedWindow por IP para endpoints sensibles (login)
var rlSection          = builder.Configuration.GetSection("RateLimiting");
var enableRateLimiting = rlSection.GetValue("EnableRateLimiting", defaultValue: true);
var loginPermitLimit   = rlSection.GetValue("LoginPermitLimit",   defaultValue: 20);
var loginWindowSeconds = rlSection.GetValue("LoginWindowSeconds", defaultValue: 60);
var loginQueueLimit    = rlSection.GetValue("LoginQueueLimit",    defaultValue: 0);

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("LoginPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = loginPermitLimit,
                Window               = TimeSpan.FromSeconds(loginWindowSeconds),
                QueueLimit           = loginQueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        var correlationId = context.HttpContext.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString() ?? string.Empty
            : string.Empty;

        context.HttpContext.Response.StatusCode  = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.Headers["Retry-After"] = loginWindowSeconds.ToString();

        await context.HttpContext.Response.WriteAsync(
            $"{{\"error\":\"rate_limit_exceeded\",\"message\":\"Too many requests. Please try again later.\",\"correlationId\":\"{correlationId}\"}}",
            cancellationToken);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "XPAY API",
        Version     = "0.1.0-mvp",
        Description = "API del sistema de pagos XPAY. Endpoints protegidos requieren Bearer JWT."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        Description  = "Ingrese el token JWT como: Bearer {token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Startup: log CORS origins (no son secretos — son URLs públicas del frontend)
app.Logger.LogInformation(
    "CORS: FrontendCorsPolicy — allowed origins: {Origins}",
    string.Join(", ", corsOrigins));

// Correlation ID — debe ir primero para que todos los logs del request tengan el scope
var enableCorrelationId      = builder.Configuration.GetValue("Observability:EnableCorrelationId",             defaultValue: true);
var enableRequestLogging     = builder.Configuration.GetValue("Observability:EnableRequestLogging",             defaultValue: true);
var enableGlobalErrorHandler = builder.Configuration.GetValue("ErrorHandling:EnableGlobalErrorHandler",        defaultValue: true);

if (enableCorrelationId)
    app.UseMiddleware<CorrelationIdMiddleware>();

// Error handling global — después de CorrelationId (correlationId disponible) y antes de todo lo demás
if (enableGlobalErrorHandler)
    app.UseMiddleware<ErrorHandlingMiddleware>();

// Request logging básico — no registra Authorization, body, passwords ni connection strings
if (enableRequestLogging)
{
    app.Use(async (context, next) =>
    {
        var sw    = Stopwatch.StartNew();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var method = context.Request.Method;
        var path   = context.Request.Path.Value ?? string.Empty;

        await next();

        sw.Stop();
        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString() ?? "-"
            : "-";

        logger.LogInformation(
            "HTTP {Method} {Path} responded {StatusCode} in {Elapsed}ms | cid={CorrelationId}",
            method, path, context.Response.StatusCode, sw.ElapsedMilliseconds, correlationId);
    });
}

// Security headers básicos — no incluye CSP ni HSTS (ver docs/PREPRODUCTION_GAPS_AND_REAL_MONEY_CHECKLIST.md)
var enableSecurityHeaders = builder.Configuration.GetValue("SecurityHeaders:EnableSecurityHeaders", defaultValue: true);
if (enableSecurityHeaders)
    app.UseMiddleware<SecurityHeadersMiddleware>();

// Swagger — habilitado por config (ApiDocs:EnableSwagger) o por defecto solo en Development
var enableSwaggerConfig = builder.Configuration.GetValue<bool?>("ApiDocs:EnableSwagger");
var enableSwagger       = enableSwaggerConfig ?? app.Environment.IsDevelopment();

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("FrontendCorsPolicy");   // antes de autenticación — requerido para preflight
if (enableRateLimiting)
    app.UseRateLimiter();            // después de CORS, antes de autenticación
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
