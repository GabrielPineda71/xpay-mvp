using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-399 (Perfil Fase 2A) — lectura y edición self-service de datos
// personales DECLARADOS. Frontera deliberada, decisión de producto
// (XPAY-398/399, NO reinterpretable por este servicio):
//
//   EDITABLE aquí:      Celular, Email, Direccion, Ciudad, Departamento.
//   NUNCA EDITABLE en el PATCH de contactos: PrimerNombre/SegundoNombre/PrimerApellido/
//                        SegundoApellido/TipoDocumento/NumeroDocumento/
//                        FechaNacimiento/Pais (identidad legal — incluso
//                        antes de que exista IdentidadVerificada, para no
//                        introducir una regla distinta pre/post-KYC);
//                        ningún campo *Verificado*/VeriffRaw/
//                        IdentidadVerificada/EstadoKycActual (resultado
//                        crudo de Veriff); nada de Usuario relacionado con
//                        seguridad (password/roles/estado/
//                        RequiereCambioClave/username).
//
// Esta clase NUNCA llama a Veriff/Passport, NUNCA activa EmailVerificado/
// CelularVerificado a true (eso solo lo hace ContactoVerificacionService con
// el código correcto), y siempre los deja en false cuando el dato declarado
// correspondiente cambia — ver XPAY-399 PASO 4.
//
// El cómputo de "qué cambia" vive en PerfilActualizacionPlanner. El servicio
// serializa los cambios con la verificación y guarda perfil, auditoría y
// revocación de códigos en una sola transacción.
public class PerfilService
{
    internal const string AccionPrimerApellido = "COMPLETAR_PRIMER_APELLIDO_DECLARADO";
    private readonly XpayDbContext _db;
    public PerfilService(XpayDbContext db) => _db = db;

