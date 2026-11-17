using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public sealed class WalletRecargaBrebQrService(
    XpayDbContext db, IConfiguration config, IPassportQrClient qrClient,
    IPassportHttpClient http, IPassportCustomerAccountClient accounts,
    ControlOperacionesFinancieras control)
{
    public static readonly TimeSpan VentanaReintento = TimeSpan.FromSeconds(60);

    public async Task<WalletRecargaBrebQrResponse?> ObtenerAsync(long idPersona, long idUsuario, CancellationToken ct = default)
    {
        var wallet = await ObtenerWalletAsync(idPersona, idUsuario, ct);
        var qr = await db.WalletRecargaBrebQrs.AsNoTracking().SingleOrDefaultAsync(q => q.IdWallet == wallet.IdWallet, ct);
        if (qr is null) return null;
        ValidarDestino(qr);
        return await RespuestaAsync(qr, ct);
    }

    public async Task<WalletRecargaBrebQrResponse> GenerarAsync(long idPersona, long idUsuario, CancellationToken ct = default)
    {
        control.ExigirInterruptor();
        var wallet = await ObtenerWalletAsync(idPersona, idUsuario, ct);
        var cuentas = await db.LedgerCuentas.CountAsync(c => c.IdUnidadNegocio == wallet.IdUnidadNegocio
            && c.Estado == "ACTIVA" && (c.Codigo == "110103" || c.Codigo == "210101"), ct);
        if (cuentas != 2)
            throw new InvalidOperationException("La Wallet requiere configuración contable antes de habilitar la recarga.");
        var accountId = ConfigurarId(PassportOptions.EnvOperationalAccountId);
        var customerId = ConfigurarId(PassportOptions.EnvOperationalCustomerId);
        var keyId = ConfigurarId(PassportOptions.EnvCollectionKeyId);
        WalletRecargaBrebQr qr;
        DateTime intento;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await BloquearAsync(wallet.IdWallet, ct);
            qr = await db.WalletRecargaBrebQrs.SingleOrDefaultAsync(q => q.IdWallet == wallet.IdWallet, ct)
                ?? new WalletRecargaBrebQr
                {
                    IdWallet = wallet.IdWallet, IdUsuario = idUsuario,
                    Referencia = "W" + LibranzaRecaudoReferencia.Generar(), FechaCreacion = DateTime.UtcNow,
                    PassportAccountId = accountId, PassportCustomerId = customerId, PassportKeyId = keyId,
                };
            if (qr.PassportAccountId != accountId || qr.PassportCustomerId != customerId || qr.PassportKeyId != keyId)
                throw new InvalidOperationException("El destino de este QR requiere revisión antes de volver a usarlo.");
            if (qr.Estado == "ACTIVO")
            {
                await tx.CommitAsync(ct);
                return await RespuestaAsync(qr, ct);
            }
            if (qr.Estado == "GENERANDO" && qr.FechaIntento > DateTime.UtcNow - VentanaReintento)
                throw new InvalidOperationException("La generación del QR está en curso. Intenta de nuevo en un momento.");
            if (qr.IdRecargaQr == 0) db.WalletRecargaBrebQrs.Add(qr);
            intento = DateTime.UtcNow;
            qr.Estado = "GENERANDO";
            qr.FechaIntento = intento;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var creacionEnviada = false;
        try
        {
            // La referencia queda persistida antes de cualquier creación remota.
            var key = await http.GetAsync<PassportKeyResponse>($"/v1/keys/{keyId}", ct);
            var account = await accounts.RetrieveAccountAsync(accountId, ct);
            if (key is null || !MismoId(key.Id, keyId) || key.Status != "ACTIVE" || !MismoId(key.AccountId, accountId)
                || !MismoId(account.Id, accountId) || account.Status != "ACTIVE" || !MismoId(account.CustomerId, customerId))
                throw new PassportProtocolException("No se pudo verificar el destino de recaudo de XPAY.");

            creacionEnviada = true;
            var response = await qrClient.CreateQrCodeAsync(new PassportCreateQrCodeRequest(
                keyId, customerId, PassportQrType.STATIC, PassportQrChannel.MPOS)
            {
                QrCodeReference = qr.Referencia,
                Vat = new PassportQrVatRequest(PassportQrVatType.FIXED, "0.00", "0.00"),
                Inc = new PassportQrIncRequest(PassportQrVatType.FIXED, "0.00"),
            }, ct);
            if (string.IsNullOrWhiteSpace(response.Id) || string.IsNullOrWhiteSpace(response.QrCodeData)
                || response.QrCodeData.Length > 4000 || response.Id.Length > 100
                || LibranzaRecaudoReferencia.ExtraerDePassport(response.QrCodeReference) != qr.Referencia
                || response.Type != "STATIC" || response.Status != "ACTIVE" || response.Amount is not null
                || response.KeyId is not null && !MismoId(response.KeyId, keyId)
                || response.CustomerId is not null && !MismoId(response.CustomerId, customerId))
                throw new PassportProtocolException("Passport devolvió un QR distinto al solicitado.");

            db.ChangeTracker.Clear();
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await BloquearAsync(wallet.IdWallet, ct);
                qr = await db.WalletRecargaBrebQrs.SingleAsync(q => q.IdWallet == wallet.IdWallet, ct);
                if (qr.Estado == "GENERANDO" && qr.FechaIntento == intento)
                {
                    qr.PassportQrId = response.Id;
                    qr.QrCodeData = response.QrCodeData;
                    qr.Estado = "ACTIVO";
                    qr.FechaGeneracion = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);
            }
            return await RespuestaAsync(qr, ct);
        }
        catch (Exception ex) when (FalloDefinitivo(ex, creacionEnviada))
        {
            await LiberarIntentoAsync(wallet.IdWallet, intento);
            throw;
        }
    }

    private static bool FalloDefinitivo(Exception error, bool creacionEnviada) =>
        !creacionEnviada || error is ArgumentException or PassportConfigurationException
            or PassportAuthenticationException
            or PassportTransportException { StatusCode: >= 400 and < 500 and not 408 };

    private async Task LiberarIntentoAsync(long idWallet, DateTime intento)
    {
        // El cliente puede haberse desconectado. La limpieza conserva un
        // plazo propio y solo libera la generación que hizo esta solicitud.
        using var limpieza = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = limpieza.Token;
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BloquearAsync(idWallet, ct);
        var qr = await db.WalletRecargaBrebQrs.SingleAsync(q => q.IdWallet == idWallet, ct);
        if (qr.Estado == "GENERANDO" && qr.FechaIntento == intento)
        {
            qr.Estado = "PENDIENTE";
            qr.FechaIntento = null;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private async Task<Wallet> ObtenerWalletAsync(long idPersona, long idUsuario, CancellationToken ct) =>
        await db.Wallets.AsNoTracking().SingleOrDefaultAsync(w => w.IdPersona == idPersona
            && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA"
            && db.Usuarios.Any(u => u.IdUsuario == idUsuario && u.IdPersona == idPersona && u.Estado == "ACTIVO"), ct)
        ?? throw new KeyNotFoundException("No se encontró una Wallet activa.");

    private async Task<WalletRecargaBrebQrResponse> RespuestaAsync(WalletRecargaBrebQr qr, CancellationToken ct)
    {
        var recargas = await db.WalletRecargasBreb.AsNoTracking().Where(r => r.IdRecargaQr == qr.IdRecargaQr)
            .OrderByDescending(r => r.Fecha).ThenByDescending(r => r.IdRecarga).Take(20)
            .Select(r => new WalletRecargaBrebItemResponse(r.IdRecarga, r.Valor, r.Fecha, r.Estado)).ToListAsync(ct);
        return new WalletRecargaBrebQrResponse(qr.IdRecargaQr, qr.Referencia, qr.QrCodeData, qr.Estado, recargas);
    }

    private string ConfigurarId(string key) => Guid.TryParse(config[key], out var id) && id != Guid.Empty
        ? id.ToString("D") : throw new PassportConfigurationException($"{key} no está configurado.");

    private void ValidarDestino(WalletRecargaBrebQr qr)
    {
        if (qr.PassportAccountId != ConfigurarId(PassportOptions.EnvOperationalAccountId)
            || qr.PassportCustomerId != ConfigurarId(PassportOptions.EnvOperationalCustomerId)
            || qr.PassportKeyId != ConfigurarId(PassportOptions.EnvCollectionKeyId))
            throw new InvalidOperationException("El destino de este QR requiere revisión antes de volver a usarlo.");
    }

    private static bool MismoId(string? value, string expected) =>
        Guid.TryParse(value, out var id) && id.ToString("D") == expected;

    private async Task BloquearAsync(long idWallet, CancellationToken ct)
    {
        if (await AppLockHelper.AdquirirAsync(db, $"XPAY:WALLET_RECARGA_QR:{idWallet}", ct) < 0)
            throw new InvalidOperationException("Otra solicitud está preparando el QR. Intenta de nuevo.");
    }
}
