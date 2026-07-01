using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class BrebService
{
    private readonly XpayDbContext          _db;
    private readonly ILogger<BrebService>   _logger;

    private static readonly HashSet<string> ValidKeyTypes =
        new(StringComparer.OrdinalIgnoreCase) { "ID", "PHONE", "EMAIL", "ALPHA", "BCODE" };

    private static readonly HashSet<string> ValidLlaveStates =
        new(StringComparer.OrdinalIgnoreCase) { "VALIDADA", "RECHAZADA" };

    public BrebService(XpayDbContext db, ILogger<BrebService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Passport health-config ────────────────────────────────────────────
    public PassportHealthResponse GetHealthConfig(IConfiguration config) => new()
    {
        PassportBaseUrl       = !string.IsNullOrWhiteSpace(config["PASSPORT_BASE_URL"]),
        PassportApiKey        = !string.IsNullOrWhiteSpace(config["PASSPORT_API_KEY"]),
        PassportApiSecret     = !string.IsNullOrWhiteSpace(config["PASSPORT_API_SECRET"]),
        PassportWebhookSecret = !string.IsNullOrWhiteSpace(config["PASSPORT_WEBHOOK_SECRET"]),
    };

    // ── Llave Bre-B — USUARIO ─────────────────────────────────────────────
    public async Task<MiLlaveResponse?> GetMiLlaveAsync(long idPersona)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA");
        if (wallet is null) return null;

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == wallet.IdWallet && l.EsActiva);
        return llave is null ? null : ToLlaveResponse(llave);
    }

    public async Task<MiLlaveResponse> RegistrarLlaveAsync(long idPersona, long idUsuario, RegistrarLlaveRequest req)
    {
        ValidateKeyType(req.KeyType);
        ValidateKeyValue(req.KeyType, req.KeyValue);

        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
            ?? throw new InvalidOperationException("No se encontró wallet activa para este usuario.");

        return await UpsertLlave(wallet.IdWallet, "USUARIO", idUsuario, null, req, idUsuario);
    }

    // ── Llave Bre-B — COMERCIO ────────────────────────────────────────────
    public async Task<MiLlaveResponse?> GetLlaveComercioAsync(long idComercio)
    {
        var comercio = await _db.Comercios.FirstOrDefaultAsync(
            c => c.IdComercio == idComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("Comercio no encontrado o inactivo.");

        if (comercio.IdWalletComercio is null)
            throw new InvalidOperationException("El comercio no tiene wallet asignada.");

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == comercio.IdWalletComercio.Value && l.EsActiva);
        return llave is null ? null : ToLlaveResponse(llave);
    }

    public async Task<MiLlaveResponse> RegistrarLlaveComercioAsync(long idComercio, long idUsuario, RegistrarLlaveRequest req)
    {
        ValidateKeyType(req.KeyType);
        ValidateKeyValue(req.KeyType, req.KeyValue);

        var comercio = await _db.Comercios.FirstOrDefaultAsync(
            c => c.IdComercio == idComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("Comercio no encontrado o inactivo.");

        if (comercio.IdWalletComercio is null)
            throw new InvalidOperationException("El comercio no tiene wallet asignada.");

        return await UpsertLlave(comercio.IdWalletComercio.Value, "COMERCIO", null, idComercio, req, idUsuario);
    }

    // ── Listar todas las llaves (Admin) ──────────────────────────────────
    public async Task<List<AdminLlaveResponse>> GetAdminLlavesAsync() =>
        await _db.PassportBrebLlaves
            .OrderByDescending(l => l.FechaRegistro)
            .Select(l => new AdminLlaveResponse
            {
                IdBrebLlave     = l.IdBrebLlave,
                TipoSujeto      = l.TipoSujeto,
                IdUsuario       = l.IdUsuario,
                IdComercio      = l.IdComercio,
                IdWallet        = l.IdWallet,
                KeyType         = l.KeyType,
                KeyValueMasked  = l.KeyValueMasked,
                Estado          = l.Estado,
                FechaRegistro   = l.FechaRegistro,
                FechaValidacion = l.FechaValidacion,
                EsActiva        = l.EsActiva,
            })
            .ToListAsync();

    // ── Simular validación (Admin/QA) ─────────────────────────────────────
    public async Task<string> SimularValidacionAsync(SimularValidacionLlaveRequest req, long adminId)
    {
        if (!ValidLlaveStates.Contains(req.Estado))
            throw new InvalidOperationException($"Estado inválido: '{req.Estado}'. Permitidos: VALIDADA, RECHAZADA.");

        var llave = await _db.PassportBrebLlaves.FindAsync(req.IdBrebLlave)
            ?? throw new InvalidOperationException($"Llave {req.IdBrebLlave} no encontrada.");

        llave.Estado              = req.Estado.ToUpperInvariant();
        llave.FechaValidacion     = DateTime.UtcNow;
        llave.FechaActualizacion  = DateTime.UtcNow;
        llave.UpdatedByUsuario    = adminId;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Bre-B llave {IdLlave} simulada a estado {Estado} por admin {Admin}",
            llave.IdBrebLlave, llave.Estado, adminId);
        return $"Llave {llave.IdBrebLlave} actualizada a {llave.Estado}.";
    }

    // ── Retiros — USUARIO ─────────────────────────────────────────────────
    public async Task<List<BrebRetiroResponse>> GetMisRetirosAsync(long idPersona)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA");
        if (wallet is null) return [];

        return await BuildRetirosResponse(wallet.IdWallet);
    }

    public async Task<BrebRetiroResponse> SimularRetiroAsync(long idPersona, long idUsuario, SimularRetiroRequest req)
    {
        if (req.Valor <= 0)
            throw new InvalidOperationException("El valor del retiro debe ser mayor a cero.");

        var wallet = await _db.Wallets.FirstOrDefaultAsync(
            w => w.IdPersona == idPersona && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA")
            ?? throw new InvalidOperationException("No se encontró wallet activa para este usuario.");

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == wallet.IdWallet && l.EsActiva && l.Estado == "VALIDADA")
            ?? throw new InvalidOperationException("Debes tener una llave Bre-B validada para solicitar un retiro.");

        var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == wallet.IdWallet);
        if (saldo is null || saldo.SaldoDisponible < req.Valor)
            throw new InvalidOperationException(
                $"Saldo insuficiente. Disponible: {saldo?.SaldoDisponible ?? 0:0.00}, solicitado: {req.Valor:0.00}.");

        return await CreateRetiroSimulado(wallet.IdWallet, "USUARIO", idUsuario, null, llave, req.Valor, idUsuario);
    }

    // ── Retiros — COMERCIO ────────────────────────────────────────────────
    public async Task<List<BrebRetiroResponse>> GetRetirosComercioAsync(long idComercio)
    {
        var comercio = await _db.Comercios.FirstOrDefaultAsync(
            c => c.IdComercio == idComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("Comercio no encontrado o inactivo.");

        if (comercio.IdWalletComercio is null) return [];
        return await BuildRetirosResponse(comercio.IdWalletComercio.Value);
    }

    public async Task<BrebRetiroResponse> SimularRetiroComercioAsync(long idComercio, long idUsuario, SimularRetiroRequest req)
    {
        if (req.Valor <= 0)
            throw new InvalidOperationException("El valor del retiro debe ser mayor a cero.");

        var comercio = await _db.Comercios.FirstOrDefaultAsync(
            c => c.IdComercio == idComercio && c.Estado == "ACTIVO")
            ?? throw new InvalidOperationException("Comercio no encontrado o inactivo.");

        if (comercio.IdWalletComercio is null)
            throw new InvalidOperationException("El comercio no tiene wallet asignada.");

        var idWallet = comercio.IdWalletComercio.Value;

        var llave = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == idWallet && l.EsActiva && l.Estado == "VALIDADA")
            ?? throw new InvalidOperationException("El comercio debe tener una llave Bre-B validada para solicitar un retiro.");

        var saldo = await _db.WalletSaldos.FirstOrDefaultAsync(s => s.IdWallet == idWallet);
        if (saldo is null || saldo.SaldoDisponible < req.Valor)
            throw new InvalidOperationException(
                $"Saldo insuficiente. Disponible: {saldo?.SaldoDisponible ?? 0:0.00}, solicitado: {req.Valor:0.00}.");

        return await CreateRetiroSimulado(idWallet, "COMERCIO", null, idComercio, llave, req.Valor, idUsuario);
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private async Task<MiLlaveResponse> UpsertLlave(
        long idWallet, string tipoSujeto, long? idUsuario, long? idComercio,
        RegistrarLlaveRequest req, long creadoPor)
    {
        var hash = ComputeKeyHash(req.KeyValue);

        await using var tx = await _db.Database.BeginTransactionAsync();
        // Desactivar llave anterior activa si existe
        var anterior = await _db.PassportBrebLlaves.FirstOrDefaultAsync(
            l => l.IdWallet == idWallet && l.EsActiva);
        if (anterior is not null)
        {
            anterior.EsActiva           = false;
            anterior.FechaActualizacion = DateTime.UtcNow;
            anterior.UpdatedByUsuario   = creadoPor;
        }

        var llave = new PassportBrebLlave
        {
            TipoSujeto       = tipoSujeto,
            IdUsuario        = idUsuario,
            IdComercio       = idComercio,
            IdWallet         = idWallet,
            KeyType          = req.KeyType.ToUpperInvariant(),
            KeyValueMasked   = MaskKeyValue(req.KeyType, req.KeyValue),
            KeyValueHash     = hash,
            Estado           = "PENDIENTE_VALIDACION",
            FechaRegistro    = DateTime.UtcNow,
            EsActiva         = true,
            CreatedByUsuario = creadoPor,
        };

        _db.PassportBrebLlaves.Add(llave);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation("Bre-B llave registrada: wallet={Wallet} tipoSujeto={Tipo} keyType={KT}",
            idWallet, tipoSujeto, llave.KeyType);

        return ToLlaveResponse(llave);
    }

    private async Task<BrebRetiroResponse> CreateRetiroSimulado(
        long idWallet, string tipoSujeto, long? idUsuario, long? idComercio,
        PassportBrebLlave llave, decimal valor, long creadoPor)
    {
        var referencia    = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        var idempotency   = $"QA-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..50];

        var retiro = new PassportBrebRetiro
        {
            TipoSujeto        = tipoSujeto,
            IdUsuario         = idUsuario,
            IdComercio        = idComercio,
            IdWallet          = idWallet,
            IdBrebLlave       = llave.IdBrebLlave,
            Valor             = valor,
            Moneda            = "COP",
            Estado            = "CREADO",
            ReferenciaInterna = referencia,
            IdempotencyKey    = idempotency,
            FechaSolicitud    = DateTime.UtcNow,
            CreatedByUsuario  = creadoPor,
        };

        _db.PassportBrebRetiros.Add(retiro);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Bre-B retiro simulado creado: id={Id} wallet={Wallet} tipoSujeto={Tipo} estado=CREADO",
            retiro.IdBrebRetiro, idWallet, tipoSujeto);

        return ToRetiroResponse(retiro, llave.KeyValueMasked);
    }

    private async Task<List<BrebRetiroResponse>> BuildRetirosResponse(long idWallet)
    {
        var retiros = await _db.PassportBrebRetiros
            .Where(r => r.IdWallet == idWallet)
            .OrderByDescending(r => r.FechaSolicitud)
            .Take(50)
            .ToListAsync();

        if (retiros.Count == 0) return [];

        var llaveIds = retiros.Select(r => r.IdBrebLlave).Distinct().ToList();
        var llaves   = await _db.PassportBrebLlaves
            .Where(l => llaveIds.Contains(l.IdBrebLlave))
            .ToDictionaryAsync(l => l.IdBrebLlave, l => l.KeyValueMasked);

        return retiros.Select(r => ToRetiroResponse(r,
            llaves.TryGetValue(r.IdBrebLlave, out var m) ? m : "***")).ToList();
    }

    private static MiLlaveResponse ToLlaveResponse(PassportBrebLlave l) => new()
    {
        IdBrebLlave    = l.IdBrebLlave,
        TipoSujeto     = l.TipoSujeto,
        KeyType        = l.KeyType,
        KeyValueMasked = l.KeyValueMasked,
        Estado         = l.Estado,
        FechaRegistro  = l.FechaRegistro,
        FechaValidacion = l.FechaValidacion,
    };

    private static BrebRetiroResponse ToRetiroResponse(PassportBrebRetiro r, string keyMasked) => new()
    {
        IdBrebRetiro      = r.IdBrebRetiro,
        TipoSujeto        = r.TipoSujeto,
        Valor             = r.Valor,
        Moneda            = r.Moneda,
        Estado            = r.Estado,
        ReferenciaInterna = r.ReferenciaInterna,
        KeyValueMasked    = keyMasked,
        FechaSolicitud    = r.FechaSolicitud,
        MotivoRechazo     = r.MotivoRechazo,
    };

    private static string ComputeKeyHash(string keyValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyValue.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string MaskKeyValue(string keyType, string keyValue)
    {
        var v = keyValue.Trim();
        return keyType.ToUpperInvariant() switch
        {
            "EMAIL" => MaskEmail(v),
            "PHONE" => MaskPhone(v),
            _       => MaskGeneric(v),
        };
    }

    private static string MaskEmail(string email)
    {
        var atIdx = email.IndexOf('@');
        if (atIdx <= 1) return "***@***";
        var local  = email[..atIdx];
        var domain = email[atIdx..];
        var visible = local.Length > 2 ? local[..2] : local[..1];
        return $"{visible}***{domain}";
    }

    private static string MaskPhone(string phone)
    {
        var digits = phone.Where(char.IsDigit).ToArray();
        if (digits.Length < 4) return "***";
        var last4 = new string(digits[^4..]);
        return $"***{last4}";
    }

    private static string MaskGeneric(string value)
    {
        if (value.Length <= 4) return "***";
        return $"***{value[^Math.Min(4, value.Length)..]}";
    }

    private static void ValidateKeyType(string keyType)
    {
        if (!ValidKeyTypes.Contains(keyType))
            throw new InvalidOperationException(
                $"Tipo de llave inválido: '{keyType}'. Permitidos: ID, PHONE, EMAIL, ALPHA, BCODE.");
    }

    private static void ValidateKeyValue(string keyType, string keyValue)
    {
        if (string.IsNullOrWhiteSpace(keyValue))
            throw new InvalidOperationException("El valor de la llave no puede estar vacío.");

        var v = keyValue.Trim();
        var valid = keyType.ToUpperInvariant() switch
        {
            "ID"    => v.Length >= 5 && v.Length <= 20 && v.All(char.IsDigit),
            "PHONE" => v.Length >= 7 && v.Length <= 15,
            "EMAIL" => v.Contains('@') && v.Contains('.') && v.Length <= 100,
            "ALPHA" => v.Length >= 3 && v.Length <= 30 && v.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'),
            "BCODE" => v.Length >= 8 && v.Length <= 50 && v.All(c => char.IsLetterOrDigit(c) || c == '-'),
            _       => false,
        };

        if (!valid)
            throw new InvalidOperationException(
                $"Formato de llave inválido para tipo {keyType}. Revisa la longitud y caracteres permitidos.");
    }
}
