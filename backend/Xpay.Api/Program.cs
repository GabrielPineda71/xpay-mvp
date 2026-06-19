using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// CORS — orígenes desde configuración (Cors:AllowedOrigins o env Cors__AllowedOrigins__0 ...)
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
    options.AddPolicy("FrontendCorsPolicy", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()));

var jwtSection = builder.Configuration.GetSection("Jwt");
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
            ClockSkew                = TimeSpan.Zero
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

// Correlation ID — debe ir primero para que todos los logs del request tengan el scope
var enableCorrelationId   = builder.Configuration.GetValue("Observability:EnableCorrelationId",   defaultValue: true);
var enableRequestLogging  = builder.Configuration.GetValue("Observability:EnableRequestLogging",  defaultValue: true);

if (enableCorrelationId)
    app.UseMiddleware<CorrelationIdMiddleware>();

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

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseCors("FrontendCorsPolicy");   // antes de autenticación — requerido para preflight
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
