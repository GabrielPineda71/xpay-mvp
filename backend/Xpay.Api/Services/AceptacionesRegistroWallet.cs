using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

// Solo las dos categorías del registro: A=Términos, B=tratamiento de datos.
// C permanece en ConsentimientoBiometricoService. Nunca se aceptan por omisión.
internal static class AceptacionesRegistroWallet
{
    internal static async Task<bool> VigenteAsync(XpayDbContext db, long idUsuario, long idPersona,
        bool terminos, CancellationToken ct = default)
    {
        var accion = terminos ? RegistroPublicoService.AccionAceptarTerminos : RegistroPublicoService.AccionAutorizarDatos;
        var entidad = idUsuario.ToString(CultureInfo.InvariantCulture);
        var ultima = await db.Auditorias.AsNoTracking().Where(a => a.IdUsuario == idUsuario && a.IdPersona == idPersona
            && a.Modulo == "USUARIOS" && a.Accion == accion && a.Entidad == "usuarios" && a.IdEntidad == entidad)
            .OrderByDescending(a => a.IdAuditoria).Select(a => new { a.Resultado, a.ValorNuevo }).FirstOrDefaultAsync(ct);
        if (ultima is null || ultima.Resultado != "EXITOSO" || string.IsNullOrWhiteSpace(ultima.ValorNuevo)) return false;
        try
        {
            using var json = JsonDocument.Parse(ultima.ValorNuevo);
            var e = json.RootElement;
            return Texto(e, "tipoDocumento", terminos ? "TERMINOS_Y_CONDICIONES" : "POLITICA_TRATAMIENTO_DATOS")
                && Texto(e, "version", PreparacionWalletConfiguracion.Version)
                && Texto(e, "sha256", terminos ? PreparacionWalletConfiguracion.TerminosSha256 : PreparacionWalletConfiguracion.PoliticaDatosSha256)
                && (Texto(e, "canal", "WEB_AUTORREGISTRO") || Texto(e, "canal", "PERFIL_WEB"));
        }
        catch (JsonException) { return false; }
    }

    internal static async Task GuardarPendientesAsync(XpayDbContext db, long idUsuario, long idPersona,
        ActivarWalletRequest request, DocumentosRegistroPublico documentos, DateTime ahora, string? ip, CancellationToken ct)
    {
        var a = await VigenteAsync(db, idUsuario, idPersona, true, ct);
        var b = await VigenteAsync(db, idUsuario, idPersona, false, ct);
        if ((!a && !request.AceptaTerminos) || (request.AceptaTerminos && request.VersionTerminos != documentos.Terminos.DatosPublicos.Version))
            throw new InvalidOperationException("Lee y acepta los Términos y Condiciones vigentes para activar tu Wallet.");
        if ((!b && !request.AutorizaDatos) || (request.AutorizaDatos && request.VersionPoliticaDatos != documentos.PoliticaDatos.DatosPublicos.Version))
            throw new InvalidOperationException("Lee la Política de Tratamiento de Datos Personales y autoriza el tratamiento descrito para activar tu Wallet.");
        if (!a) db.Auditorias.Add(RegistroPublicoService.EvidenciaConsentimiento(
            new() { IdUsuario = idUsuario, IdPersona = idPersona }, RegistroPublicoService.AccionAceptarTerminos,
            "TERMINOS_Y_CONDICIONES", documentos.Terminos, ahora, ip, "PERFIL_WEB"));
        if (!b) db.Auditorias.Add(RegistroPublicoService.EvidenciaConsentimiento(
            new() { IdUsuario = idUsuario, IdPersona = idPersona }, RegistroPublicoService.AccionAutorizarDatos,
            "POLITICA_TRATAMIENTO_DATOS", documentos.PoliticaDatos, ahora, ip, "PERFIL_WEB"));
    }

    private static bool Texto(JsonElement e, string nombre, string valor) => e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(nombre, out var p) && p.ValueKind == JsonValueKind.String && p.GetString() == valor;
}
