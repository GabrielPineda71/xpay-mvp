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
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Middleware;
using Xpay.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<XpayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("XpayConnection")));

builder.Services.AddScoped<RegistroUsuarioFinalService>();
builder.Services.AddScoped<RegistroInicialService>();
builder.Services.AddScoped<PerfilService>(); // XPAY-399 — Perfil Fase 2A (mi-perfil self-service)
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<WalletOperacionService>();
builder.Services.AddScoped<QrResolutionService>(); // XPAY-451 — resolución read-only compartida CodigoQr→Comercio/Tienda
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
// XPAY-381 — reconciliación automática server-side de retiros Bre-B reales
// transitorios (ENVIADO_PASSPORT). Requiere que IPassportPaymentClient y
// BrebPaymentService ya estén registrados (ver registro Passport más abajo
// en este archivo) — Scoped, resueltos por scope nuevo en cada barrido.
builder.Services.AddHostedService<BrebPaymentReconciliationService>();
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
// M2.3a / M2.4d — pipeline de riesgo/decisión de Cartera Ordinaria (orquestador
// + consumo M2.4a + decisión M2.4b + materialización M2.4c + reconciliación
// fail-closed). IConsultaRiesgoAutorizacion permanece ligada al stub
// AutorizacionConsultaRiesgoNoDisponible (fail-closed): con él ningún endpoint
// puede alcanzar una llamada real a MiDecisor. Ver CarteraRiesgoRuntimeWiring.
builder.Services.AddCarteraRiesgoRuntime();

// Passport / Bre-B como Servicio — XPAY-272: sólo infraestructura base
// reutilizable (token provider OAuth2 client_credentials + cliente HTTP
// autenticado genérico). NO se registra ni implementa todavía ningún
// endpoint de negocio (resolve-key, payments/breb, customers/accounts,
// Bre-B keys, QR) — eso queda para fases posteriores sobre esta base.
// BrebService/BrebController (retiro simulado, Fase 64) permanecen sin
// cambios funcionales y NO consumen esta infraestructura todavía.
// Fail-closed: sin PASSPORT_BASE_URL/PASSPORT_API_KEY/PASSPORT_API_SECRET
// configurados, cualquier intento de uso lanza PassportConfigurationException
// ANTES de cualquier llamada HTTP. El arranque NUNCA resuelve estos valores.
builder.Services.AddSingleton<
    Xpay.Api.Integrations.Passport.IPassportTokenProvider,
    Xpay.Api.Integrations.Passport.PassportTokenProvider>();
builder.Services.AddSingleton<
    Xpay.Api.Integrations.Passport.IPassportHttpClient,
    Xpay.Api.Integrations.Passport.PassportHttpClient>();

// XPAY-371 — primer cliente de negocio Passport registrado en DI de la
// aplicación viva: IPassportKeyClient (Resolve Key), consumido por
// BrebService.ResolverMiLlaveAsync. Mismo lifetime (Singleton) y mismo
// patrón que IPassportHttpClient/IPassportTokenProvider: PassportKeyClient
// no tiene estado propio, es un wrapper puro sobre IPassportHttpClient (ya
// singleton) — no introduce ningún HttpClient/config adicional. Fail-closed
// sin cambios: sigue dependiendo de PASSPORT_BASE_URL/API_KEY/API_SECRET
// (vía IPassportHttpClient) y, para Resolve Key específicamente, de
// PASSPORT_CUSTOMER_ID (ver PassportOptions.EnvOperationalCustomerId) —
// ninguno de los dos está configurado en ningún ambiente todavía, así que
// ResolverMiLlaveAsync no puede completar una llamada real hasta que se
// autorice y configure explícitamente (fuera de alcance de XPAY-371).
//
// IPassportCustomerAccountClient NO se registra en este ticket — XPAY-371
// sólo integra Resolve Key; el cliente de customer/account (saldo de
// cuenta operativa) no es una dependencia de ResolverMiLlaveAsync y
// registrarlo ahora ampliaría el alcance sin necesidad (ver XPAY-370
// Bloque B, todavía no autorizado).
builder.Services.AddSingleton<
    Xpay.Api.Integrations.Passport.IPassportKeyClient,
    Xpay.Api.Integrations.Passport.PassportKeyClient>();

