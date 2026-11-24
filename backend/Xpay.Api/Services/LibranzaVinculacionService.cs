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

// Coincidir no crea asociaciones. Aprobar solo enlaza un empleado con su cuenta
// y su Wallet ya existente; no acepta documentos ni crea Wallet, cupos o dinero.
public sealed class LibranzaVinculacionService(XpayDbContext db, ILogger<LibranzaVinculacionService> logger)
{
    public const string ClaveLock = "XPAY:VINCULACION_NOMINA";
    public const string AccionAuditoria = "PERTENENCIA_LABORAL_APROBADA";

    public async Task<LibranzaVinculacionesResponse> ListarAsync(long idAdmin, long? idConvenio = null,
        int pagina = 1, int tamanio = 50, CancellationToken ct = default)
    {
        await ExigirAdministradorAsync(idAdmin, ct);
        if (pagina < 1 || tamanio is < 1 or > 200 || idConvenio is <= 0)
            throw new InvalidOperationException("Página, tamaño o convenio inválidos (máximo 200 por página).");
        // SQL reduce candidatos; la comparación cerrada posterior decide. No
        // inferir identidades por nombre, teléfono o correo ni devolverlos.
        var parejas = from e in db.LibranzaEmpleados.AsNoTracking()
            join u in db.Usuarios.AsNoTracking() on 1 equals 1
            join p in db.Personas.AsNoTracking() on u.IdPersona equals p.IdPersona
            where (!idConvenio.HasValue || e.IdConvenio == idConvenio.Value)
                && p.IdentidadVerificada && p.IdentidadVerificadaProveedor == "VERIFF"
                && u.EstadoKycActual == "APROBADO" && p.NumeroDocumentoVerificado != null
                && ((p.TipoDocumentoVeriffRaw == "ID_CARD" && e.TipoDocumento == "CC")
                    || (p.TipoDocumentoVeriffRaw == "PASSPORT" && e.TipoDocumento == "PASAPORTE"))
                && e.NumeroDocumento.Replace(".", "").Trim().ToUpper()
                    == p.NumeroDocumentoVerificado.Replace(".", "").Trim().ToUpper()
            select new { e.IdEmpleado, u.IdUsuario };
        var total = await parejas.CountAsync(ct);
        var seleccion = await parejas.OrderBy(x => x.IdEmpleado).ThenBy(x => x.IdUsuario)
            .Skip(checked((pagina - 1) * tamanio)).Take(tamanio).ToListAsync(ct);
        var datos = await CargarAsync(db, seleccion.Select(x => (x.IdEmpleado, x.IdUsuario)).ToList(), ct);
        // Una preselección SQL puede contener números sintácticamente inválidos;
        // se muestran bloqueados sin exponer su documento completo.
        return new() { Pagina = pagina, Total = total, TotalPaginas = (int)Math.Ceiling(total / (decimal)tamanio),
            Items = datos.Select(d => Evaluar(d, DateTime.UtcNow)).ToList() };
    }

