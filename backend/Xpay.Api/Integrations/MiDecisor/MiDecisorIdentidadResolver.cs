using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;
using Xpay.Api.Services;

namespace Xpay.Api.Integrations.MiDecisor;

public interface IMiDecisorIdentidadResolver
{
    Task<MiDecisorIdentidad?> ResolverAsync(long idUsuario, CancellationToken ct = default);
}

// Sensitive in-memory domain snapshot, deliberately not a record/response DTO.
public sealed class MiDecisorIdentidad
{
    public required long IdUsuario { get; init; }
    public required long IdPersona { get; init; }
    public required long IdKycVerificacion { get; init; }
    public required string TipoDocumento { get; init; }
    public required string NumeroDocumento { get; init; }
    public required string TipoVeriff { get; init; }
    public required string PrimerApellido { get; init; }

    internal Persona ParaConsulta() => new()
    {
        IdPersona = IdPersona, TipoDocumento = TipoDocumento, NumeroDocumento = NumeroDocumento,
        PrimerApellido = PrimerApellido,
    };
}

// Scoped cache: context and production gate share one coherent GET per HTTP
// request. Nothing here consolidates legal identity or writes to any table.
public sealed class MiDecisorIdentidadResolver(
    XpayDbContext db, IHttpClientFactory http, IConfiguration configuration,
    ILogger<MiDecisorIdentidadResolver> logger) : IMiDecisorIdentidadResolver
{
    public const string VeriffClientName = "MiDecisorIdentidadVeriff";
    private readonly Dictionary<long, Task<MiDecisorIdentidad?>> _cache = new();

    public Task<MiDecisorIdentidad?> ResolverAsync(long idUsuario, CancellationToken ct = default)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(idUsuario, out var task))
                _cache[idUsuario] = task = ResolverUnaVezAsync(idUsuario, ct);
            return task;
        }
    }

    private async Task<MiDecisorIdentidad?> ResolverUnaVezAsync(long idUsuario, CancellationToken ct)
    {
        try
        {
            var u = await db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == idUsuario)
                .Select(x => new { x.IdUsuario, x.IdPersona, x.Estado, x.EstadoKycActual, x.EmailVerificado, x.RequiereCambioClave }).SingleOrDefaultAsync(ct);
            if (u is null || u.Estado != "ACTIVO" || !u.EmailVerificado || u.RequiereCambioClave || u.EstadoKycActual != "APROBADO") return null;
            var p = await db.Personas.AsNoTracking().SingleOrDefaultAsync(x => x.IdPersona == u.IdPersona, ct);
            var actuales = await db.KycVerificaciones.AsNoTracking().Where(x => x.IdUsuario == idUsuario && x.EsActual).Take(2).ToListAsync(ct);
            if (p is null || p.Estado != "ACTIVA" || !p.IdentidadVerificada || p.IdentidadVerificadaProveedor != "VERIFF" || actuales.Count != 1) return null;
            var k = actuales[0];
            if (k.IdPersona != p.IdPersona || k.Proveedor != "VERIFF" || k.EstadoKyc != "APROBADO" || k.Decision != "approved" ||
                string.IsNullOrWhiteSpace(k.SessionId) || string.IsNullOrWhiteSpace(k.AttemptIdVeriff) || !k.DecisionTimeVeriff.HasValue) return null;
            if (string.IsNullOrWhiteSpace(p.PrimerApellido) || !PrimerApellidoDeclaradoReglas.CoincideConCompleto(p.PrimerApellido, p.ApellidoVerificadoCompleto, p.SegundoApellido)) return null;

            var key = configuration["VERIFF_API_KEY"];
            var secret = configuration["VERIFF_SHARED_SECRET"];
            if (configuration["VERIFF_BASE_URL"]?.TrimEnd('/') != "https://api-saas.veriff.com" || !Resuelto(key) || !Resuelto(secret)) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api-saas.veriff.com/v1/sessions/" + Uri.EscapeDataString(k.SessionId) + "/decision");
            request.Headers.TryAddWithoutValidation("X-AUTH-CLIENT", key);
            request.Headers.TryAddWithoutValidation("X-HMAC-SIGNATURE", Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret!.Trim()), Encoding.UTF8.GetBytes(k.SessionId))).ToLowerInvariant());
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.CreateClient(VeriffClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return null; // never read error bodies
            var root = await MiDecisorJsonTransport.ReadJsonAsync<JsonElement>(response.Content,
                new JsonSerializerOptions(), 262_144, linked.Token).ConfigureAwait(false);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) || status.GetString() != "success" ||
                !root.TryGetProperty("verification", out var verification)) return null;
            var decision = KycService.ParseVeriffDecision(verification);
            var identity = CrearDesdeDecision(u.IdUsuario, p, k, decision);
            if (identity is null) return null;

            // An approval/identity changed by a webhook during the GET never
            // silently replaces this snapshot with another person's document.
            var current = await db.KycVerificaciones.AsNoTracking().Where(x => x.IdUsuario == idUsuario && x.EsActual).Take(2).ToListAsync(ct);
            var currentPerson = await db.Personas.AsNoTracking().SingleOrDefaultAsync(x => x.IdPersona == p.IdPersona, ct);
            if (current.Count != 1 || current[0].IdKycVerificacion != k.IdKycVerificacion || currentPerson is null ||
                CrearDesdeDecision(u.IdUsuario, currentPerson, current[0], decision) is not { } confirmed ||
                confirmed.TipoDocumento != identity.TipoDocumento || confirmed.NumeroDocumento != identity.NumeroDocumento ||
                confirmed.PrimerApellido != identity.PrimerApellido) return null;
            return identity;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            logger.LogWarning("midecisor.identity: no se pudo acreditar la identidad vigente; no autorizado.");
            return null;
        }
    }

    internal static MiDecisorIdentidad? CrearDesdeDecision(long idUsuario, Persona persona, KycVerificacion sesion, VeriffDecisionParsed? decision)
    {
        if (decision is null || persona.Estado != "ACTIVA" || !persona.IdentidadVerificada || persona.IdentidadVerificadaProveedor != "VERIFF" ||
            sesion.IdUsuario != idUsuario || sesion.IdPersona != persona.IdPersona || !SincronizacionDocumentoVeriffService.DecisionCoincide(sesion, persona, decision) ||
            string.IsNullOrWhiteSpace(persona.PrimerApellido) || !PrimerApellidoDeclaradoReglas.CoincideConCompleto(persona.PrimerApellido, persona.ApellidoVerificadoCompleto, persona.SegundoApellido)) return null;
        var legal = TipoDocumentoVeriffMapper.Mapear(decision.DocumentType, decision.DocumentCountry, decision.DocumentNumber);
        if (legal.Resultado != TipoDocumentoVeriffMapper.Resultado.Mapeado || legal.NumeroDocumento is not { Length: >= 3 and <= 13 } number || !number.All(char.IsAsciiDigit)) return null;
        var tipo = legal.TipoDocumento == "PASAPORTE" ? "PAS" : legal.TipoDocumento;
        if (!TipoDocumentoMiDecisorMapper.TryMapPersonaNatural(tipo, out _)) return null;
        // Existing legal data may be absent (legacy), but conflicting legal
        // data requires review. No residence-country fallback or auto-fill.
        var declaradoTipo = persona.TipoDocumento?.Trim().ToUpperInvariant();
        if (declaradoTipo == "PAS") declaradoTipo = "PASAPORTE";
        if ((!string.IsNullOrWhiteSpace(persona.TipoDocumento) || !string.IsNullOrWhiteSpace(persona.NumeroDocumento)) &&
            (!string.Equals(declaradoTipo, legal.TipoDocumento, StringComparison.Ordinal) ||
             TipoDocumentoVeriffMapper.NumeroComparable(persona.NumeroDocumento) != number)) return null;
        return new MiDecisorIdentidad
        {
            IdUsuario = idUsuario, IdPersona = persona.IdPersona, IdKycVerificacion = sesion.IdKycVerificacion,
            TipoDocumento = tipo!, NumeroDocumento = number, TipoVeriff = decision.DocumentType!,
            PrimerApellido = PrimerApellidoDeclaradoReglas.Normalizar(persona.PrimerApellido),
        };
    }

    private static bool Resuelto(string? value) => !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase);
}
