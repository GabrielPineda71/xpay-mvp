using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public sealed class AutorizacionAnticipoConflictoException : InvalidOperationException
{
    public AutorizacionAnticipoConflictoException() : base("Los datos cambiaron. Actualiza las autorizaciones antes de continuar.") { }
}

// La decisión de habilitar/revocar la toma exclusivamente un administrador.
// Este servicio nunca crea un vínculo, una Wallet, un cupo ni dinero.
public sealed class LibranzaAutorizacionAnticipoService(XpayDbContext db)
{
    public const string MensajeBloqueo = "Los cupos y anticipos requieren una habilitación separada de XPAY. Puedes usar tu Wallet.";
    public const string AccionAutorizar = "ANTICIPO_NOMINA_AUTORIZADO";
    public const string AccionRevocar = "ANTICIPO_NOMINA_REVOCADO";

    public async Task<LibranzaAutorizacionesAnticipoResponse> ListarAsync(long idAdmin, long? idConvenio = null,
        int pagina = 1, int tamanio = 50, CancellationToken ct = default)
    {
        await ExigirAdministradorAsync(idAdmin, ct);
        if (pagina < 1 || tamanio is < 1 or > 200 || idConvenio is <= 0)
            throw new InvalidOperationException("Página, tamaño o convenio inválidos (máximo 200 por página).");
        var consulta = from v in db.LibranzaEmpleadoUsuarios.AsNoTracking()
            join e in db.LibranzaEmpleados.AsNoTracking() on v.IdEmpleado equals e.IdEmpleado
            where v.Estado == "ACTIVO" && (!idConvenio.HasValue || e.IdConvenio == idConvenio)
            select v;
        var total = await consulta.CountAsync(ct);
        var vinculos = await consulta.OrderBy(x => x.IdEmpleadoUsuario).Skip(checked((pagina - 1) * tamanio)).Take(tamanio).ToListAsync(ct);
        var datos = await CargarAsync(vinculos, ct);
        return new() { Pagina = pagina, Total = total, TotalPaginas = (int)Math.Ceiling(total / (decimal)tamanio),
            Items = datos.Select(d => Presentar(d, DateTime.UtcNow)).ToList() };
    }