// XPAY-372 — segundo cliente de negocio Passport registrado en DI:
// IPassportCustomerAccountClient (Retrieve Account), consumido por
// CuentaOperativaService.ObtenerCuentaOperativaAsync (GET
// /api/breb/admin/cuenta-operativa). Mismo lifetime/patrón que
// IPassportKeyClient — wrapper puro y sin estado sobre IPassportHttpClient.
// XPAY-372 NO llama LinkMerchantAsync/LinkAccountAsync (creación de
// customer/account) desde ningún código productivo — sólo se consume
// RetrieveAccountAsync, sobre una cuenta que Passport YA aprovisionó
// (ver PassportOptions.EnvOperationalAccountId). Fail-closed sin cambios:
// sigue dependiendo de PASSPORT_BASE_URL/API_KEY/API_SECRET (vía
// IPassportHttpClient) y de PASSPORT_ACCOUNT_ID, ninguno configurado en
// ningún ambiente todavía.
builder.Services.AddSingleton<
    Xpay.Api.Integrations.Passport.IPassportCustomerAccountClient,
    Xpay.Api.Integrations.Passport.PassportCustomerAccountClient>();
builder.Services.AddScoped<Xpay.Api.Services.CuentaOperativaService>();

// XPAY-373 — tercer cliente de negocio Passport registrado en DI:
// IPassportPaymentClient (Bre-B Payment: crear + consultar), consumido por
// BrebPaymentService. Mismo lifetime/patrón que IPassportKeyClient/
// IPassportCustomerAccountClient. Fail-closed sin cambios: sigue
// dependiendo de PASSPORT_BASE_URL/API_KEY/API_SECRET (vía
// IPassportHttpClient) y de PASSPORT_ACCOUNT_ID (vía
// BrebPaymentRequestBuilder), ninguno configurado en ningún ambiente
// todavía — ninguna llamada real es posible hasta que se autorice y
// configure explícitamente en un ticket futuro (XPAY-374).
builder.Services.AddSingleton<
    Xpay.Api.Integrations.Passport.IPassportPaymentClient,
    Xpay.Api.Integrations.Passport.PassportPaymentClient>();
builder.Services.AddScoped<Xpay.Api.Services.BrebPaymentService>();

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

// XPAY-400 — política dedicada para POST /api/auth/cambiar-clave, NO
// reutiliza LoginPolicy: LoginPolicy particiona por IP remota, apropiado
// para un endpoint anónimo (es la única señal disponible antes de
// autenticar). cambiar-clave es un endpoint AUTENTICADO — particionar por
// idUsuario es más preciso (evita que usuarios distintos detrás del mismo
// NAT/IP corporativa compartan un único cupo, y evita que una sola cuenta
// evada el límite rotando de IP). Límites deliberadamente más
// conservadores que el login (una cuenta legítima rara vez necesita
// cambiar su contraseña más de un par de veces seguidas) — configurables,
// mismo patrón que LoginPermitLimit/LoginWindowSeconds/LoginQueueLimit.
var cambiarClavePermitLimit   = rlSection.GetValue("CambiarClavePermitLimit",   defaultValue: 5);
var cambiarClaveWindowSeconds = rlSection.GetValue("CambiarClaveWindowSeconds", defaultValue: 300);
var cambiarClaveQueueLimit    = rlSection.GetValue("CambiarClaveQueueLimit",    defaultValue: 0);

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

    options.AddPolicy("CambiarClavePolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            // idUsuario del JWT ya autenticado; si por algún motivo no está
            // presente (no debería, el endpoint exige [Authorize]), cae a IP
            // remota como último recurso — nunca deja la partición vacía.
            partitionKey: httpContext.User.FindFirst("idUsuario")?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = cambiarClavePermitLimit,
                Window               = TimeSpan.FromSeconds(cambiarClaveWindowSeconds),
                QueueLimit           = cambiarClaveQueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        var correlationId = context.HttpContext.Items.TryGetValue("CorrelationId", out var cid)
            ? cid?.ToString() ?? string.Empty
            : string.Empty;

        context.HttpContext.Response.StatusCode  = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";

        // XPAY-400 — ahora hay más de una policy con ventanas distintas
        // (LoginPolicy=loginWindowSeconds, CambiarClavePolicy=
        // cambiarClaveWindowSeconds): se usa el Retry-After real que calcula
        // el propio limiter (metadata estándar de System.Threading.RateLimiting)
        // en vez de asumir loginWindowSeconds para cualquier policy — antes de
        // XPAY-400 solo existía LoginPolicy, así que ambos valores siempre
        // coincidían; ahora ya no es seguro asumirlo.
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? ((int)retryAfter.TotalSeconds).ToString()
            : loginWindowSeconds.ToString(); // fallback conservador si el limiter no expone la metadata
        context.HttpContext.Response.Headers["Retry-After"] = retryAfterSeconds;

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

// XPAY-385 — bootstrap idempotente de la fila de cuentas_operativas
// (CAPA 2). Corre una sola vez por arranque del proceso, antes de servir
// tráfico. Nunca lee/loguea PASSPORT_ACCOUNT_ID — solo su fingerprint.
await OperationalAccountBootstrapper.EnsureSeededAsync(app.Services, app.Logger);

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
