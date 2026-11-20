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

// Detectar una coincidencia nunca habilita dinero ni cambia la cuenta. Solo
// una revisión explícita de XPAY puede crear la asociación. No existe modo AUTO.
public sealed class LibranzaVinculacionService(
    XpayDbContext db,
    IConfiguration configuracion,
    HabilitacionCuentaPublicaService habilitacion,
    ILogger<LibranzaVinculacionService> logger)
{
    public const string ClaveHabilitado = "XPAY_ALTA_PUBLICA_NOMINA_HABILITADA";
    public const string ClaveLock = "XPAY:VINCULACION_NOMINA";
    public bool Habilitada => configuracion[ClaveHabilitado] == "true";

    private void ExigirHabilitada()
    {
        if (!Habilitada) throw new VinculacionNominaNoDisponibleException();
    }

    public async Task<LibranzaVinculacionesResponse> ListarAsync(
        long idAdmin, long? idConvenio = null, int pagina = 1, int tamanio = 50, CancellationToken ct = default)
    {
        ExigirHabilitada();
        await ExigirAdministradorAsync(db, idAdmin, ct);
        if (pagina < 1 || tamanio is < 1 or > 200 || idConvenio is <= 0)
            throw new InvalidOperationException("Página, tamaño o convenio inválidos (máximo 200 por página).");

        // Reemplazos SQL cerrados solo para reducir el conjunto. La validación
        // conservadora final rechaza Unicode, símbolos desconocidos y tipos sin
        // correspondencia inequívoca. Nunca se compara nombre, correo o teléfono.
        var parejas =
            from e in db.LibranzaEmpleados.AsNoTracking()
            join u in db.Usuarios.AsNoTracking() on 1 equals 1
            join p in db.Personas.AsNoTracking() on u.IdPersona equals p.IdPersona
            where (!idConvenio.HasValue || e.IdConvenio == idConvenio.Value)
                && p.IdentidadVerificada && p.IdentidadVerificadaProveedor == "VERIFF"
                && u.EstadoKycActual == "APROBADO" && p.NumeroDocumentoVerificado != null
                && ((p.TipoDocumentoVeriffRaw == "ID_CARD" && e.TipoDocumento == "CC")
                    || (p.TipoDocumentoVeriffRaw == "PASSPORT" && e.TipoDocumento == "PASAPORTE"))
                && e.NumeroDocumento.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper()
                    == p.NumeroDocumentoVerificado.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper()
            select new { e.IdEmpleado, u.IdUsuario };
        var total = await parejas.CountAsync(ct);
        var seleccion = await parejas.OrderBy(x => x.IdEmpleado).ThenBy(x => x.IdUsuario)
            .Skip(checked((pagina - 1) * tamanio)).Take(tamanio).ToListAsync(ct);
        var datos = await CargarAsync(db, seleccion.Select(x => (x.IdEmpleado, x.IdUsuario)).ToList(), ct);
        return new LibranzaVinculacionesResponse
        {
            Pagina = pagina, Total = total, TotalPaginas = (int)Math.Ceiling(total / (decimal)tamanio),
            Items = datos.Select(d => Evaluar(d, DateTime.UtcNow)).ToList(),
        };
    }

    public async Task<AprobarVinculacionesResponse> AprobarAsync(
        long idAdmin, AprobarVinculacionesRequest request, CancellationToken ct = default)
    {
        ExigirHabilitada();
        await ExigirAdministradorAsync(db, idAdmin, ct);
        ValidarSeleccion(request);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La revisión masiva requiere su propia transacción por elemento.");

        var respuesta = new AprobarVinculacionesResponse();
        foreach (var s in request.Seleccion)
        {
            ct.ThrowIfCancellationRequested();
            var r = new ResultadoVinculacionResponse { IdEmpleado = s.IdEmpleado, IdUsuario = s.IdUsuario };
            try
            {
                // Solo resuelve el recurso de sincronización; se vuelve a
                // comprobar la identidad bajo los bloqueos y la transacción.
                var idPersonaPrevia = await db.Usuarios.AsNoTracking().Where(u => u.IdUsuario == s.IdUsuario)
                    .Select(u => (long?)u.IdPersona).SingleOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("El usuario ya no está disponible.");
                // Serializable mantiene estables las filas y los rangos de
                // duplicados leídos frente a importaciones/KYC concurrentes.
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var adquirido = await AppLockHelper.AdquirirAsync(db, ClaveLock, ct);
                if (adquirido < 0) throw new InvalidOperationException("Hay otra revisión en curso. Actualiza e intenta de nuevo.");
                // Orden común con alta histórica, KYC y edición de contactos.
                // No conservar lecturas SQL antes de esperar estos recursos.
                if (await AppLockHelper.AdquirirAsync(db, AltaUsuarioPilotoService.ClaveLock, ct) < 0
                    || await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{s.IdUsuario}", ct) < 0)
                    throw new InvalidOperationException("Hay otra habilitación en curso. Actualiza e intenta de nuevo.");
                await ContactoVerificacionService.BloquearPersonaAsync(db, idPersonaPrevia, ct);
                ExigirHabilitada();
                await ExigirAdministradorAsync(db, idAdmin, ct);
                var d = (await CargarAsync(db, [(s.IdEmpleado, s.IdUsuario)], ct)).Single();
                if (d.Usuario?.IdPersona != idPersonaPrevia)
                    throw new InvalidOperationException("La identidad de la cuenta cambió. Actualiza las coincidencias.");
                var actual = Evaluar(d, DateTime.UtcNow);
                if (!HuellasIguales(s.Huella, actual.Huella))
                    throw new InvalidOperationException("Los datos cambiaron desde la revisión. Actualiza las coincidencias.");
                var propia = d.Vinculos.SingleOrDefault(v => v.IdEmpleado == s.IdEmpleado && v.IdUsuario == s.IdUsuario && v.Estado == "ACTIVO");
                if (actual.Estado == "APROBADO")
                {
                    r.Aprobado = true;
                    r.Mensaje = "El vínculo ya estaba aprobado; no se duplicó la activación.";
                    await tx.CommitAsync(ct);
                }
                else
                {
                    if (!actual.Elegible)
                        throw new InvalidOperationException(string.Join(" ", actual.Razones));
                    var idWallet = await habilitacion.HabilitarEnTransaccionAsync(s.IdUsuario, idAdmin, ct);
                    var link = propia is null ? new LibranzaEmpleadoUsuario
                    {
                        IdEmpleado = s.IdEmpleado, IdUsuario = s.IdUsuario, Estado = "ACTIVO",
                        CreatedAt = DateTime.UtcNow, CreatedByUsuario = idAdmin,
                    } : await db.LibranzaEmpleadoUsuarios.SingleAsync(v => v.IdEmpleadoUsuario == propia.IdEmpleadoUsuario, ct);
                    if (propia is null) db.LibranzaEmpleadoUsuarios.Add(link);
                    link.IdWallet = idWallet;
                    link.RevisionHuella = actual.Huella;
                    link.RevisionFecha = DateTime.UtcNow;
                    link.RevisionPorUsuario = idAdmin;
                    link.RevisionMetodo = "MANUAL";
                    link.RevisionReferencia = request.ReferenciaRevision.Trim();
                    db.Auditorias.Add(new Auditoria
                    {
                        IdUsuario = idAdmin, Modulo = "LIBRANZA", Accion = "PERTENENCIA_LABORAL_APROBADA",
                        Entidad = "libranza_empleado_usuario", IdEntidad = s.IdEmpleado.ToString(),
                        ValorNuevo = JsonSerializer.Serialize(new { idEmpleado = s.IdEmpleado, idUsuario = s.IdUsuario,
                            idConvenio = actual.IdConvenio, metodo = "MANUAL", huella = actual.Huella,
                            referencia = link.RevisionReferencia, pertenenciaLaboralConfirmada = true }),
                        Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync(ct);
                    await ValidarRevisionVigenteAsync(db, link, ct);
                    await tx.CommitAsync(ct);
                    r.Aprobado = true;
                    r.Mensaje = "Pertenencia laboral aprobada. Los demás controles de operación siguen vigentes.";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (VinculacionNominaNoDisponibleException) { throw; }
            catch (InvalidOperationException ex) { r.Mensaje = ex.Message; }
            catch (Exception ex)
            {
                logger.LogWarning("VINCULACION_NOMINA_RECHAZADA empleado={Empleado} usuario={Usuario} tipo={Tipo}",
                    s.IdEmpleado, s.IdUsuario, ex.GetType().Name);
                r.Mensaje = "No se pudo completar la revisión. Actualiza el estado antes de intentar nuevamente.";
            }
            finally { db.ChangeTracker.Clear(); }
            respuesta.Resultados.Add(r);
        }
        return respuesta;
    }

    internal static void ValidarSeleccion(AprobarVinculacionesRequest request)
    {
        if (request.Seleccion is null || request.Seleccion.Count is < 1 or > 50)
            throw new InvalidOperationException("Selecciona entre 1 y 50 coincidencias.");
        if (!request.ConfirmaPertenenciaLaboral)
            throw new InvalidOperationException("Confirma que revisaste la pertenencia laboral con la empresa.");
        var referencia = request.ReferenciaRevision?.Trim() ?? string.Empty;
        if (referencia.Length is < 5 or > 200 || referencia.Any(char.IsControl))
            throw new InvalidOperationException("Indica una referencia de la revisión de entre 5 y 200 caracteres, sin datos sensibles.");
        if (request.Seleccion.Any(s => s is null || s.IdEmpleado <= 0 || s.IdUsuario <= 0 || !HuellaValida(s.Huella))
            || request.Seleccion.Select(s => s.IdEmpleado).Distinct().Count() != request.Seleccion.Count
            || request.Seleccion.Select(s => s.IdUsuario).Distinct().Count() != request.Seleccion.Count)
            throw new InvalidOperationException("La selección contiene identificadores inválidos, repetidos o una revisión inválida.");
    }

    // Orden compartido con el aprobador. Se adquiere ANTES del UPDLOCK del
    // empleado y de las lecturas retenidas de la revisión. Perfil espera el
    // mismo recurso de contactos antes de leer Persona/Usuario para escribir.
    public static async Task BloquearParaOperacionAsync(XpayDbContext db, LibranzaEmpleadoUsuario asociacion, CancellationToken ct = default)
    {
        if (EsLegacy(asociacion)) return;
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La operación requiere su propia transacción.");
        if (await AppLockHelper.AdquirirAsync(db, ClaveLock, ct) < 0
            || await AppLockHelper.AdquirirAsync(db, $"XPAY:KYC_USUARIO:{asociacion.IdUsuario}", ct) < 0)
            throw new InvalidOperationException("Hay otra revisión u operación en curso. Actualiza e intenta de nuevo.");
        // Esta microlectura solo determina el recurso de contactos. El hint
        // evita retener un S-lock si otro llamador usa SERIALIZABLE antes de
        // esperar al editor de Perfil que ya posee ese recurso de aplicación.
        var idPersona = await db.Usuarios.FromSqlRaw("SELECT * FROM dbo.usuarios WITH (READCOMMITTEDLOCK)").AsNoTracking()
            .Where(u => u.IdUsuario == asociacion.IdUsuario)
            .Select(u => (long?)u.IdPersona).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("El usuario ya no está disponible.");
        await ContactoVerificacionService.BloquearPersonaAsync(db, idPersona, ct);
    }

    // La ruta de desembolso y la vista de cupo deben invocar esta comprobación.
    // Preserva solo vínculos anteriores sin metadata; nunca fabrica una revisión.
    public static async Task ValidarRevisionVigenteAsync(XpayDbContext db, LibranzaEmpleadoUsuario asociacion, CancellationToken ct = default)
    {
        if (EsLegacy(asociacion)) return;
        // El llamador puede haber leído el vínculo antes de esperar un lock.
        // El estado y el destino de dinero deben provenir de la fila vigente,
        // no de aquel objeto, que incluso podría estar revocado o reemplazado.
        var vinculos = db.Database.CurrentTransaction is null ? db.LibranzaEmpleadoUsuarios
            : db.LibranzaEmpleadoUsuarios.FromSqlRaw("SELECT * FROM dbo.libranza_empleado_usuario WITH (HOLDLOCK)");
        var vigente = await vinculos.AsNoTracking().SingleOrDefaultAsync(v => v.IdEmpleadoUsuario == asociacion.IdEmpleadoUsuario, ct);
        if (vigente is null || vigente.Estado != "ACTIVO" || vigente.IdEmpleado != asociacion.IdEmpleado
            || vigente.IdUsuario != asociacion.IdUsuario || vigente.IdWallet != asociacion.IdWallet
            || vigente.RevisionHuella != asociacion.RevisionHuella || vigente.RevisionFecha != asociacion.RevisionFecha
            || vigente.RevisionPorUsuario != asociacion.RevisionPorUsuario || vigente.RevisionMetodo != asociacion.RevisionMetodo
            || vigente.RevisionReferencia != asociacion.RevisionReferencia)
            throw new InvalidOperationException("La vinculación cambió o fue revocada. Actualiza el estado antes de operar.");
        if (!RevisionCompleta(vigente))
            throw new InvalidOperationException("La vinculación requiere una revisión completa de XPAY.");
        var datos = (await CargarAsync(db, [(vigente.IdEmpleado, vigente.IdUsuario)], ct)).Single();
        var actual = Evaluar(datos, DateTime.UtcNow);
        if (!HuellasIguales(vigente.RevisionHuella, actual.Huella)
            || actual.Razones.Count > 0 || actual.Estado != "APROBADO")
            throw new InvalidOperationException("Las condiciones de la vinculación cambiaron. XPAY debe revisar nuevamente la pertenencia laboral.");
        var wallets = db.Database.CurrentTransaction is null ? db.Wallets
            : db.Wallets.FromSqlRaw("SELECT * FROM dbo.wallets WITH (HOLDLOCK)");
        var wallet = await wallets.AsNoTracking().SingleOrDefaultAsync(w => w.IdWallet == vigente.IdWallet, ct);
        if (wallet is null || wallet.IdPersona != datos.Usuario!.IdPersona || wallet.TipoWallet != "PERSONA" || wallet.Estado != "ACTIVA")
            throw new InvalidOperationException("La vinculación no tiene una Wallet activa del mismo titular.");
    }

    internal static bool EsLegacy(LibranzaEmpleadoUsuario a) => a.RevisionHuella is null && a.RevisionFecha is null
        && a.RevisionPorUsuario is null && a.RevisionMetodo is null && a.RevisionReferencia is null;
    private static bool RevisionCompleta(LibranzaEmpleadoUsuario a) => HuellaValida(a.RevisionHuella)
        && a.RevisionFecha.HasValue && a.RevisionPorUsuario > 0 && a.RevisionMetodo == "MANUAL"
        && a.RevisionReferencia?.Trim().Length is >= 5 and <= 200;
    private static bool HuellaValida(string? h) => h?.Length == 64 && h.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool HuellasIguales(string? a, string? b) => HuellaValida(a) && HuellaValida(b)
        && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a!), Convert.FromHexString(b!));

    private static async Task ExigirAdministradorAsync(XpayDbContext db, long idAdmin, CancellationToken ct)
    {
        var permitido = idAdmin > 0 && await (
            from u in db.Usuarios.AsNoTracking()
            join ur in db.UsuarioRoles.AsNoTracking() on u.IdUsuario equals ur.IdUsuario
            join r in db.Roles.AsNoTracking() on ur.IdRol equals r.IdRol
            where u.IdUsuario == idAdmin && u.Estado == "ACTIVO" && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
                && (r.Codigo == "ADMIN_XPAY" || r.Codigo == "SUPERUSUARIO")
            select u.IdUsuario).AnyAsync(ct);
        if (!permitido) throw new UnauthorizedAccessException("Se requiere un administrador activo de XPAY.");
    }

    internal static string? NormalizarNumero(string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero) || numero.Length > 80) return null;
        var result = new StringBuilder();
        foreach (var c in numero.Trim().ToUpperInvariant())
        {
            if (c is ' ' or '.' or '-') continue;
            if (c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9')) return null;
            result.Append(c);
        }
        return result.Length is >= 3 and <= 50 ? result.ToString() : null;
    }

    private static string? Clave(string? tipo, string? numero)
    {
        var t = tipo?.Trim().ToUpperInvariant();
        var n = NormalizarNumero(numero);
        if (n is null || t is not ("CC" or "CE" or "PASAPORTE")) return null;
        if (t is "CC" or "CE" && n.Any(c => c is < '0' or > '9')) return null;
        return t + ":" + n;
    }
    private static string? TipoVeriff(string? tipo) => tipo switch { "ID_CARD" => "CC", "PASSPORT" => "PASAPORTE", _ => null };
    private static string? ClaveVeriff(Persona p) => Clave(TipoVeriff(p.TipoDocumentoVeriffRaw), p.NumeroDocumentoVerificado);

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
        public List<LibranzaParametrosEmpresa> Parametros { get; init; } = [];
        public List<LibranzaEmpleadoCortesPago> Cortes { get; init; } = [];
        public List<long> EmpleadosMismoDocumento { get; init; } = [];
        public List<long> UsuariosMismoDocumento { get; init; } = [];
        public List<LibranzaEmpleadoUsuario> Vinculos { get; init; } = [];
    }

    internal static LibranzaCoincidenciaResponse Evaluar(DatosRevision d, DateTime ahora)
    {
        var e = d.Empleado; var c = d.Convenio; var u = d.Usuario; var p = d.Persona;
        var razones = new List<string>();
        var identidad = p is not null && u is not null && p.Estado == "ACTIVA"
            && p.IdentidadVerificada && p.IdentidadVerificadaProveedor == "VERIFF" && p.IdentidadVerificadaFecha.HasValue
            && u.EstadoKycActual == "APROBADO" && d.Kyc.Count == 1 && d.Kyc[0].Proveedor == "VERIFF"
            && d.Kyc[0].EstadoKyc == "APROBADO" && d.Kyc[0].IdPersona == u.IdPersona
            && string.Equals(d.Kyc[0].Decision, "approved", StringComparison.OrdinalIgnoreCase);
        if (e is null || c is null || u is null || p is null) razones.Add("No se encontraron todos los datos de la coincidencia.");
        if (e?.Estado != "ACTIVO") razones.Add("El empleado no está activo.");
        if (c?.Estado != "ACTIVO" || c.FechaInicio.Date > ahora.AddHours(-5).Date
            || c.FechaFin?.Date < ahora.AddHours(-5).Date) razones.Add("El convenio no está activo o vigente.");
        if (u?.Estado != "ACTIVO" || u.FechaBloqueo.HasValue || u.RequiereCambioClave)
            razones.Add("El usuario debe estar activo, sin bloqueos ni cambio de contraseña pendiente.");
        if (!d.RolUsuarioFinalActivo) razones.Add("La cuenta requiere el rol de usuario final activo.");
        if (!identidad) razones.Add("La identidad requiere una única verificación actual aprobada de Veriff.");
        var clave = p is null ? null : ClaveVeriff(p);
        if (clave is null || e is null || Clave(e.TipoDocumento, e.NumeroDocumento) != clave)
            razones.Add("El tipo y número de documento verificado no coinciden con el empleado.");
        if (p is not null && (Clave(p.TipoDocumento, p.NumeroDocumento) != clave
            || p.NumeroDocumentoVerificado?.Trim().Length is not (>= 3 and <= 30)
            || p.NumeroDocumentoVerificado.Any(ch => !char.IsAsciiLetterOrDigit(ch))
            || p.NumeroDocumento?.Trim().ToUpperInvariant() != p.NumeroDocumentoVerificado.Trim().ToUpperInvariant()))
            razones.Add("El documento legal y el documento verificado requieren revisión.");
        if (d.UsuariosMismoDocumento.Distinct().Count() != 1 || !d.UsuariosMismoDocumento.Contains(d.IdUsuario))
            razones.Add("Hay una identidad de usuario duplicada o ambigua para ese documento.");
        if (d.EmpleadosMismoDocumento.Distinct().Count() != 1 || !d.EmpleadosMismoDocumento.Contains(d.IdEmpleado))
            razones.Add("El documento aparece en varios empleados o convenios; requiere aclaración de la empresa.");
        if (u?.EmailVerificado != true) razones.Add("Falta verificar el correo electrónico.");
        if (u?.CelularVerificado != true) razones.Add("Falta verificar WhatsApp.");
        if (d.Vinculos.Any(v => v.Estado == "ACTIVO" && (v.IdEmpleado != d.IdEmpleado || v.IdUsuario != d.IdUsuario)))
            razones.Add("El empleado o el usuario ya tienen otro vínculo activo.");
        ValidarCondiciones(d, razones);
        var huella = CalcularHuella(d);
        var propio = d.Vinculos.SingleOrDefault(v => v.Estado == "ACTIVO" && v.IdEmpleado == d.IdEmpleado && v.IdUsuario == d.IdUsuario);
        var aprobado = propio is not null && (EsLegacy(propio) || (RevisionCompleta(propio) && HuellasIguales(propio.RevisionHuella, huella))) && razones.Count == 0;
        var numero = p is null ? null : NormalizarNumero(p.NumeroDocumentoVerificado);
        var porcentaje = d.Parametros.Count == 1 ? d.Parametros[0].PorcentajeMaximoCupo : 0m;
        return new LibranzaCoincidenciaResponse
        {
            IdEmpleado = d.IdEmpleado, IdUsuario = d.IdUsuario, IdConvenio = e?.IdConvenio ?? 0,
            Empresa = c?.NombreEmpresa ?? string.Empty, Empleado = $"{e?.Nombres} {e?.Apellidos}".Trim(),
            Usuario = u?.NombreUsuario ?? string.Empty,
            DocumentoMasked = numero is null ? "Documento no disponible" : $"{TipoVeriff(p!.TipoDocumentoVeriffRaw)} · ****{(numero.Length > 4 ? numero[^4..] : string.Empty)}",
            CupoPreliminar = e?.CupoPreliminar ?? 0m,
            CuposPorCorte = d.Cortes.OrderBy(x => x.NumeroCorte).Select(x => new CupoCorteRevisionResponse
            {
                NumeroCorte = x.NumeroCorte, DiaPago = x.DiaPago, ValorPagoProgramado = x.ValorPagoProgramado,
                PorcentajeCupo = porcentaje, CupoBase = x.ValorPagoProgramado * porcentaje / 100m,
            }).ToList(),
            EmailVerificado = u?.EmailVerificado == true,
            WhatsAppVerificado = u?.CelularVerificado == true, IdentidadVerificada = identidad,
            Elegible = razones.Count == 0 && !aprobado, Razones = razones, Huella = huella,
            Estado = aprobado ? "APROBADO" : propio is null ? "PENDIENTE" : "REVISION_REQUERIDA",
        };
    }

    private static void ValidarCondiciones(DatosRevision d, List<string> razones)
    {
        var e = d.Empleado;
        if (e is null) return;
        var cantidad = e.PeriodicidadPago switch { "MENSUAL" => 1, "QUINCENAL" => 2, "DECADAL" => 3, "SEMANAL" => 4, _ => 0 };
        var dias = new[] { e.DiaPago1, e.DiaPago2, e.DiaPago3, e.DiaPago4 };
        if (e.SalarioMensual <= 0 || e.CupoPreliminar <= 0 || cantidad == 0 || d.Cortes.Count != cantidad
            || d.Cortes.Select(x => x.NumeroCorte).Distinct().Count() != cantidad
            || d.Cortes.Select(x => x.DiaPago).Distinct().Count() != cantidad
            || d.Cortes.Any(x => x.NumeroCorte < 1 || x.NumeroCorte > cantidad || x.DiaPago is < 1 or > 31
                || x.ValorPagoProgramado <= 0 || dias[x.NumeroCorte - 1] != x.DiaPago))
            razones.Add("La empresa debe completar salario, cupo preliminar y cortes de pago válidos.");
        if (d.Parametros.Count != 1 || d.Parametros[0].PorcentajeMaximoCupo is <= 0 or > 100
            || d.Parametros[0].MomentoCobroComision != "VENCIDO" || d.Parametros[0].MontoMinimoAnticipo <= 0
            || e.SalarioMensual < d.Parametros[0].SalarioMinimoEmpleado || e.SalarioMensual > d.Parametros[0].SalarioMaximoEmpleado)
            razones.Add("El convenio requiere parámetros de anticipo únicos, activos y válidos.");
        // El cupo efectivo se calcula sobre cada pago programado. Una fila con
        // salario pequeño y pagos inflados no debe convertirse en más crédito.
        // DECIMAL(18,2) redondea el preliminar al persistir; usar el mismo
        // redondeo comercial evita rechazar diferencias legítimas de centavos.
        if (d.Cortes.Sum(x => x.ValorPagoProgramado) > e.SalarioMensual
            || decimal.Round(e.SalarioMensual, 2) != e.SalarioMensual
            || decimal.Round(e.CupoPreliminar, 2) != e.CupoPreliminar
            || d.Cortes.Any(x => decimal.Round(x.ValorPagoProgramado, 2) != x.ValorPagoProgramado)
            || (d.Parametros.Count == 1 && e.CupoPreliminar > decimal.Round(
                e.SalarioMensual * d.Parametros[0].PorcentajeMaximoCupo / 100m, 2, MidpointRounding.AwayFromZero)))
            razones.Add("Los pagos de los cortes y el cupo preliminar deben corresponder al salario y porcentaje del convenio.");
    }

    internal static string CalcularHuella(DatosRevision d)
    {
        var e = d.Empleado; var c = d.Convenio; var u = d.Usuario; var p = d.Persona;
        // Excluye datos de acceso, PIN, saldos, URLs y la propia activación de
        // Wallet/limitación. Una nueva sesión de login no invalida una revisión.
        var json = JsonSerializer.Serialize(new
        {
            version = 1, d.IdEmpleado, d.IdUsuario, d.RolUsuarioFinalActivo,
            empleado = e is null ? null : new { e.IdConvenio, e.TipoDocumento, e.NumeroDocumento, e.Nombres, e.Apellidos,
                e.SalarioMensual, e.PeriodicidadPago, e.DiaPago1, e.DiaPago2, e.DiaPago3, e.DiaPago4,
                e.FechaIngreso, e.Estado, e.CupoPreliminar, e.LoteImportacion, e.UpdatedAt },
            convenio = c is null ? null : new { c.IdConvenio, c.Nit, c.NombreEmpresa, c.Estado, c.FechaInicio, c.FechaFin,
                c.PorcentajeMaximoCupo, c.PeriodicidadPago, c.PermiteAnticipodiaPago, c.UpdatedAt },
            usuario = u is null ? null : new { u.IdPersona, u.Estado, u.EmailVerificado, u.CelularVerificado,
                u.RequiereCambioClave, u.FechaBloqueo, u.EstadoKycActual, u.FechaKycActualizacion },
            persona = p is null ? null : new { p.IdPersona, p.IdUnidadNegocio, p.Estado, p.TipoDocumento, p.NumeroDocumento,
                p.IdentidadVerificada, p.IdentidadVerificadaProveedor, p.IdentidadVerificadaFecha,
                p.TipoDocumentoVeriffRaw, p.NumeroDocumentoVerificado, p.NombreVerificadoCompleto,
                p.ApellidoVerificadoCompleto, p.Email, p.Celular },
            kyc = d.Kyc.OrderBy(x => x.IdKycVerificacion).Select(x => new { x.IdKycVerificacion, x.IdPersona,
                x.Proveedor, x.EstadoKyc, x.Decision, x.EsActual, x.FechaDecision, x.DecisionTimeVeriff }),
            parametros = d.Parametros.OrderBy(x => x.IdParametro).Select(x => new { x.IdParametro, x.PorcentajeMaximoCupo,
                x.SalarioMinimoEmpleado, x.SalarioMaximoEmpleado, x.RequiereValidacionEmpresa,
                x.IvaPorcentaje, x.MomentoCobroComision, x.MontoMinimoAnticipo, x.Estado, x.UpdatedAt }),
            cortes = d.Cortes.OrderBy(x => x.IdCortePago).Select(x => new { x.IdCortePago, x.NumeroCorte, x.DiaPago,
                x.ValorPagoProgramado, x.Estado, x.UpdatedAt }),
            usuariosDocumento = d.UsuariosMismoDocumento.Distinct().Order().ToArray(),
            empleadosDocumento = d.EmpleadosMismoDocumento.Distinct().Order().ToArray(),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static async Task<List<DatosRevision>> CargarAsync(XpayDbContext db, List<(long Empleado, long Usuario)> pares, CancellationToken ct)
    {
        if (pares.Count == 0) return [];
        // Dentro de un desembolso las condiciones revisadas permanecen leídas
        // bajo bloqueo hasta el COMMIT, incluso si la transacción del llamador
        // es READ COMMITTED. Fuera de una operación la bandeja es solo lectura.
        var retener = db.Database.CurrentTransaction is not null;
        IQueryable<LibranzaEmpleado> qe = retener ? db.LibranzaEmpleados.FromSqlRaw("SELECT * FROM dbo.libranza_empleados WITH (HOLDLOCK)") : db.LibranzaEmpleados;
        IQueryable<Usuario> qu = retener ? db.Usuarios.FromSqlRaw("SELECT * FROM dbo.usuarios WITH (HOLDLOCK)") : db.Usuarios;
        IQueryable<Persona> qp = retener ? db.Personas.FromSqlRaw("SELECT * FROM dbo.personas WITH (HOLDLOCK)") : db.Personas;
        IQueryable<LibranzaEmpresaConvenio> qc = retener ? db.LibranzaEmpresasConvenio.FromSqlRaw("SELECT * FROM dbo.libranza_empresas_convenio WITH (HOLDLOCK)") : db.LibranzaEmpresasConvenio;
        IQueryable<LibranzaParametrosEmpresa> qparam = retener ? db.LibranzaParametrosEmpresas.FromSqlRaw("SELECT * FROM dbo.libranza_parametros_empresa WITH (HOLDLOCK)") : db.LibranzaParametrosEmpresas;
        IQueryable<LibranzaEmpleadoCortesPago> qcp = retener ? db.LibranzaEmpleadoCortesPago.FromSqlRaw("SELECT * FROM dbo.libranza_empleado_cortes_pago WITH (HOLDLOCK)") : db.LibranzaEmpleadoCortesPago;
        IQueryable<KycVerificacion> qk = retener ? db.KycVerificaciones.FromSqlRaw("SELECT * FROM dbo.kyc_verificaciones WITH (HOLDLOCK)") : db.KycVerificaciones;
        IQueryable<LibranzaEmpleadoUsuario> qv = retener ? db.LibranzaEmpleadoUsuarios.FromSqlRaw("SELECT * FROM dbo.libranza_empleado_usuario WITH (HOLDLOCK)") : db.LibranzaEmpleadoUsuarios;
        var empleadosIds = pares.Select(x => x.Empleado).Distinct().ToArray();
        var usuariosIds = pares.Select(x => x.Usuario).Distinct().ToArray();
        var empleados = await qe.AsNoTracking().Where(x => empleadosIds.Contains(x.IdEmpleado)).ToListAsync(ct);
        var usuarios = await qu.AsNoTracking().Where(x => usuariosIds.Contains(x.IdUsuario)).ToListAsync(ct);
        var personasIds = usuarios.Select(x => x.IdPersona).Distinct().ToArray();
        var personas = await qp.AsNoTracking().Where(x => personasIds.Contains(x.IdPersona)).ToListAsync(ct);
        var conveniosIds = empleados.Select(x => x.IdConvenio).Distinct().ToArray();
        var convenios = await qc.AsNoTracking().Where(x => conveniosIds.Contains(x.IdConvenio)).ToListAsync(ct);
        var parametros = await qparam.AsNoTracking().Where(x => conveniosIds.Contains(x.IdConvenio) && x.Estado == "ACTIVO").ToListAsync(ct);
        var cortes = await qcp.AsNoTracking().Where(x => empleadosIds.Contains(x.IdEmpleado) && x.Estado == "ACTIVO").ToListAsync(ct);
        var kyc = await qk.AsNoTracking().Where(x => usuariosIds.Contains(x.IdUsuario) && x.EsActual).ToListAsync(ct);
        IQueryable<UsuarioRol> qur = retener ? db.UsuarioRoles.FromSqlRaw("SELECT * FROM dbo.usuario_roles WITH (HOLDLOCK)") : db.UsuarioRoles;
        IQueryable<Rol> qr = retener ? db.Roles.FromSqlRaw("SELECT * FROM dbo.roles WITH (HOLDLOCK)") : db.Roles;
        var finales = await (from ur in qur.AsNoTracking() join r in qr.AsNoTracking() on ur.IdRol equals r.IdRol
            where usuariosIds.Contains(ur.IdUsuario) && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO" && r.Codigo == "USUARIO_FINAL"
            select ur.IdUsuario).Distinct().ToListAsync(ct);
        var vinculos = await qv.AsNoTracking()
            .Where(x => (empleadosIds.Contains(x.IdEmpleado) || usuariosIds.Contains(x.IdUsuario)) && x.Estado == "ACTIVO").ToListAsync(ct);
        var numeros = personas.Select(p => NormalizarNumero(p.NumeroDocumentoVerificado)).OfType<string>().Distinct().ToArray();
        var empleadosPares = await qe.AsNoTracking().Where(x => x.Estado == "ACTIVO"
            && numeros.Contains(x.NumeroDocumento.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper()))
            .Select(x => new { x.IdEmpleado, x.TipoDocumento, x.NumeroDocumento }).ToListAsync(ct);
        var usuariosPares = await (
            from u in qu.AsNoTracking()
            join p in qp.AsNoTracking() on u.IdPersona equals p.IdPersona
            where (p.NumeroDocumentoVerificado != null && numeros.Contains(p.NumeroDocumentoVerificado.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper()))
                || (p.NumeroDocumento != null && numeros.Contains(p.NumeroDocumento.Replace(" ", "").Replace(".", "").Replace("-", "").ToUpper()))
            select new { u.IdUsuario, p.TipoDocumento, p.NumeroDocumento, p.TipoDocumentoVeriffRaw, p.NumeroDocumentoVerificado }).ToListAsync(ct);
        return pares.Select(par =>
        {
            var e = empleados.SingleOrDefault(x => x.IdEmpleado == par.Empleado);
            var u = usuarios.SingleOrDefault(x => x.IdUsuario == par.Usuario);
            var p = personas.SingleOrDefault(x => x.IdPersona == u?.IdPersona);
            var clave = p is null ? null : ClaveVeriff(p);
            return new DatosRevision
            {
                IdEmpleado = par.Empleado, IdUsuario = par.Usuario, Empleado = e, Usuario = u, Persona = p,
                RolUsuarioFinalActivo = finales.Contains(par.Usuario),
                Convenio = convenios.SingleOrDefault(x => x.IdConvenio == e?.IdConvenio),
                Kyc = kyc.Where(x => x.IdUsuario == par.Usuario).ToList(),
                Parametros = parametros.Where(x => x.IdConvenio == e?.IdConvenio).ToList(),
                Cortes = cortes.Where(x => x.IdEmpleado == par.Empleado).ToList(),
                Vinculos = vinculos.Where(x => x.IdEmpleado == par.Empleado || x.IdUsuario == par.Usuario).ToList(),
                EmpleadosMismoDocumento = clave is null ? [] : empleadosPares.Where(x => Clave(x.TipoDocumento, x.NumeroDocumento) == clave).Select(x => x.IdEmpleado).Distinct().ToList(),
                UsuariosMismoDocumento = clave is null ? [] : usuariosPares.Where(x => Clave(x.TipoDocumento, x.NumeroDocumento) == clave
                    || Clave(TipoVeriff(x.TipoDocumentoVeriffRaw), x.NumeroDocumentoVerificado) == clave).Select(x => x.IdUsuario).Distinct().ToList(),
            };
        }).ToList();
    }
}

public sealed class VinculacionNominaNoDisponibleException : Exception
{
    public VinculacionNominaNoDisponibleException() : base("La aprobación de nuevas vinculaciones de nómina aún no está habilitada.") { }
}