    public async Task<MiPerfilResponseDto> ObtenerAsync(long idPersona)
    {
        var persona = await _db.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.IdPersona == idPersona)
            ?? throw new KeyNotFoundException("No se encontró la persona asociada a este usuario.");
        var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdPersona == idPersona)
            ?? throw new KeyNotFoundException("No se encontró el usuario asociado a esta persona.");

        return await MapearRespuestaAsync(persona, usuario);
    }

    public async Task<MiPerfilResponseDto> ActualizarAsync(long idUsuario, long idPersona, ActualizarMiPerfilRequest request)
    {
        await using var transaccion = _db.Database.IsSqlServer()
            ? await _db.Database.BeginTransactionAsync()
            : null;
        if (transaccion is not null)
            await ContactoVerificacionService.BloquearPersonaAsync(_db, idPersona);

        var persona = _db.Personas.Local.SingleOrDefault(p => p.IdPersona == idPersona);
        if (persona is null)
            persona = await _db.Personas.FirstOrDefaultAsync(p => p.IdPersona == idPersona)
                ?? throw new KeyNotFoundException("No se encontró la persona asociada a este usuario.");
        else if (transaccion is not null)
            await _db.Entry(persona).ReloadAsync();

        var usuario = _db.Usuarios.Local.SingleOrDefault(u => u.IdPersona == idPersona && u.IdUsuario == idUsuario);
        if (usuario is null)
            usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.IdPersona == idPersona && u.IdUsuario == idUsuario)
                ?? throw new KeyNotFoundException("No se encontró el usuario asociado a esta persona.");
        else if (transaccion is not null)
            await _db.Entry(usuario).ReloadAsync();

        var actuales = new PerfilActualizacionPlanner.CamposActuales(
            persona.Celular, persona.Email, persona.Direccion, persona.Ciudad, persona.Departamento);

        // Puede lanzar InvalidOperationException (validación) — el
        // controller la mapea a 400. Nada se ha tocado todavía en este punto.
        var plan = PerfilActualizacionPlanner.Calcular(actuales, request);

        if (plan.Cambios.Count > 0)
        {
            persona.Celular      = plan.CelularNuevo;
            persona.Email        = plan.EmailNuevo;
            persona.Direccion    = plan.DireccionNuevo;
            persona.Ciudad       = plan.CiudadNuevo;
            persona.Departamento = plan.DepartamentoNuevo;
            persona.FechaActualizacion = DateTime.UtcNow;

            if (plan.ResetearCelularVerificado) usuario.CelularVerificado = false;
            if (plan.ResetearEmailVerificado)   usuario.EmailVerificado   = false;

            var antes   = new Dictionary<string, object?>(StringComparer.Ordinal);
            var despues = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (campo, cambio) in plan.Cambios)
            {
                antes[campo]   = cambio.Antes;
                despues[campo] = cambio.Despues;
            }

            // El PATCH histórico admite texto libre. Cambiar la localidad no
            // puede conservar la marca de una selección anterior del catálogo.
            if (persona.IdCiudadDane.HasValue &&
                (plan.Cambios.ContainsKey("ciudad") || plan.Cambios.ContainsKey("departamento")))
            {
                antes["id_ciudad_dane"] = persona.IdCiudadDane;
                despues["id_ciudad_dane"] = null;
                persona.IdCiudadDane = null;
            }

            // Auditoría persistente (tabla `auditoria` ya existente, mismo
            // patrón que UsuarioAdminService.RegistrarAuditoria). Solo los
            // campos que realmente cambiaron — nunca password/tokens/KYC crudo.
            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario     = idUsuario,
                IdPersona     = idPersona,
                Modulo        = "PERFIL",
                Accion        = "ACTUALIZAR_DATOS_PERSONALES",
                Entidad       = "Persona",
                IdEntidad     = idPersona.ToString(),
                ValorAnterior = JsonSerializer.Serialize(antes),
                ValorNuevo    = JsonSerializer.Serialize(despues),
                Resultado     = "EXITOSO",
                Observacion   = $"Campos modificados: {string.Join(", ", plan.Cambios.Keys)}",
                FechaEvento   = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync();

            if (transaccion is not null && (plan.ResetearCelularVerificado || plan.ResetearEmailVerificado))
                await RevocarContactosAsync(idPersona, plan);
        }
        // Sin cambios reales -> no se escribe nada (ni Persona ni auditoría):
        // evita una fila de auditoría engañosa que sugiera una modificación
        // que nunca ocurrió (ver XPAY-399 PASO 10, caso de test explícito).

        if (transaccion is not null)
            await transaccion.CommitAsync();

        return await MapearRespuestaAsync(persona, usuario);
    }

    public async Task<MiPerfilResponseDto> CompletarPrimerApellidoAsync(
        long idUsuario, long idPersona, CompletarPrimerApellidoRequest request, CancellationToken ct = default)
    {
        var apellido = PrimerApellidoDeclaradoReglas.Normalizar(request.PrimerApellido);
        if (_db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Completar el apellido requiere su propia transacción.");
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // Mismo orden de la sincronización KYC: usuario KYC, después persona/contactos.
        if (await AppLockHelper.AdquirirAsync(_db, $"XPAY:KYC_USUARIO:{idUsuario}", ct) < 0)
            throw new InvalidOperationException("Hay una actualización de identidad en curso. Intenta nuevamente.");
        await ContactoVerificacionService.BloquearPersonaAsync(_db, idPersona, ct);
        var usuario = await _db.Usuarios.SingleOrDefaultAsync(
            u => u.IdUsuario == idUsuario && u.IdPersona == idPersona, ct)
            ?? throw new KeyNotFoundException("No se encontró la cuenta asociada a esta solicitud.");
        await _db.Entry(usuario).ReloadAsync(ct);
        if (usuario.IdPersona != idPersona)
            throw new KeyNotFoundException("No se encontró la cuenta asociada a esta solicitud.");
        var persona = await _db.Personas.SingleOrDefaultAsync(p => p.IdPersona == idPersona, ct)
            ?? throw new KeyNotFoundException("No se encontró la persona asociada a esta cuenta.");
        await _db.Entry(persona).ReloadAsync(ct);
        var actuales = await KycActualesAsync(idUsuario, ct);
        if (!PrimerApellidoDeclaradoReglas.TieneIdentidadVigente(usuario, persona, actuales))
            throw new UnauthorizedAccessException("Confirma tu correo y completa la verificación de identidad antes de continuar.");

        if (!string.IsNullOrWhiteSpace(persona.PrimerApellido))
        {
            if (!PrimerApellidoDeclaradoReglas.Equivalentes(persona.PrimerApellido, apellido))
                throw new PrimerApellidoYaRegistradoException();
            await tx.CommitAsync(ct);
            return await MapearRespuestaAsync(persona, usuario, ct);
        }
        if (!PrimerApellidoDeclaradoReglas.CoincideConCompleto(apellido, persona.ApellidoVerificadoCompleto, persona.SegundoApellido))
            throw new InvalidOperationException("El primer apellido no coincide con la identidad verificada. Revísalo tal como aparece en tu documento.");

        persona.PrimerApellido = apellido;
        persona.FechaActualizacion = DateTime.UtcNow;
        _db.Auditorias.Add(new Auditoria
        {
            IdUsuario = idUsuario, IdPersona = idPersona, Modulo = "PERFIL",
            Accion = AccionPrimerApellido, Entidad = "Persona", IdEntidad = idPersona.ToString(),
            Resultado = "EXITOSO", FechaEvento = DateTime.UtcNow,
            Observacion = "Primer apellido declarado expresamente por el titular y contrastado con el apellido completo verificado. No modifica la identidad KYC.",
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await MapearRespuestaAsync(persona, usuario, ct);
    }

    private Task<List<KycVerificacion>> KycActualesAsync(long idUsuario, CancellationToken ct) =>
        _db.KycVerificaciones.AsNoTracking().Where(k => k.IdUsuario == idUsuario && k.EsActual)
            .Take(2).ToListAsync(ct);

    public async Task<MiPerfilResponseDto> ActualizarDomicilioDaneAsync(
        long idUsuario, long idPersona, GuardarDomicilioDaneRequest request,
        CancellationToken ct = default)
    {
        var direccion = (request.Direccion ?? string.Empty).Trim();
        if (direccion.Length is < 1 or > 300)
            throw new InvalidOperationException("Escribe una dirección de hasta 300 caracteres.");
        if (request.IdCiudadDane <= 0 || request.IdDepartamentoDane <= 0)
            throw new InvalidOperationException("Selecciona el departamento y la ciudad de la lista.");

        await using var transaccion = _db.Database.IsSqlServer()
            ? await _db.Database.BeginTransactionAsync(ct) : null;
        if (transaccion is not null)
            await ContactoVerificacionService.BloquearPersonaAsync(_db, idPersona);

        var persona = await _db.Personas.SingleOrDefaultAsync(p => p.IdPersona == idPersona, ct)
            ?? throw new KeyNotFoundException("No se encontró la persona asociada a este usuario.");
        var usuario = await _db.Usuarios.SingleOrDefaultAsync(
            u => u.IdUsuario == idUsuario && u.IdPersona == idPersona, ct)
            ?? throw new KeyNotFoundException("No se encontró el usuario asociado a esta persona.");
        if (transaccion is not null)
        {
            await _db.Entry(persona).ReloadAsync(ct);
            await _db.Entry(usuario).ReloadAsync(ct);
        }

        var lugar = await DomiciliosColombiaActivos()
            .SingleOrDefaultAsync(x => x.IdCiudad == request.IdCiudadDane &&
                                      x.IdDepartamento == request.IdDepartamentoDane, ct)
            ?? throw new InvalidOperationException("Selecciona una ciudad activa que pertenezca al departamento indicado en Colombia.");

        var antes = new { persona.Direccion, persona.Ciudad, persona.Departamento, persona.IdCiudadDane };
        if (persona.Direccion != direccion || persona.Ciudad != lugar.Ciudad ||
            persona.Departamento != lugar.Departamento || persona.IdCiudadDane != lugar.IdCiudad)
        {
            persona.Direccion = direccion;
            persona.Ciudad = lugar.Ciudad;
            persona.Departamento = lugar.Departamento;
            persona.IdCiudadDane = lugar.IdCiudad;
            persona.FechaActualizacion = DateTime.UtcNow;
            _db.Auditorias.Add(new Auditoria
            {
                IdUsuario = idUsuario, IdPersona = idPersona,
                Modulo = "PERFIL", Accion = "ACTUALIZAR_DOMICILIO_DANE", Entidad = "Persona",
                IdEntidad = idPersona.ToString(), ValorAnterior = JsonSerializer.Serialize(antes),
                ValorNuevo = JsonSerializer.Serialize(new
                {
                    persona.Direccion, persona.Ciudad, persona.Departamento, persona.IdCiudadDane,
                    IdDepartamentoDane = lugar.IdDepartamento
                }),
                Resultado = "EXITOSO", Observacion = "Domicilio elegido del catálogo geográfico activo de Colombia.",
                FechaEvento = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        if (transaccion is not null) await transaccion.CommitAsync(ct);
        return await MapearRespuestaAsync(persona, usuario, ct);
    }

    // La activación debe volver a validar el catálogo: tener una FK o nombres
    // históricos no acredita por sí solo un domicilio completo y vigente.
    public async Task<bool> DomicilioDaneCompletoAsync(long idPersona, CancellationToken ct = default)
    {
        return await (from p in _db.Personas.AsNoTracking()
                      join lugar in DomiciliosColombiaActivos() on p.IdCiudadDane equals (long?)lugar.IdCiudad
                      where p.IdPersona == idPersona && p.Direccion != null &&
                            p.Direccion.Trim().Length > 0 && p.Direccion.Length <= 300 &&
                            p.Ciudad == lugar.Ciudad && p.Departamento == lugar.Departamento
                      select p.IdPersona).AnyAsync(ct);
    }

    private sealed class DomicilioCatalogo
    {
        public long IdCiudad { get; init; }
        public long IdDepartamento { get; init; }
        public string Ciudad { get; init; } = string.Empty;
        public string Departamento { get; init; } = string.Empty;
    }

    private IQueryable<DomicilioCatalogo> DomiciliosColombiaActivos() =>
        from c in _db.CatalogoCiudades.AsNoTracking()
        join d in _db.CatalogoDepartamentos.AsNoTracking() on c.IdDepartamento equals d.IdDepartamento
        join p in _db.CatalogoPaises.AsNoTracking() on d.IdPais equals p.IdPais
        where c.Estado == "ACTIVO" && d.Estado == "ACTIVO" && p.Estado == "ACTIVO" &&
              (p.Codigo == "CO" || p.Codigo == "COL")
        select new DomicilioCatalogo
        {
            IdCiudad = c.IdCiudad, IdDepartamento = d.IdDepartamento,
            Ciudad = c.Nombre, Departamento = d.Nombre
        };

    private async Task RevocarContactosAsync(long idPersona, PerfilActualizacionPlanner.Plan plan)
    {
        var usuarios = _db.Usuarios.Where(u => u.IdPersona == idPersona);
        var ids = usuarios.Select(u => u.IdUsuario);
        var codigos = _db.ContactoCodigosVerificacion.Where(c => ids.Contains(c.IdUsuario));
        var ahora = DateTime.UtcNow;

        // El dato pertenece a la persona y puede estar asociado a más de un usuario.
        if (plan.ResetearCelularVerificado)
        {
            await usuarios.Where(u => u.CelularVerificado)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.CelularVerificado, false));
            await ContactoVerificacionService.InvalidarPendientesAsync(codigos.Where(c => c.Canal == CanalContacto.WhatsApp),
                ContactoVerificacionService.MotivoContactoCambio, ahora);
        }
        if (plan.ResetearEmailVerificado)
        {
            await usuarios.Where(u => u.EmailVerificado)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailVerificado, false));
            await ContactoVerificacionService.InvalidarPendientesAsync(codigos.Where(c => c.Canal == CanalContacto.Email),
                ContactoVerificacionService.MotivoContactoCambio, ahora);
        }
    }

    private async Task<MiPerfilResponseDto> MapearRespuestaAsync(
        Persona persona, Usuario usuario, CancellationToken ct = default)
    {
        var respuesta = MapearRespuesta(persona, usuario);
        respuesta.PuedeCompletarPrimerApellido = string.IsNullOrWhiteSpace(persona.PrimerApellido)
            && PrimerApellidoDeclaradoReglas.TieneIdentidadVigente(usuario, persona,
                await KycActualesAsync(usuario.IdUsuario, ct));
        respuesta.IdCiudadDane = persona.IdCiudadDane;
        if (persona.IdCiudadDane.HasValue)
            respuesta.IdDepartamentoDane = await _db.CatalogoCiudades.AsNoTracking()
                .Where(c => c.IdCiudad == persona.IdCiudadDane.Value)
                .Select(c => (long?)c.IdDepartamento).SingleOrDefaultAsync(ct);
        return respuesta;
    }

    private static MiPerfilResponseDto MapearRespuesta(Persona persona, Usuario usuario) => new()
    {
        Usuario             = usuario.NombreUsuario,
        PrimerNombre        = persona.PrimerNombre,
        SegundoNombre       = persona.SegundoNombre,
        PrimerApellido      = persona.PrimerApellido,
        SegundoApellido     = persona.SegundoApellido,
        TipoDocumento       = persona.TipoDocumento,
        NumeroDocumento     = persona.NumeroDocumento,
        FechaNacimiento     = persona.FechaNacimiento,
        Celular             = persona.Celular,
        Email               = persona.Email,
        Direccion           = persona.Direccion,
        Ciudad              = persona.Ciudad,
        Departamento        = persona.Departamento,
        Pais                = persona.Pais,
        IdentidadVerificada = persona.IdentidadVerificada,
        NombreVerificado    = persona.IdentidadVerificada ? NombreVerificado(persona) : null,
        TipoDocumentoVerificado = persona.IdentidadVerificada ? EtiquetaTipoDocumentoVeriff(persona.TipoDocumentoVeriffRaw) : null,
        NumeroDocumentoVerificadoMasked = persona.IdentidadVerificada ? EnmascararDocumento(persona.NumeroDocumentoVerificado) : null,
        EstadoKycActual     = usuario.EstadoKycActual,
        EmailVerificado     = usuario.EmailVerificado,
        CelularVerificado   = usuario.CelularVerificado,
    };

    private static string? NombreVerificado(Persona persona)
    {
        var partes = new[] { persona.NombreVerificadoCompleto, persona.ApellidoVerificadoCompleto }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToArray();
        return partes.Length > 0 ? string.Join(' ', partes) : null;
    }

    internal static string? EtiquetaTipoDocumentoVeriff(string? raw) =>
        (raw ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            ""                 => null,
            "ID_CARD"          => "Documento de identidad",
            "PASSPORT"         => "Pasaporte",
            "DRIVERS_LICENSE"  => "Licencia de conducción",
            "RESIDENCE_PERMIT" => "Permiso de residencia",
            _                  => "Otro documento",
        };

    internal static string? EnmascararDocumento(string? numero)
    {
        var n = (numero ?? string.Empty).Trim();
        if (n.Length == 0) return null;
        return n.Length <= 4 ? new string('*', n.Length) : "***" + n[^4..];
    }
}
