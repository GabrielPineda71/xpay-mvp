using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// XPAY-399 (Perfil Fase 2A) — lectura y edición self-service de datos
// personales DECLARADOS. Frontera deliberada, decisión de producto
// (XPAY-398/399, NO reinterpretable por este servicio):
//
//   EDITABLE aquí:      Celular, Email, Direccion, Ciudad, Departamento.
//   NUNCA EDITABLE aquí: PrimerNombre/SegundoNombre/PrimerApellido/
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
    private readonly XpayDbContext _db;
    public PerfilService(XpayDbContext db) => _db = db;

    public async Task<MiPerfilResponseDto> ObtenerAsync(long idPersona)
    {
        var persona = await _db.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.IdPersona == idPersona)
            ?? throw new KeyNotFoundException("No se encontró la persona asociada a este usuario.");
        var usuario = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdPersona == idPersona)
            ?? throw new KeyNotFoundException("No se encontró el usuario asociado a esta persona.");

        return MapearRespuesta(persona, usuario);
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

        return MapearRespuesta(persona, usuario);
    }

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