    public async Task<LibranzaAutorizacionAnticipoResponse> CambiarAsync(long idAdmin,
        CambiarAutorizacionAnticipoRequest solicitud, bool autorizar, CancellationToken ct = default)
    {
        await ExigirAdministradorAsync(idAdmin, ct);
        ValidarSolicitud(solicitud);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La autorización requiere su propia transacción.");
        var previo = await (from v in db.LibranzaEmpleadoUsuarios.AsNoTracking()
            join u in db.Usuarios.AsNoTracking() on v.IdUsuario equals u.IdUsuario
            where v.IdEmpleadoUsuario == solicitud.IdEmpleadoUsuario
            select new { v.IdUsuario, v.IdEmpleado, u.IdPersona }).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("El vínculo ya no está disponible.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // El mismo orden que la aprobación laboral. La fila de empleado también
        // serializa con SolicitarAnticipoAsync, que verifica el permiso bajo ese lock.
        if (await AppLockHelper.AdquirirAsync(db, LibranzaVinculacionService.ClaveLock, ct) < 0
            || await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{previo.IdUsuario}", ct) < 0)
            throw new AutorizacionAnticipoConflictoException();
        await ContactoVerificacionService.BloquearPersonaAsync(db, previo.IdPersona, ct);
        _ = await db.LibranzaEmpleados.FromSqlInterpolated(
            $"SELECT * FROM dbo.libranza_empleados WITH (UPDLOCK, ROWLOCK) WHERE id_empleado={previo.IdEmpleado}")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        await ExigirAdministradorAsync(idAdmin, ct);
        var vinculo = await db.LibranzaEmpleadoUsuarios.AsNoTracking()
            .SingleOrDefaultAsync(v => v.IdEmpleadoUsuario == solicitud.IdEmpleadoUsuario, ct)
            ?? throw new AutorizacionAnticipoConflictoException();
        if (vinculo.IdUsuario != previo.IdUsuario || vinculo.IdEmpleado != previo.IdEmpleado)
            throw new AutorizacionAnticipoConflictoException();
        var dato = (await CargarAsync([vinculo], ct)).Single();
        if (dato.Revision.Usuario?.IdPersona != previo.IdPersona || !HuellaIgual(solicitud.Huella, Huella(dato)))
            throw new AutorizacionAnticipoConflictoException();
        var actual = Presentar(dato, DateTime.UtcNow);
        // Un POST repetido con una foto nueva no duplica decisiones ni auditoría.
        if ((autorizar && actual.Estado == "AUTORIZADO") || (!autorizar && actual.Estado == "REVOCADO"))
        {
            await tx.CommitAsync(ct);
            return actual;
        }
        if (autorizar && !actual.PuedeAutorizar)
            throw new InvalidOperationException(string.Join(" ", actual.Razones));
        if (!autorizar && !actual.PuedeRevocar)
            throw new InvalidOperationException("No hay una habilitación vigente para revocar en este vínculo.");
        var ahora = DateTime.UtcNow;
        var registro = await db.LibranzaAutorizacionesAnticipo.SingleOrDefaultAsync(
            a => a.IdEmpleadoUsuario == vinculo.IdEmpleadoUsuario && a.IdUsuario == vinculo.IdUsuario, ct);
        if (registro is null)
        {
            registro = new() { IdEmpleadoUsuario = vinculo.IdEmpleadoUsuario, IdUsuario = vinculo.IdUsuario, CreadoPorUsuario = idAdmin,
                FechaCreacion = ahora };
            db.LibranzaAutorizacionesAnticipo.Add(registro);
        }
        registro.IdEmpleado = vinculo.IdEmpleado;
        registro.IdConvenio = dato.Revision.Empleado!.IdConvenio;
        registro.IdWallet = vinculo.IdWallet;
        registro.Estado = autorizar ? "AUTORIZADO" : "REVOCADO";
        registro.Revision = checked(registro.Revision + 1);
        registro.HuellaContexto = Contexto(dato);
        registro.ModificadoPorUsuario = idAdmin;
        registro.FechaCambio = ahora;
        registro.ReferenciaRevision = solicitud.ReferenciaRevision.Trim();
        if (autorizar) { registro.FechaAutorizacion = ahora; registro.FechaRevocacion = null; }
        else registro.FechaRevocacion = ahora;
        db.Auditorias.Add(new() { IdUsuario = idAdmin, Modulo = "LIBRANZA", Accion = autorizar ? AccionAutorizar : AccionRevocar,
            Entidad = "libranza_autorizaciones_anticipo", IdEntidad = vinculo.IdEmpleadoUsuario.ToString(),
            ValorAnterior = JsonSerializer.Serialize(new { estado = dato.Autorizacion?.Estado ?? "SIN_AUTORIZACION" }),
            ValorNuevo = JsonSerializer.Serialize(new { producto = "ANTICIPO_NOMINA", idEmpleadoUsuario = vinculo.IdEmpleadoUsuario,
                idUsuario = vinculo.IdUsuario, idEmpleado = vinculo.IdEmpleado, idConvenio = registro.IdConvenio,
                idWallet = vinculo.IdWallet, estado = registro.Estado, revision = registro.Revision,
                metodo = "MANUAL", confirmacionExpresa = true, referencia = registro.ReferenciaRevision }),
            Resultado = "EXITOSO", FechaEvento = ahora });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AutorizacionAnticipoConflictoException(); }
        await tx.CommitAsync(ct);
        dato.Autorizacion = registro;
        dato.AutorizacionAuditada = autorizar;
        dato.TieneHistorial = true;
        return Presentar(dato, ahora);
    }

    // Lectura del filtro HTTP; no sustituye la comprobación bajo lock de desembolso.
    public async Task<bool> PuedeAccederAsync(long idUsuario, CancellationToken ct = default)
    {
        var publico = await RegistroPublicoService.EsAutorregistroAsync(db, idUsuario, ct);
        var registros = await db.LibranzaAutorizacionesAnticipo.AsNoTracking().Where(a => a.IdUsuario == idUsuario).ToListAsync(ct);
        if (!publico && registros.Count == 0) return true; // compatibilidad del piloto nunca administrado
        var vinculos = await db.LibranzaEmpleadoUsuarios.AsNoTracking().Where(v => v.IdUsuario == idUsuario && v.Estado == "ACTIVO").ToListAsync(ct);
        if (vinculos.Count != 1) return false;
        var dato = (await CargarAsync(vinculos, ct)).Single();
        return AutorizadaVigente(dato, DateTime.UtcNow);
    }

    // El llamador ya posee UPDLOCK del empleado en su transacción de dinero.
    // Autorizar/revocar usan ese mismo lock: una revocación confirmada gana a
    // solicitudes que todavía no lo adquirieron. Nunca se crea un permiso aquí.
    internal async Task<LibranzaEmpleadoUsuario> ExigirEnDesembolsoAsync(long idUsuario, long idEmpleado, long idEmpleadoUsuario, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La validación de autorización requiere la transacción del desembolso.");
        var vinculos = await db.LibranzaEmpleadoUsuarios.AsNoTracking().Where(v => v.IdUsuario == idUsuario && v.Estado == "ACTIVO").ToListAsync(ct);
        if (vinculos.Count != 1 || vinculos[0].IdEmpleado != idEmpleado || vinculos[0].IdEmpleadoUsuario != idEmpleadoUsuario)
            throw new UnauthorizedAccessException(MensajeBloqueo);
        if (!await PuedeAccederAsync(idUsuario, ct)) throw new UnauthorizedAccessException(MensajeBloqueo);
        return vinculos[0];
    }

    internal sealed class Datos
    {
        public required LibranzaEmpleadoUsuario Vinculo { get; init; }
        public required LibranzaVinculacionService.DatosRevision Revision { get; init; }
        public LibranzaAutorizacionAnticipo? Autorizacion { get; set; }
        public bool OrigenPublico { get; init; }
        public bool TieneHistorial { get; set; }
        public bool AutorizacionAuditada { get; set; }
        public bool CuentaPublicaLimitada { get; init; }
    }

    internal static bool AutorizadaVigente(Datos d, DateTime ahora) =>
        d.Autorizacion is { Estado: "AUTORIZADO" } a && d.AutorizacionAuditada && !d.CuentaPublicaLimitada && d.Vinculo.Estado == "ACTIVO"
        && a.IdUsuario == d.Vinculo.IdUsuario && a.IdEmpleado == d.Vinculo.IdEmpleado
        && a.IdWallet == d.Vinculo.IdWallet && a.IdConvenio == d.Revision.Empleado?.IdConvenio
        && LibranzaVinculacionService.Evaluar(d.Revision, ahora).Estado == "APROBADO"
        && HuellaIgual(a.HuellaContexto, Contexto(d));

    internal static LibranzaAutorizacionAnticipoResponse Presentar(Datos d, DateTime ahora)
    {
        var revision = LibranzaVinculacionService.Evaluar(d.Revision, ahora);
        var autorizada = AutorizadaVigente(d, ahora);
        var previa = !d.OrigenPublico && !d.TieneHistorial;
        var revocada = d.Autorizacion?.Estado == "REVOCADO";
        var contextoValido = revision.Estado == "APROBADO" && d.Vinculo.Estado == "ACTIVO" && !d.CuentaPublicaLimitada;
        var razones = revision.Razones.ToList();
        if (d.CuentaPublicaLimitada) razones.Add("El titular debe completar la activación de su Wallet desde Perfil.");
        if (d.Autorizacion?.Estado == "AUTORIZADO" && !d.AutorizacionAuditada)
            razones.Add("La autorización requiere una evidencia de revisión de XPAY.");
        else if (d.Autorizacion?.Estado == "AUTORIZADO" && contextoValido && !autorizada)
            razones.Add("Los datos del vínculo cambiaron desde la autorización; revisa nuevamente.");
        return new() { IdEmpleadoUsuario = d.Vinculo.IdEmpleadoUsuario, IdEmpleado = d.Vinculo.IdEmpleado,
            IdUsuario = d.Vinculo.IdUsuario, IdConvenio = revision.IdConvenio, Empresa = revision.Empresa,
            Empleado = revision.Empleado, Usuario = revision.Usuario, DocumentoMasked = revision.DocumentoMasked,
            EmailVerificado = revision.EmailVerificado, IdentidadVerificada = revision.IdentidadVerificada,
            WalletActiva = revision.WalletActiva, OrigenPublico = d.OrigenPublico,
            Estado = autorizada ? "AUTORIZADO" : revocada ? "REVOCADO" : !contextoValido || d.Autorizacion?.Estado == "AUTORIZADO"
                ? "REVISION_REQUERIDA" : previa ? "FLUJO_PREVIO" : "PENDIENTE",
            PuedeAutorizar = contextoValido && !autorizada,
            PuedeRevocar = d.Vinculo.Estado == "ACTIVO" && (d.Autorizacion?.Estado == "AUTORIZADO" || previa),
            Razones = razones, Huella = Huella(d), FechaAutorizacion = Utc(d.Autorizacion?.FechaAutorizacion),
            FechaRevocacion = Utc(d.Autorizacion?.FechaRevocacion) };
    }

    // El permiso durable vincula identidades y recursos, no resultados de cálculo
    // o fechas genéricas de importación. Una nómina reimportada no es una nueva
    // decisión de producto. La evaluación actual sigue revalidando los requisitos.
    internal static string Contexto(Datos d)
    {
        var p = d.Revision.Persona;
        var tipo = p?.TipoDocumentoVeriffRaw?.Trim().ToUpperInvariant() switch { "ID_CARD" => "CC", "PASSPORT" => "PASAPORTE", _ => null };
        return Hash(new { version = 2, vinculo = new { d.Vinculo.IdEmpleadoUsuario, d.Vinculo.IdUsuario,
                d.Vinculo.IdEmpleado, d.Vinculo.IdWallet, d.Vinculo.Estado }, d.OrigenPublico,
            persona = p is null ? null : new { p.IdPersona, p.IdUnidadNegocio,
                documento = LibranzaVinculacionService.ClaveDocumento(tipo, p.NumeroDocumentoVerificado) },
            empleado = d.Revision.Empleado is null ? null : new { d.Revision.Empleado.IdConvenio,
                documento = LibranzaVinculacionService.ClaveDocumento(d.Revision.Empleado.TipoDocumento, d.Revision.Empleado.NumeroDocumento) } });
    }
    // La foto de una acción pendiente sí detecta cualquier cambio desde la
    // consulta, incluidos los metadatos de nómina/identidad actuales.
    internal static string Huella(Datos d) => Hash(new { contexto = Contexto(d),
        revision = LibranzaVinculacionService.CalcularHuella(d.Revision), d.CuentaPublicaLimitada, d.TieneHistorial, d.AutorizacionAuditada,
        autorizacion = d.Autorizacion is null ? null : new { d.Autorizacion.IdEmpleadoUsuario, d.Autorizacion.IdUsuario,
            d.Autorizacion.IdEmpleado, d.Autorizacion.IdConvenio, d.Autorizacion.IdWallet, d.Autorizacion.Estado,
            d.Autorizacion.Revision, d.Autorizacion.HuellaContexto } });
    private static string Hash(object objeto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(objeto)))).ToLowerInvariant();
    private static DateTime? Utc(DateTime? fecha) => fecha.HasValue ? DateTime.SpecifyKind(fecha.Value, DateTimeKind.Utc) : null;
    private static bool HuellaIgual(string? a, string? b) => a is { Length: 64 } && b is { Length: 64 }
        && a.All(c => char.IsAsciiHexDigit(c)) && b.All(c => char.IsAsciiHexDigit(c))
        && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a), Convert.FromHexString(b));

    internal static void ValidarSolicitud(CambiarAutorizacionAnticipoRequest r)
    {
        if (r.IdEmpleadoUsuario <= 0 || !HuellaIgual(r.Huella, r.Huella))
            throw new InvalidOperationException("La selección o su revisión no son válidas.");
        if (!r.ConfirmaAutorizacion) throw new InvalidOperationException("Confirma expresamente la decisión de XPAY sobre Anticipo de Nómina.");
        var referencia = r.ReferenciaRevision?.Trim() ?? "";
        if (referencia.Length is < 5 or > 200 || referencia.Any(char.IsControl))
            throw new InvalidOperationException("Indica una referencia de revisión de entre 5 y 200 caracteres, sin datos sensibles.");
    }

    private async Task<List<Datos>> CargarAsync(List<LibranzaEmpleadoUsuario> vinculos, CancellationToken ct)
    {
        var revisiones = await LibranzaVinculacionService.CargarAsync(db,
            vinculos.Select(v => (v.IdEmpleado, v.IdUsuario)).Distinct().ToList(), ct);
        var ids = vinculos.Select(v => v.IdUsuario).Distinct().ToArray();
        var registros = await db.LibranzaAutorizacionesAnticipo.AsNoTracking().Where(a => ids.Contains(a.IdUsuario)).ToListAsync(ct);
        var entidades = vinculos.Select(v => v.IdEmpleadoUsuario.ToString()).Distinct().ToArray();
        var evidencias = await db.Auditorias.AsNoTracking().Where(a => a.Modulo == "LIBRANZA"
            && a.Accion == AccionAutorizar && a.Entidad == "libranza_autorizaciones_anticipo" && a.Resultado == "EXITOSO"
            && a.IdEntidad != null && entidades.Contains(a.IdEntidad)).ToListAsync(ct);
        var publicos = await db.Auditorias.AsNoTracking().Where(a => a.IdUsuario.HasValue && ids.Contains(a.IdUsuario.Value)
            && a.Modulo == "USUARIOS" && a.Accion == RegistroPublicoService.AccionAceptarTerminos && a.Resultado == "EXITOSO")
            .Select(a => a.IdUsuario!.Value).Distinct().ToListAsync(ct);
        var limitadas = new HashSet<long>();
        foreach (var id in publicos)
            if (await RegistroPublicoService.EsCuentaLimitadaAsync(db, id)) limitadas.Add(id);
        return vinculos.Select(v => new Datos { Vinculo = v,
            Revision = revisiones.Single(r => r.IdUsuario == v.IdUsuario && r.IdEmpleado == v.IdEmpleado),
            Autorizacion = registros.SingleOrDefault(a => a.IdEmpleadoUsuario == v.IdEmpleadoUsuario && a.IdUsuario == v.IdUsuario),
            AutorizacionAuditada = registros.Any(a => a.IdEmpleadoUsuario == v.IdEmpleadoUsuario && a.IdUsuario == v.IdUsuario
                && evidencias.Any(e => EvidenciaCoincide(e, a))),
            OrigenPublico = publicos.Contains(v.IdUsuario), CuentaPublicaLimitada = limitadas.Contains(v.IdUsuario),
            TieneHistorial = registros.Any(a => a.IdUsuario == v.IdUsuario) }).ToList();
    }

    private static bool EvidenciaCoincide(Auditoria evento, LibranzaAutorizacionAnticipo a)
    {
        if (evento.IdUsuario != a.ModificadoPorUsuario || evento.FechaEvento != a.FechaCambio || evento.ValorNuevo is null
            || evento.IdEntidad != a.IdEmpleadoUsuario.ToString()) return false;
        try
        {
            using var json = JsonDocument.Parse(evento.ValorNuevo); var e = json.RootElement;
            return e.GetProperty("producto").GetString() == "ANTICIPO_NOMINA"
                && e.GetProperty("idEmpleadoUsuario").GetInt64() == a.IdEmpleadoUsuario
                && e.GetProperty("idUsuario").GetInt64() == a.IdUsuario && e.GetProperty("idEmpleado").GetInt64() == a.IdEmpleado
                && e.GetProperty("idConvenio").GetInt64() == a.IdConvenio && e.GetProperty("idWallet").GetInt64() == a.IdWallet
                && e.GetProperty("estado").GetString() == "AUTORIZADO" && e.GetProperty("revision").GetInt64() == a.Revision
                && e.GetProperty("metodo").GetString() == "MANUAL" && e.GetProperty("confirmacionExpresa").GetBoolean();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { return false; }
    }

    private async Task ExigirAdministradorAsync(long idAdmin, CancellationToken ct)
    {
        if (idAdmin <= 0 || !await (from u in db.Usuarios.AsNoTracking()
            join ur in db.UsuarioRoles.AsNoTracking() on u.IdUsuario equals ur.IdUsuario
            join r in db.Roles.AsNoTracking() on ur.IdRol equals r.IdRol
            where u.IdUsuario == idAdmin && u.Estado == "ACTIVO" && !u.FechaBloqueo.HasValue && !u.RequiereCambioClave
                && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
                && (r.Codigo == "ADMIN_XPAY" || r.Codigo == "SUPERUSUARIO")
            select u.IdUsuario).AnyAsync(ct))
            throw new UnauthorizedAccessException("Se requiere un administrador activo de XPAY.");
    }
}