    public async Task<AprobarVinculacionesResponse> AprobarAsync(long idAdmin,
        AprobarVinculacionesRequest request, CancellationToken ct = default)
    {
        await ExigirAdministradorAsync(idAdmin, ct);
        ValidarSeleccion(request);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La revisión requiere su propia transacción por persona.");
        var respuesta = new AprobarVinculacionesResponse();
        foreach (var s in request.Seleccion)
        {
            ct.ThrowIfCancellationRequested();
            var resultado = new ResultadoVinculacionResponse { IdEmpleado = s.IdEmpleado, IdUsuario = s.IdUsuario };
            try
            {
                // Microlectura fuera de la transacción: solo identifica el recurso
                // de contactos. La identidad se comprueba otra vez bajo locks.
                var idPersonaPrevia = await db.Usuarios.AsNoTracking().Where(u => u.IdUsuario == s.IdUsuario)
                    .Select(u => (long?)u.IdPersona).SingleOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("El usuario ya no está disponible.");
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                // Nunca retener lecturas SQL antes de esperar KYC/contactos.
                if (await AppLockHelper.AdquirirAsync(db, ClaveLock, ct) < 0
                    || await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{s.IdUsuario}", ct) < 0)
                    throw new InvalidOperationException("Hay otra revisión en curso. Actualiza e intenta de nuevo.");
                await ContactoVerificacionService.BloquearPersonaAsync(db, idPersonaPrevia, ct);
                // Comparte el recurso de fila con el asociador histórico y las
                // solicitudes de anticipos. Los índices únicos cubren también
                // solicitudes legacy que compitan por otro empleado/usuario.
                _ = await db.LibranzaEmpleados.FromSqlInterpolated(
                    $"SELECT * FROM dbo.libranza_empleados WITH (UPDLOCK, ROWLOCK) WHERE id_empleado={s.IdEmpleado}")
                    .AsNoTracking().SingleOrDefaultAsync(ct);
                await ExigirAdministradorAsync(idAdmin, ct);
                var d = (await CargarAsync(db, [(s.IdEmpleado, s.IdUsuario)], ct)).Single();
                if (d.Usuario?.IdPersona != idPersonaPrevia)
                    throw new InvalidOperationException("La identidad de la cuenta cambió. Actualiza las coincidencias.");
                var actual = Evaluar(d, DateTime.UtcNow);
                if (!HuellasIguales(s.Huella, actual.Huella))
                    throw new InvalidOperationException("Los datos cambiaron desde la revisión. Actualiza las coincidencias.");
                if (actual.Estado == "APROBADO")
                {
                    resultado.Mensaje = "La cuenta ya estaba vinculada; no se duplicó la aprobación.";
                }
                else
                {
                    if (!actual.Elegible) throw new InvalidOperationException(string.Join(" ", actual.Razones));
                    var wallet = d.Wallets.Single();
                    db.LibranzaEmpleadoUsuarios.Add(new() { IdEmpleado = s.IdEmpleado, IdUsuario = s.IdUsuario,
                        IdWallet = wallet.IdWallet, Estado = "ACTIVO", CreatedAt = DateTime.UtcNow, CreatedByUsuario = idAdmin });
                    db.Auditorias.Add(new() { IdUsuario = idAdmin, Modulo = "LIBRANZA", Accion = AccionAuditoria,
                        Entidad = "libranza_empleado_usuario", IdEntidad = s.IdEmpleado.ToString(),
                        ValorNuevo = JsonSerializer.Serialize(new { idEmpleado = s.IdEmpleado, idUsuario = s.IdUsuario,
                            idConvenio = actual.IdConvenio, idWallet = wallet.IdWallet, metodo = "MANUAL", huella = actual.Huella,
                            referencia = request.ReferenciaRevision.Trim(), pertenenciaLaboralConfirmada = true }),
                        Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                    resultado.Mensaje = "Vinculación aprobada. Los controles del convenio siguen vigentes.";
                }
                await tx.CommitAsync(ct);
                resultado.Aprobado = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (InvalidOperationException ex) { resultado.Mensaje = ex.Message; }
            catch (Exception ex)
            {
                logger.LogWarning("VINCULACION_NOMINA_RECHAZADA empleado={Empleado} usuario={Usuario} tipo={Tipo}",
                    s.IdEmpleado, s.IdUsuario, ex.GetType().Name);
                resultado.Mensaje = "No se pudo completar la revisión. Actualiza el estado antes de intentar nuevamente.";
            }
            finally { db.ChangeTracker.Clear(); }
            respuesta.Resultados.Add(resultado);
        }
        return respuesta;
    }

    internal static void ValidarSeleccion(AprobarVinculacionesRequest request)
    {
        if (request.Seleccion is null || request.Seleccion.Count is < 1 or > 50)
            throw new InvalidOperationException("Selecciona entre 1 y 50 coincidencias.");
        if (!request.ConfirmaPertenenciaLaboral)
            throw new InvalidOperationException("Confirma que revisaste la pertenencia laboral con la empresa.");
        var referencia = request.ReferenciaRevision?.Trim() ?? "";
        if (referencia.Length is < 5 or > 200 || referencia.Any(char.IsControl))
            throw new InvalidOperationException("Indica una referencia de revisión de entre 5 y 200 caracteres, sin datos sensibles.");
        if (request.Seleccion.Any(s => s is null || s.IdEmpleado <= 0 || s.IdUsuario <= 0 || !HuellaValida(s.Huella))
            || request.Seleccion.Select(s => s.IdEmpleado).Distinct().Count() != request.Seleccion.Count
            || request.Seleccion.Select(s => s.IdUsuario).Distinct().Count() != request.Seleccion.Count)
            throw new InvalidOperationException("La selección contiene identificadores repetidos o una revisión inválida.");
    }

    private static bool HuellaValida(string? h) => h?.Length == 64 && h.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool HuellasIguales(string? a, string? b) => HuellaValida(a) && HuellaValida(b)
        && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a!), Convert.FromHexString(b!));

    private async Task ExigirAdministradorAsync(long idAdmin, CancellationToken ct)
    {
        var permitido = idAdmin > 0 && await (from u in db.Usuarios.AsNoTracking()
            join ur in db.UsuarioRoles.AsNoTracking() on u.IdUsuario equals ur.IdUsuario
            join r in db.Roles.AsNoTracking() on ur.IdRol equals r.IdRol
            where u.IdUsuario == idAdmin && u.Estado == "ACTIVO" && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
                && (r.Codigo == "ADMIN_XPAY" || r.Codigo == "SUPERUSUARIO")
            select u.IdUsuario).AnyAsync(ct);
        if (!permitido) throw new UnauthorizedAccessException("Se requiere un administrador activo de XPAY.");
    }

    internal static string? ClaveDocumento(string? tipo, string? numero)
    {
        var t = tipo?.Trim().ToUpperInvariant();
        var n = t == "CC" ? TipoDocumentoVeriffMapper.NumeroComparable(numero)
            : numero is { Length: <= 64 } && numero.Trim().Length is > 0 and <= 30 && numero.Trim().All(char.IsAsciiLetterOrDigit)
                ? numero.Trim().ToUpperInvariant() : null;
        if (n is null || t is not ("CC" or "PASAPORTE") || (t == "CC" && !n.All(char.IsAsciiDigit))) return null;
        return t + ":" + n;
    }
    private static string? TipoVeriff(string? tipo) => tipo?.Trim().ToUpperInvariant() switch
        { "ID_CARD" => "CC", "PASSPORT" => "PASAPORTE", _ => null };
    private static string? ClaveVeriff(Persona p) => ClaveDocumento(TipoVeriff(p.TipoDocumentoVeriffRaw), p.NumeroDocumentoVerificado);

    internal sealed class DatosRevision
    {
        public long IdEmpleado { get; init; }
        public long IdUsuario { get; init; }
        public LibranzaEmpleado? Empleado { get; init; }
        public LibranzaEmpresaConvenio? Convenio { get; init; }
        public Usuario? Usuario { get; init; }
        public Persona? Persona { get; init; }
        public bool RolUsuarioFinalActivo { get; init; }
        public List<KycVerificacion> Kyc { get; init; } = [];
        public List<Wallet> Wallets { get; init; } = [];
        public List<long> EmpleadosMismoDocumento { get; init; } = [];
        public List<long> UsuariosMismoDocumento { get; init; } = [];
        public List<long> PersonasMismoDocumento { get; init; } = [];
        public List<LibranzaEmpleadoUsuario> Vinculos { get; init; } = [];
    }

    internal static LibranzaCoincidenciaResponse Evaluar(DatosRevision d, DateTime ahora)
    {
        var e = d.Empleado; var c = d.Convenio; var u = d.Usuario; var p = d.Persona;
        var razones = new List<string>();
        var identidad = p is not null && u is not null && p.Estado == "ACTIVA"
            && p.IdentidadVerificada && p.IdentidadVerificadaProveedor == "VERIFF" && p.IdentidadVerificadaFecha.HasValue
            && u.EstadoKycActual == "APROBADO" && d.Kyc.Count == 1 && d.Kyc[0].Proveedor == "VERIFF"
            && d.Kyc[0].EstadoKyc == "APROBADO" && d.Kyc[0].IdPersona == u.IdPersona && d.Kyc[0].Decision == "approved";
        if (e is null || c is null || u is null || p is null) razones.Add("No se encontraron todos los datos de la coincidencia.");
        if (e?.Estado != "ACTIVO") razones.Add("El empleado no está activo.");
        if (c?.Estado != "ACTIVO" || c.FechaInicio.Date > ahora.AddHours(-5).Date
            || c.FechaFin?.Date < ahora.AddHours(-5).Date) razones.Add("El convenio no está activo o vigente.");
        if (u?.Estado != "ACTIVO" || u.FechaBloqueo.HasValue || u.RequiereCambioClave)
            razones.Add("La cuenta debe estar activa, sin bloqueos ni cambio de contraseña pendiente.");
        if (!d.RolUsuarioFinalActivo) razones.Add("La cuenta requiere el perfil de usuario final activo.");
        if (!identidad) razones.Add("La identidad requiere una única verificación actual aprobada de Veriff.");
        var clave = p is null ? null : ClaveVeriff(p);
        if (clave is null || e is null || ClaveDocumento(e.TipoDocumento, e.NumeroDocumento) != clave)
            razones.Add("El documento verificado no coincide con el empleado.");
        if (p is not null)
        {
            var tieneTipoLegal = !string.IsNullOrWhiteSpace(p.TipoDocumento);
            var tieneNumeroLegal = !string.IsNullOrWhiteSpace(p.NumeroDocumento);
            // Las cuentas históricas pueden tener ambos campos legales vacíos.
            // En ese caso se lee la misma identidad Veriff ya aprobada y
            // comparable con el empleado; no se completan campos ni se inventa
            // una verificación. Identidad, coincidencia y unicidad se exigen
            // también para estas cuentas en el resto de esta evaluación.
            if (tieneTipoLegal != tieneNumeroLegal)
                razones.Add("El documento legal está incompleto y requiere revisión de XPAY.");
            else if (tieneTipoLegal && (clave is null || ClaveDocumento(p.TipoDocumento, p.NumeroDocumento) != clave))
                razones.Add("El documento legal y el documento verificado no coinciden; requieren revisión de XPAY.");
        }
        if (d.UsuariosMismoDocumento.Distinct().Count() != 1 || !d.UsuariosMismoDocumento.Contains(d.IdUsuario)
            || d.PersonasMismoDocumento.Distinct().Count() != 1 || !d.PersonasMismoDocumento.Contains(p?.IdPersona ?? 0))
            razones.Add("Hay una identidad duplicada o ambigua para ese documento.");
        if (d.EmpleadosMismoDocumento.Distinct().Count() != 1 || !d.EmpleadosMismoDocumento.Contains(d.IdEmpleado))
            razones.Add("El documento aparece en varios empleados o convenios; requiere aclaración de la empresa.");
        if (u?.EmailVerificado != true) razones.Add("Falta verificar el correo electrónico.");
        var walletActiva = d.Wallets.Count == 1 && d.Wallets[0].IdPersona == p?.IdPersona
            && d.Wallets[0].IdUnidadNegocio == p?.IdUnidadNegocio
            && d.Wallets[0].TipoWallet == "PERSONA" && d.Wallets[0].Estado == "ACTIVA";
        if (!walletActiva) razones.Add("El usuario debe completar la activación de su Wallet personal.");
        if (d.Vinculos.Any(v => v.Estado == "ACTIVO" && (v.IdEmpleado != d.IdEmpleado || v.IdUsuario != d.IdUsuario)))
            razones.Add("El empleado o el usuario ya tienen otro vínculo activo.");
        var ambiguo = d.UsuariosMismoDocumento.Distinct().Count() != 1 || d.PersonasMismoDocumento.Distinct().Count() != 1
            || d.EmpleadosMismoDocumento.Distinct().Count() != 1
            || d.Vinculos.Any(v => v.Estado == "ACTIVO" && (v.IdEmpleado != d.IdEmpleado || v.IdUsuario != d.IdUsuario));
        var propios = d.Vinculos.Where(v => v.Estado == "ACTIVO" && v.IdEmpleado == d.IdEmpleado && v.IdUsuario == d.IdUsuario).ToList();
        if (propios.Count > 1 || (propios.Count == 1 && (!walletActiva || propios[0].IdWallet != d.Wallets[0].IdWallet)))
            razones.Add("La Wallet del vínculo existente requiere revisión de XPAY.");
        var aprobado = propios.Count == 1 && razones.Count == 0;
        var numero = p is null ? null : TipoDocumentoVeriffMapper.NumeroComparable(p.NumeroDocumentoVerificado);
        return new() { IdEmpleado = d.IdEmpleado, IdUsuario = d.IdUsuario, IdConvenio = e?.IdConvenio ?? 0,
            Empresa = c?.NombreEmpresa ?? "", Empleado = $"{e?.Nombres} {e?.Apellidos}".Trim(), Usuario = u?.NombreUsuario ?? "",
            DocumentoMasked = numero is null ? "Documento no disponible" : $"{TipoVeriff(p!.TipoDocumentoVeriffRaw)} · ****{(numero.Length > 4 ? numero[^4..] : "")}",
            EmailVerificado = u?.EmailVerificado == true, IdentidadVerificada = identidad, WalletActiva = walletActiva,
            Elegible = razones.Count == 0 && !aprobado, Razones = razones, Huella = CalcularHuella(d),
            Estado = aprobado ? "APROBADO" : propios.Count > 0 || ambiguo ? "REVISION_REQUERIDA" : "PENDIENTE" };
    }

    internal static string CalcularHuella(DatosRevision d)
    {
        var e = d.Empleado; var c = d.Convenio; var u = d.Usuario; var p = d.Persona;
        // Sin credenciales, PIN, saldos, contactos ni URLs. El vínculo propio se
        // excluye solo mientras siga ACTIVO: revocarlo cambia la huella y
        // obliga a revisar de nuevo, sin resucitarlo por replay de un POST viejo.
        var json = JsonSerializer.Serialize(new { version = 2, d.IdEmpleado, d.IdUsuario, d.RolUsuarioFinalActivo,
            empleado = e is null ? null : new { e.IdConvenio, e.TipoDocumento, e.NumeroDocumento, e.Nombres, e.Apellidos, e.Estado, e.UpdatedAt },
            convenio = c is null ? null : new { c.IdConvenio, c.Estado, c.FechaInicio, c.FechaFin, c.UpdatedAt },
            usuario = u is null ? null : new { u.IdPersona, u.Estado, u.EmailVerificado, u.FechaBloqueo, u.RequiereCambioClave, u.EstadoKycActual, u.FechaKycActualizacion },
            persona = p is null ? null : new { p.IdPersona, p.Estado, p.TipoDocumento, p.NumeroDocumento, p.IdentidadVerificada,
                p.IdentidadVerificadaProveedor, p.IdentidadVerificadaFecha, p.TipoDocumentoVeriffRaw, p.NumeroDocumentoVerificado },
            kyc = d.Kyc.OrderBy(k => k.IdKycVerificacion).Select(k => new { k.IdKycVerificacion, k.IdPersona, k.Proveedor, k.EstadoKyc, k.Decision, k.EsActual, k.FechaDecision, k.DecisionTimeVeriff }),
            wallets = d.Wallets.OrderBy(w => w.IdWallet).Select(w => new { w.IdWallet, w.IdPersona, w.IdUnidadNegocio, w.TipoWallet, w.Estado }),
            usuarios = d.UsuariosMismoDocumento.Distinct().Order().ToArray(), personas = d.PersonasMismoDocumento.Distinct().Order().ToArray(),
            empleados = d.EmpleadosMismoDocumento.Distinct().Order().ToArray(),
            otrosVinculos = d.Vinculos.Where(v => v.Estado != "ACTIVO" || v.IdEmpleado != d.IdEmpleado || v.IdUsuario != d.IdUsuario)
                .OrderBy(v => v.IdEmpleadoUsuario).Select(v => new { v.IdEmpleadoUsuario, v.IdEmpleado, v.IdUsuario, v.IdWallet, v.Estado }) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    internal static async Task<List<DatosRevision>> CargarAsync(XpayDbContext db, List<(long Empleado, long Usuario)> pares, CancellationToken ct)
    {
        if (pares.Count == 0) return [];
        var empleadosIds = pares.Select(x => x.Empleado).Distinct().ToArray();
        var usuariosIds = pares.Select(x => x.Usuario).Distinct().ToArray();
        // SERIALIZABLE mantiene filas y búsquedas de duplicados estables durante
        // la aprobación, también ante importaciones que no usan applock.
        var empleados = await db.LibranzaEmpleados.AsNoTracking().Where(x => empleadosIds.Contains(x.IdEmpleado)).ToListAsync(ct);
        var usuarios = await db.Usuarios.AsNoTracking().Where(x => usuariosIds.Contains(x.IdUsuario)).ToListAsync(ct);
        var personasIds = usuarios.Select(x => x.IdPersona).Distinct().ToArray();
        var personas = await db.Personas.AsNoTracking().Where(x => personasIds.Contains(x.IdPersona)).ToListAsync(ct);
        var conveniosIds = empleados.Select(x => x.IdConvenio).Distinct().ToArray();
        var convenios = await db.LibranzaEmpresasConvenio.AsNoTracking().Where(x => conveniosIds.Contains(x.IdConvenio)).ToListAsync(ct);
        var kyc = await db.KycVerificaciones.AsNoTracking().Where(x => usuariosIds.Contains(x.IdUsuario) && x.EsActual).ToListAsync(ct);
        var wallets = await db.Wallets.AsNoTracking().Where(w => w.IdPersona != null && personasIds.Contains(w.IdPersona.Value)
            && w.TipoWallet == "PERSONA" && w.Estado == "ACTIVA").ToListAsync(ct);
        var roles = await (from ur in db.UsuarioRoles.AsNoTracking() join r in db.Roles.AsNoTracking() on ur.IdRol equals r.IdRol
            where usuariosIds.Contains(ur.IdUsuario) && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
            select new { ur.IdUsuario, r.Codigo }).ToListAsync(ct);
        var empresas = await db.LibranzaUsuariosEmpresa.AsNoTracking().Where(a => usuariosIds.Contains(a.IdUsuario) && a.Estado == "ACTIVO")
            .Select(a => a.IdUsuario).Distinct().ToListAsync(ct);
        var finales = roles.GroupBy(r => r.IdUsuario).Where(g => g.Count() == 1 && g.Single().Codigo == "USUARIO_FINAL"
            && !empresas.Contains(g.Key)).Select(g => g.Key).ToList();
        var vinculos = await db.LibranzaEmpleadoUsuarios.AsNoTracking().Where(v =>
            empleadosIds.Contains(v.IdEmpleado) || usuariosIds.Contains(v.IdUsuario)).ToListAsync(ct);
        var numeros = personas.Select(p => TipoDocumentoVeriffMapper.NumeroComparable(p.NumeroDocumentoVerificado)).OfType<string>().Distinct().ToArray();
        var empleadosPares = await db.LibranzaEmpleados.AsNoTracking().Where(e => e.Estado == "ACTIVO"
            && numeros.Contains(e.NumeroDocumento.Replace(".", "").Trim().ToUpper()))
            .Select(e => new { e.IdEmpleado, e.TipoDocumento, e.NumeroDocumento }).ToListAsync(ct);
        var personasPares = await db.Personas.AsNoTracking().Where(p =>
            (p.NumeroDocumentoVerificado != null && numeros.Contains(p.NumeroDocumentoVerificado.Replace(".", "").Trim().ToUpper()))
            || (p.NumeroDocumento != null && numeros.Contains(p.NumeroDocumento.Replace(".", "").Trim().ToUpper())))
            .Select(p => new { p.IdPersona, p.TipoDocumento, p.NumeroDocumento, p.TipoDocumentoVeriffRaw, p.NumeroDocumentoVerificado }).ToListAsync(ct);
        var personasParesIds = personasPares.Select(p => p.IdPersona).ToArray();
        var usuariosPares = await db.Usuarios.AsNoTracking().Where(u => personasParesIds.Contains(u.IdPersona))
            .Select(u => new { u.IdUsuario, u.IdPersona }).ToListAsync(ct);
        return pares.Select(par => { var e = empleados.SingleOrDefault(x => x.IdEmpleado == par.Empleado);
            var u = usuarios.SingleOrDefault(x => x.IdUsuario == par.Usuario); var p = personas.SingleOrDefault(x => x.IdPersona == u?.IdPersona);
            var clave = p is null ? null : ClaveVeriff(p);
            var mismos = clave is null ? [] : personasPares.Where(x => ClaveDocumento(x.TipoDocumento, x.NumeroDocumento) == clave
                || ClaveDocumento(TipoVeriff(x.TipoDocumentoVeriffRaw), x.NumeroDocumentoVerificado) == clave).Select(x => x.IdPersona).Distinct().ToList();
            return new DatosRevision { IdEmpleado = par.Empleado, IdUsuario = par.Usuario, Empleado = e, Usuario = u, Persona = p,
                Convenio = convenios.SingleOrDefault(x => x.IdConvenio == e?.IdConvenio), RolUsuarioFinalActivo = finales.Contains(par.Usuario),
                Kyc = kyc.Where(x => x.IdUsuario == par.Usuario).ToList(), Wallets = wallets.Where(w => w.IdPersona == u?.IdPersona).ToList(),
                Vinculos = vinculos.Where(x => x.IdEmpleado == par.Empleado || x.IdUsuario == par.Usuario).ToList(),
                EmpleadosMismoDocumento = clave is null ? [] : empleadosPares.Where(x => ClaveDocumento(x.TipoDocumento, x.NumeroDocumento) == clave).Select(x => x.IdEmpleado).Distinct().ToList(),
                PersonasMismoDocumento = mismos, UsuariosMismoDocumento = usuariosPares.Where(x => mismos.Contains(x.IdPersona)).Select(x => x.IdUsuario).Distinct().ToList() }; }).ToList();
    }
}
