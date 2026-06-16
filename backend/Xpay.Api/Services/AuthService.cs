using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

public class AuthService
{
    private readonly XpayDbContext _db;
    public AuthService(XpayDbContext db) => _db = db;

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var nombreUsuario = request.Usuario.Trim().ToLower();
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario);
        if (usuario == null) throw new InvalidOperationException("Usuario o contraseña inválidos.");
        if (usuario.Estado != "ACTIVO") throw new InvalidOperationException("El usuario no está activo.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, usuario.PasswordHash))
        {
            usuario.IntentosFallidos += 1;
            if (usuario.IntentosFallidos >= 5)
            {
                usuario.Estado = "BLOQUEADO";
                usuario.FechaBloqueo = DateTime.UtcNow;
                usuario.MotivoBloqueo = "Bloqueo automático por intentos fallidos.";
            }
            await _db.SaveChangesAsync();
            throw new InvalidOperationException("Usuario o contraseña inválidos.");
        }

        usuario.IntentosFallidos = 0;
        usuario.UltimoIngreso = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var roles = await (from ur in _db.UsuarioRoles
                           join r in _db.Roles on ur.IdRol equals r.IdRol
                           where ur.IdUsuario == usuario.IdUsuario && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
                           select r.Codigo).ToListAsync();

        return new LoginResponse { IdUsuario = usuario.IdUsuario, IdPersona = usuario.IdPersona, Usuario = usuario.NombreUsuario, Estado = usuario.Estado, Roles = roles };
    }
}
