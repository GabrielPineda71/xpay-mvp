using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Commit 3 — registro-inicial (Opción B: login inmediato, perfil/identidad
// se completan después). Crea únicamente Persona-cascarón + Usuario + rol
// mínimo USUARIO_FINAL — sin documento, sin nombre, sin Wallet, sin JWT.
// Ver database/032_persona_identidad_verificada.sql y
// database/033_persona_nombre_nullable.sql (nullability ya aplicada en QA).
public class RegistroInicialService(XpayDbContext db)
{
    private const long IdUnidadNegocio = 1;

    public async Task<RegistroInicialResultDto> RegistrarAsync(RegistroInicialRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(request.Usuario))
                throw new InvalidOperationException("El nombre de usuario es obligatorio.");
            var nombreUsuario = request.Usuario.Trim().ToLower();
            if (nombreUsuario.Length > 100)
                throw new InvalidOperationException("El nombre de usuario no puede superar 100 caracteres.");

            AuthService.ValidarPoliticaClave(request.Password, nombreUsuario);

            if (string.IsNullOrWhiteSpace(request.Celular))
                throw new InvalidOperationException("El celular es obligatorio.");
            var celular = request.Celular.Trim();
            if (celular.Length > 30)
                throw new InvalidOperationException("El celular no puede superar 30 caracteres.");

            if (await db.Usuarios.AnyAsync(u => u.NombreUsuario == nombreUsuario))
                throw new InvalidOperationException("El nombre de usuario ya existe.");

            var rol = await db.Roles.FirstOrDefaultAsync(r => r.Codigo == "USUARIO_FINAL" && r.Estado == "ACTIVO")
                ?? throw new InvalidOperationException("No existe el rol USUARIO_FINAL.");

            var persona = new Persona
            {
                IdUnidadNegocio = IdUnidadNegocio,
                Celular         = celular,
                Pais            = "Colombia",
                Estado          = "ACTIVA",
                FechaCreacion   = DateTime.UtcNow
            };
            db.Personas.Add(persona);
            await db.SaveChangesAsync();

            var usuario = new Usuario
            {
                IdPersona     = persona.IdPersona,
                NombreUsuario = nombreUsuario,
                PasswordHash  = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Estado        = "ACTIVO",
                FechaCreacion = DateTime.UtcNow
            };
            db.Usuarios.Add(usuario);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (EsViolacionUsuarioDuplicado(ex))
            {
                throw new InvalidOperationException("El nombre de usuario ya existe.");
            }

            db.UsuarioRoles.Add(new UsuarioRol
            {
                IdUsuario       = usuario.IdUsuario,
                IdRol           = rol.IdRol,
                Estado          = "ACTIVO",
                FechaAsignacion = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            db.Auditorias.Add(new Auditoria
            {
                IdUsuario   = usuario.IdUsuario,
                IdPersona   = persona.IdPersona,
                Modulo      = "USUARIOS",
                Accion      = "REGISTRO_INICIAL",
                Entidad     = "usuarios",
                IdEntidad   = usuario.IdUsuario.ToString(),
                Resultado   = "EXITOSO",
                Observacion = "Registro inicial (usuario + clave + celular) — perfil pendiente de completar.",
                FechaEvento = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
            return new RegistroInicialResultDto(usuario.IdUsuario, persona.IdPersona);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // Solo traduce la violación de unicidad real de usuarios.usuario
    // (IX_usuarios_usuario) — cualquier otra restricción única que
    // coincida por casualidad con 2601/2627 se conserva como error
    // inesperado, sin ocultarla bajo "usuario duplicado".
    private static bool EsViolacionUsuarioDuplicado(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx
        && (sqlEx.Number == 2601 || sqlEx.Number == 2627)
        && sqlEx.Message.Contains("IX_usuarios_usuario", StringComparison.OrdinalIgnoreCase);
}
