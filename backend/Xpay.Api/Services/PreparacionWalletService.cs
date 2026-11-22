using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public sealed class PreparacionWalletService(
    XpayDbContext db, IConfiguration config, CondicionesWalletPiloto condiciones,
    ConsentimientoBiometricoService consentimiento, PerfilService perfil, AuthService auth,
    TimeProvider reloj)
{
    public const string CriteriosVersion = "WALLET_PUBLICA_V1";
    public const string AccionActivar = "ACTIVAR_WALLET_PUBLICA";
    internal const string MensajeRevision = "Tu identidad requiere una revisión de XPAY antes de activar la Wallet.";

    private CondicionesWalletResponse ExigirDisponible() => condiciones.Obtener()
        ?? throw new PreparacionWalletNoDisponibleException();

    public async Task<PreparacionWalletResponse> ObtenerAsync(long idUsuario, CancellationToken ct = default)
    {
        var documento = ExigirDisponible();
        var publica = await RegistroPublicoService.EsAutorregistroAsync(db, idUsuario, ct);
        // La preparación nueva no cambia la navegación ni los requisitos de
        // las cuentas históricas del piloto, cualquiera sea su combinación de roles.
        if (!publica) throw new PreparacionWalletNoDisponibleException();
        var (usuario, persona) = await TitularAsync(idUsuario, ct);
        var identidad = await IdentidadAprobadaAsync(db, usuario, persona, ct)
            && await consentimiento.TieneConsentimientoVigenteAsync(idUsuario, ct);
        var datos = await perfil.DomicilioDaneCompletoAsync(persona.IdPersona, ct);
        var preparacion = await db.WalletPreparaciones.AsNoTracking().SingleOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
        var pin = await db.PinesTransaccionales.AsNoTracking().SingleOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
        var pinListo = pin is { Estado: EstadosPin.Activo, Bloqueado: false, LongitudPin: 6 } && pin.PinHash.Length > 0;
        var activada = publica && !await RegistroPublicoService.EsCuentaLimitadaAsync(db, idUsuario);
        var idDepartamento = persona.IdCiudadDane is { } ciudad
            ? await db.CatalogoCiudades.Where(c => c.IdCiudad == ciudad).Select(c => (long?)c.IdDepartamento).SingleOrDefaultAsync(ct)
            : null;
        var revision = !publica || (usuario.EstadoKycActual == "APROBADO" && !identidad)
            || (identidad && await MotivoIdentidadAsync(persona, ct) is not null)
            || (preparacion is not null && preparacion.IdPersona != persona.IdPersona)
            || (!activada && await db.Wallets.AnyAsync(w => w.IdPersona == persona.IdPersona, ct));
        var paso = activada ? "WALLET" : !usuario.EmailVerificado ? "CORREO"
            : revision ? "REVISION" : !identidad ? "IDENTIDAD" : !datos ? "DATOS"
            : preparacion is null || preparacion.OpcionLlave == "PENDIENTE" ? "LLAVE"
            : !pinListo ? (pin?.Bloqueado == true ? "REVISION" : "PIN") : "ACTIVAR";
        var puedeActualizar = paso == "REVISION" && identidad && usuario.EmailVerificado
            && string.IsNullOrWhiteSpace(persona.TipoDocumento) && string.IsNullOrWhiteSpace(persona.NumeroDocumento)
            && persona.TipoDocumentoVeriffRaw is "ID_CARD" or "PASSPORT";
        return new(true, publica, usuario.EmailVerificado, usuario.EstadoKycActual, identidad, datos,
            persona.Direccion, idDepartamento, persona.IdCiudadDane, persona.Departamento, persona.Ciudad,
            new(preparacion?.OpcionLlave ?? "PENDIENTE", preparacion?.LlaveTipo, preparacion?.LlaveMasked),
            new(pin?.Estado == EstadosPin.Activo, pin?.Bloqueado == true, pin?.LongitudPin),
            activada, paso, paso == "ACTIVAR", documento,
            paso == "REVISION" ? (pin?.Bloqueado == true ? "Tu PIN está bloqueado. Solicita a XPAY su restablecimiento." : MensajeRevision) : null,
            puedeActualizar);
    }

    public async Task<PreparacionWalletResponse> GuardarDatosAsync(long idUsuario, PreparacionDatosRequest request, CancellationToken ct = default)
    {
        await ExigirEtapaAsync(idUsuario, ["DATOS", "LLAVE", "PIN", "ACTIVAR"], ct);
        var (u, _) = await TitularAsync(idUsuario, ct);
        await perfil.ActualizarDomicilioDaneAsync(idUsuario, u.IdPersona,
            new GuardarDomicilioDaneRequest(request.Direccion ?? "", request.IdDepartamento, request.IdCiudad), ct);
        return await ObtenerAsync(idUsuario, ct);
    }

    public async Task<PreparacionWalletResponse> GuardarLlaveAsync(long idUsuario, PreparacionLlaveRequest request, CancellationToken ct = default)
    {
        ExigirDisponible();
        string? tipo = null, hash = null, mascara = null;
        if (request.Omitir)
        {
            if (request.ConfirmaPropiedad || request.TipoLlave is not null || request.ValorLlave is not null)
                throw new InvalidOperationException("Elige registrar la llave o continuar sin ella.");
        }
        else
        {
            if (!request.ConfirmaPropiedad) throw new InvalidOperationException("Confirma que la llave es tuya.");
            tipo = request.TipoLlave?.Trim().ToUpperInvariant() ?? "";
            BrebService.ValidateKeyType(tipo);
            var valor = BrebService.NormalizeKeyValue(tipo, request.ValorLlave ?? "");
            hash = BrebKeyValue.ComputeHash(valor);
            mascara = BrebService.MaskKeyValue(tipo, valor);
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await BloquearTitularAsync(idUsuario, ct);
        await ExigirEtapaAsync(idUsuario, ["LLAVE", "PIN", "ACTIVAR"], ct);
        var (usuario, _) = await TitularAsync(idUsuario, ct);
        var fila = await db.WalletPreparaciones.SingleOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        if (fila is null)
        {
            fila = new() { IdUsuario = idUsuario, IdPersona = usuario.IdPersona, FechaCreacion = ahora };
            db.WalletPreparaciones.Add(fila);
        }
        fila.OpcionLlave = request.Omitir ? "OMITIDA" : "REGISTRADA";
        fila.LlaveTipo = tipo; fila.LlaveHash = hash; fila.LlaveMasked = mascara;
        fila.PropiedadConfirmada = !request.Omitir; fila.FechaActualizacion = ahora;
        db.Auditorias.Add(new Auditoria { IdUsuario = idUsuario, IdPersona = usuario.IdPersona,
            Modulo = "USUARIOS", Accion = "PREPARAR_LLAVE_WALLET", Entidad = "usuarios",
            IdEntidad = idUsuario.ToString(CultureInfo.InvariantCulture), Resultado = "EXITOSO", FechaEvento = ahora,
            ValorNuevo = JsonSerializer.Serialize(new { opcion = fila.OpcionLlave, tipo }),
            Observacion = "Elección del titular; no valida titularidad bancaria ni llama al proveedor." });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ObtenerAsync(idUsuario, ct);
    }

    // Permite preparar el PIN de una cuenta limitada únicamente cuando llegó
    // al paso correspondiente. El PIN conserva contraseña actual, límites y hash.
    public async Task ExigirAccesoPinAsync(long idUsuario, CancellationToken ct = default)
    {
        if (!await RegistroPublicoService.EsCuentaLimitadaAsync(db, idUsuario)) return;
        await ExigirEtapaAsync(idUsuario, ["PIN", "ACTIVAR"], ct);
    }

    public async Task<LoginResponse> ActivarAsync(long idUsuario, ActivarWalletRequest request, string? ipSolicitud, CancellationToken ct = default)
    {
        var documento = ExigirDisponible();
        if (!request.AceptaCondiciones || request.VersionCondiciones != documento.Version)
            throw new InvalidOperationException("Revisa y acepta las condiciones de habilitación de Wallet vigentes.");
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("La activación requiere su propia transacción.");
        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct))
        {
            try
            {
                // Ambas vías del piloto comparten capacidad y orden de locks.
                if (await AppLockHelper.AdquirirAsync(db, AltaUsuarioPilotoService.ClaveLock, ct) < 0)
                    throw new InvalidOperationException("Hay otra activación en curso. Actualiza el estado.");
                await BloquearTitularAsync(idUsuario, ct);
                var (usuario, persona) = await TitularAsync(idUsuario, ct);
                if (!await RegistroPublicoService.EsAutorregistroAsync(db, idUsuario, ct))
                    throw new InvalidOperationException("Esta cuenta no pertenece al registro público.");
                var fila = await db.WalletPreparaciones.SingleOrDefaultAsync(p => p.IdUsuario == idUsuario, ct);
                if (fila?.IdWallet is not null)
                {
                    if (await RegistroPublicoService.EsCuentaLimitadaAsync(db, idUsuario))
                        throw new InvalidOperationException(MensajeRevision);
                    await tx.CommitAsync(ct);
                }
                else
                {
                    // Antes de consultar duplicados se comparte el lock global
                    // con KYC. Evita invertirlo con lecturas serializables.
                    var numero = TipoDocumentoVeriffMapper.NumeroComparable(persona.NumeroDocumentoVerificado);
                    if (numero is null) throw new InvalidOperationException(MensajeRevision);
                    if (await AppLockHelper.AdquirirAsync(db, $"XPAY:IDENTIDAD_DOCUMENTO:{persona.IdUnidadNegocio}:{numero}", ct) < 0
                        || await AppLockHelper.AdquirirAsync(db, $"XPAY:IDENTIDAD_DOCUMENTO_GLOBAL:{numero}", ct) < 0)
                        throw new InvalidOperationException("La identidad está siendo revisada. Actualiza el estado.");
                    await ExigirEtapaAsync(idUsuario, ["ACTIVAR"], ct);
                    // Lectura mantenida hasta commit: un reset o bloqueo del PIN
                    // concurrente no puede completar una activación obsoleta.
                    var pinListo = await db.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) AS Value FROM dbo.pin_transaccional WITH (UPDLOCK, HOLDLOCK) WHERE id_usuario={0} AND estado='ACTIVO' AND bloqueado=0 AND longitud_pin=6 AND LEN(pin_hash)>0", idUsuario).SingleAsync(ct);
                    if (pinListo != 1) throw new InvalidOperationException("Configura un PIN de 6 dígitos antes de activar la Wallet.");
                    if (await MotivoIdentidadAsync(persona, ct) is not null) throw new InvalidOperationException(MensajeRevision);
                    var maximo = AltaUsuarioPilotoService.LeerMaximoHabilitado(
                        config[AltaUsuarioPilotoService.ClaveHabilitada], config[AltaUsuarioPilotoService.ClaveMaxUsuarios]);
                    if (maximo is null) throw new InvalidOperationException("La incorporación al piloto no está disponible.");
                    if (await ContarPlazasAsync(db, ct) >= Math.Min(maximo.Value, 50))
                        throw new InvalidOperationException(AltaUsuarioPilotoService.MensajeMaximoAlcanzado);
                    var ahora = reloj.GetUtcNow().UtcDateTime;
                    var wallet = new Wallet { IdPersona = persona.IdPersona, IdUnidadNegocio = persona.IdUnidadNegocio,
                        TipoWallet = "PERSONA", NombreWallet = $"Wallet {usuario.NombreUsuario}", Estado = "ACTIVA", FechaCreacion = ahora };
                    db.Wallets.Add(wallet); await db.SaveChangesAsync(ct);
                    db.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = ahora });
                    fila!.IdWallet = wallet.IdWallet; fila.FechaActivacion = ahora; fila.FechaActualizacion = ahora;
                    fila.CriteriosVersion = CriteriosVersion; fila.CondicionesVersion = documento.Version; fila.CondicionesSha256 = documento.Sha256;
                    if (fila.OpcionLlave == "REGISTRADA") db.PassportBrebLlaves.Add(new PassportBrebLlave {
                        TipoSujeto = "USUARIO", IdUsuario = idUsuario, IdWallet = wallet.IdWallet, KeyType = fila.LlaveTipo!,
                        KeyValueHash = fila.LlaveHash!, KeyValueMasked = fila.LlaveMasked!, Estado = "PENDIENTE_VALIDACION",
                        EsActiva = true, FechaRegistro = ahora, CreatedByUsuario = idUsuario });
                    db.Auditorias.Add(new Auditoria { IdUsuario = idUsuario, IdPersona = persona.IdPersona,
                        Modulo = "USUARIOS", Accion = AccionActivar, Entidad = "usuarios", IdEntidad = idUsuario.ToString(CultureInfo.InvariantCulture),
                        Resultado = "EXITOSO", FechaEvento = ahora, Ip = IPAddress.TryParse(ipSolicitud, out var ip) ? ip.ToString() : null,
                        ValorNuevo = JsonSerializer.Serialize(new { idWallet = wallet.IdWallet, criteriosVersion = CriteriosVersion,
                            condicionesVersion = documento.Version, condicionesSha256 = documento.Sha256,
                            aceptaCondiciones = true, canal = "PERFIL_WEB", emailVerificado = true, identidadVerificada = true,
                            domicilioDane = true, longitudPin = 6, opcionLlave = fila.OpcionLlave, whatsAppPendiente = true }),
                        Observacion = "Activación solicitada por el titular tras los controles del piloto; saldo inicial cero, sin cupos ni crédito." });
                    await db.SaveChangesAsync(ct);
                    await VersionSeguridadUsuario.IncrementarAsync(db, idUsuario, ct);
                    await tx.CommitAsync(ct);
                }
            }
            catch { db.ChangeTracker.Clear(); throw; }
        }
        return await auth.ReemitirSesionWalletAsync(idUsuario, ct);
    }

    private async Task ExigirEtapaAsync(long idUsuario, string[] pasos, CancellationToken ct)
    {
        var estado = await ObtenerAsync(idUsuario, ct);
        if (!estado.EsRegistroPublico || !pasos.Contains(estado.SiguientePaso, StringComparer.Ordinal))
            throw new InvalidOperationException(estado.Mensaje ?? "Completa primero el paso pendiente de tu registro.");
    }

    private async Task<(Usuario, Persona)> TitularAsync(long idUsuario, CancellationToken ct)
    {
        var u = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.IdUsuario == idUsuario, ct)
            ?? throw new UnauthorizedAccessException("Usuario no disponible.");
        var p = await db.Personas.AsNoTracking().SingleAsync(p => p.IdPersona == u.IdPersona, ct);
        if (u.Estado != "ACTIVO" || p.Estado != "ACTIVA" || u.RequiereCambioClave)
            throw new UnauthorizedAccessException("La cuenta debe estar activa y con su contraseña vigente.");
        if (!await db.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM dbo.unidades_negocio WHERE id_unidad_negocio={0} AND codigo='XPAY_COL' AND estado='ACTIVA'",
            p.IdUnidadNegocio).AnyAsync(ct))
            throw new UnauthorizedAccessException("La unidad de la cuenta no está disponible.");
        var roles = await (from ur in db.UsuarioRoles join r in db.Roles on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO" select r.Codigo).ToListAsync(ct);
        if (roles.Count != 1 || roles[0] != "USUARIO_FINAL") throw new UnauthorizedAccessException("Se requiere una cuenta personal de usuario final.");
        if (await db.LibranzaUsuariosEmpresa.AnyAsync(a => a.IdUsuario == idUsuario && a.Estado == "ACTIVO", ct))
            throw new UnauthorizedAccessException("Se requiere una cuenta personal de usuario final.");
        return (u, p);
    }

    private async Task BloquearTitularAsync(long idUsuario, CancellationToken ct)
    {
        if (await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{idUsuario}", ct) < 0)
            throw new InvalidOperationException("Hay una actualización de identidad en curso.");
        var idPersona = await db.Usuarios.Where(u => u.IdUsuario == idUsuario).Select(u => u.IdPersona).SingleAsync(ct);
        await ContactoVerificacionService.BloquearPersonaAsync(db, idPersona, ct);
    }

    internal static Task<int> ContarPlazasAsync(XpayDbContext db, CancellationToken ct = default) =>
        db.Auditorias.CountAsync(a => a.Resultado == "EXITOSO" &&
            (a.Accion == AltaUsuarioPilotoService.AccionAuditoria || a.Accion == AccionActivar), ct);

    internal static async Task<bool> IdentidadAprobadaAsync(XpayDbContext db, Usuario u, Persona p, CancellationToken ct = default)
    {
        if (u.EstadoKycActual != "APROBADO" || !p.IdentidadVerificada || p.IdentidadVerificadaProveedor != "VERIFF"
            || p.IdentidadVerificadaFecha is null || string.IsNullOrWhiteSpace(p.NumeroDocumentoVerificado)) return false;
        var actuales = await db.KycVerificaciones.AsNoTracking().Where(k => k.IdUsuario == u.IdUsuario && k.EsActual)
            .Select(k => new { k.IdPersona, k.Proveedor, k.EstadoKyc, k.Decision }).Take(2).ToListAsync(ct);
        return actuales.Count == 1 && actuales[0].IdPersona == p.IdPersona && actuales[0].Proveedor == "VERIFF"
            && actuales[0].EstadoKyc == "APROBADO" && actuales[0].Decision == "approved";
    }

    private async Task<string?> MotivoIdentidadAsync(Persona p, CancellationToken ct)
    {
        var numero = TipoDocumentoVeriffMapper.NumeroComparable(p.NumeroDocumentoVerificado) ?? "";
        var tipo = p.TipoDocumento?.Trim().ToUpperInvariant();
        if (numero.Length is 0 or > 30 || !numero.All(char.IsAsciiLetterOrDigit) || tipo is not ("CC" or "PASAPORTE")
            || (tipo == "CC" && !numero.All(char.IsAsciiDigit))
            || p.NumeroDocumento?.Trim().ToUpperInvariant() != numero) return "NO_CONSOLIDADA";
        if (await db.Usuarios.CountAsync(u => u.IdPersona == p.IdPersona, ct) != 1) return "VARIAS_CUENTAS";
        return await db.Personas.AnyAsync(otra => otra.IdPersona != p.IdPersona &&
            ((otra.TipoDocumento == tipo && otra.NumeroDocumento != null &&
                otra.NumeroDocumento.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper() == numero)
            || (otra.NumeroDocumentoVerificado != null &&
                otra.NumeroDocumentoVerificado.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper() == numero &&
                (otra.TipoDocumentoVeriffRaw == p.TipoDocumentoVeriffRaw || otra.TipoDocumento == tipo))), ct)
            ? "DOCUMENTO_DUPLICADO" : null;
    }
}

public sealed class PreparacionWalletNoDisponibleException() : Exception("La preparación de Wallet no está disponible.");
