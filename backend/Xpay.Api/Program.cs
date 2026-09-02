using System.Diagnostics;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Xpay.Api.Authorization;
using Xpay.Api.Data;
using Xpay.Api.Middleware;
using Xpay.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<XpayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("XpayConnection")));

builder.Services.AddScoped<RegistroUsuarioFinalService>();
builder.Services.AddScoped<RegistroInicialService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<WalletOperacionService>();
builder.Services.AddScoped<PagoQrService>();
builder.Services.AddScoped<LiquidacionComercioService>();
builder.Services.AddScoped<RetiroComercioService>();
builder.Services.AddScoped<ReportesService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<UsuarioAdminService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<KycService>();
builder.Services.AddScoped<BrebService>();
builder.Services.AddScoped<LibranzaService>();
builder.Services.AddScoped<LibranzaEmpleadosService>();
builder.Services.AddScoped<LibranzaAnticipoService>();
builder.Services.AddScoped<ComercioAliadoService>();
builder.Services.AddScoped<ComercioDisponibilidadService>();
builder.Services.AddScoped<ComercioScopeService>();
builder.Services.AddScoped<ComercioLiquidacionAutomaticaService>();
builder.Services.AddScoped<CarteraOrdinariaService>();
builder.Services.AddScoped<WalletRecargaComercioService>();
builder.Services.AddScoped<WalletLiquidacionRecaudoComercioService>();
builder.Services.AddScoped<WalletCierreDiarioComercioService>();
builder.Services.AddScoped<WalletCajaComercioService>();
builder.Services.AddScoped<CatalogoGeograficoService>();
builder.Services.AddHostedService<CajaVencidaSchedulerService>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 6 * 1024 * 1024; // 6 MB global — per-endpoint can restrict further
});
builder.Services.AddHttpClient();

// MiDecisor / DataCrédito — M2.1: sólo el token provider (auth OAuth2 +
// cache en memoria). NO se registra IMiDecisorClient todavía; la integración
// real permanece inactiva. El provider hace fail-closed cuando se invoca sin
// configuración válida — el arranque NUNCA resuelve BASE_URL ni credenciales.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<
    Xpay.Api.Integrations.MiDecisor.IMiDecisorTokenProvider,
    Xpay.Api.Integrations.MiDecisor.MiDecisorTokenProvider>();
// M2.2 — query client (consulta de riesgo PN). Depende del token provider
// vía la abstracción; NO conoce credenciales; NO hace llamadas al arranque.
builder.Services.AddSingleton<
    Xpay.Api.Integrations.MiDecisor.IMiDecisorClient,
    Xpay.Api.Integrations.MiDecisor.MiDecisorClient>();
// M2.3a — orquestación estructural Cartera ↔ MiDecisor. Ningún endpoint /
// scheduler la invoca, y el consentimiento runtime devuelve SIEMPRE false:
// dos barreras independientes contra una consulta real.
builder.Services.AddScoped<Xpay.Api.Services.CarteraConsultaRiesgoService>();
builder.Services.AddScoped<
    Xpay.Api.Services.ICarteraConsultaRiesgoStore,
    Xpay.Api.Services.CarteraConsultaRiesgoStore>();
builder.Services.AddScoped<
    Xpay.Api.Integrations.MiDecisor.IConsultaRiesgoAutorizacion,
    Xpay.Api.Integrations.MiDecisor.AutorizacionConsultaRiesgoNoDisponible>();

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

// Fase USUARIOS-ADMIN-5: ClaveVigenteRequirement se agrega a la DefaultPolicy
// — se combina automáticamente con todo [Authorize]/[Authorize(Roles=...)] sin
// política explícita en TODA la aplicación (Wallet, Comercio, KYC, BREB,
// Empresa, etc.), sin tocar esos controladores. Bloquea el acceso mientras
// usuarios.requiere_cambio_clave = true, verificado en vivo contra BD en cada
// request. POST /api/auth/cambiar-clave-obligatoria usa la política
// "SoloAutenticado" (no se combina con DefaultPolicy) para quedar exento.
builder.Services.AddScoped<IAuthorizationHandler, ClaveVigenteAuthorizationHandler>();

// KYC-GATING-001: policy nombrada de opt-in — NO se agrega a DefaultPolicy.
// Se combina (AND) con el [Authorize]/[Authorize(Roles=...)] ya existente
// solo en los endpoints financieros que declaran explícitamente
// [Authorize(Policy = "KycAprobado")]. Ver Authorization/KycAprobadoRequirement.cs.
builder.Services.AddScoped<IAuthorizationHandler, KycAprobadoAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, KycAuthorizationResultHandler>();
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(options.DefaultPolicy)
        .AddRequirements(new ClaveVigenteRequirement())
        .Build();
    options.AddPolicy("SoloAutenticado", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("KycAprobado", policy => policy.AddRequirements(new KycAprobadoRequirement()));
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

// HTTPS / HSTS — configurable por ambiente (Fase 46)
var httpsSection           = builder.Configuration.GetSection("Https");
var enableHttpsRedirection = httpsSection.GetValue("EnableHttpsRedirection", defaultValue: true);
var enableHsts             = httpsSection.GetValue("EnableHsts",              defaultValue: false);
var hstsMaxAgeDays         = httpsSection.GetValue("HstsMaxAgeDays",          defaultValue: 30);
if (hstsMaxAgeDays <= 0) hstsMaxAgeDays = 30;

builder.Services.AddHsts(options =>
{
    options.MaxAge            = TimeSpan.FromDays(hstsMaxAgeDays);
    options.Preload           = false;
    options.IncludeSubDomains = false;
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

// Security headers básicos — no incluye CSP (HSTS gestionado vía Https:EnableHsts; ver docs/PREPRODUCTION_GAPS_AND_REAL_MONEY_CHECKLIST.md)
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

// HTTPS/HSTS — HSTS nunca activo en Development aunque la config lo pida
if (enableHsts && !app.Environment.IsDevelopment())
    app.UseHsts();
if (enableHttpsRedirection)
    app.UseHttpsRedirection();
app.UseCors("FrontendCorsPolicy");   // antes de autenticación — requerido para preflight
if (enableRateLimiting)
    app.UseRateLimiter();            // después de CORS, antes de autenticación
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
