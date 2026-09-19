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
// CelularVerificado a true (no existe mecanismo real de verificación aún),
// y siempre los deja en false cuando el dato declarado correspondiente
// cambia — ver XPAY-399 PASO 4.
//
// El cómputo de "qué cambia" vive en PerfilActualizacionPlanner (puro, sin
// XpayDbContext) — esta clase solo orquesta: cargar entidades, aplicar el
// plan, persistir entidad+auditoría en un único SaveChangesAsync (mismo
// patrón ya usado por UsuarioAdminService.RegistrarAuditoria: ambas
// entidades comparten el ChangeTracker de EF Core, por lo que una sola
// llamada las confirma de forma atómica — sin transacción explícita
// adicional, consistente con "no infraestructura compleja innecesaria").
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
        var persona = await _db.Personas.FirstOrDefaultAsync(p => p.IdPersona == idPersona)
            ?? throw new KeyNotFoundException("No se encontró la persona asociada a este usuario.");
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.IdPersona == idPersona)
            ?? throw new KeyNotFoundException("No se encontró el usuario asociado a esta persona.");

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
        }
        // Sin cambios reales -> no se escribe nada (ni Persona ni auditoría):
        // evita una fila de auditoría engañosa que sugiera una modificación
        // que nunca ocurrió (ver XPAY-399 PASO 10, caso de test explícito).

        return MapearRespuesta(persona, usuario);
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
        EstadoKycActual     = usuario.EstadoKycActual,
        EmailVerificado     = usuario.EmailVerificado,
        CelularVerificado   = usuario.CelularVerificado,
    };
}
