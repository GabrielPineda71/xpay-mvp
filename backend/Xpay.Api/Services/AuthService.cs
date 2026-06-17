using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xpay.Api.Data;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

public class AuthService
{
    private readonly XpayDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(XpayDbContext db, IConfiguration config)
    {
        _db    = db;
        _config = config;
    }

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
                usuario.Estado        = "BLOQUEADO";
                usuario.FechaBloqueo  = DateTime.UtcNow;
                usuario.MotivoBloqueo = "Bloqueo automático por intentos fallidos.";
            }
            await _db.SaveChangesAsync();
            throw new InvalidOperationException("Usuario o contraseña inválidos.");
        }

        usuario.IntentosFallidos = 0;
        usuario.UltimoIngreso    = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var roles = await (from ur in _db.UsuarioRoles
                           join r in _db.Roles on ur.IdRol equals r.IdRol
                           where ur.IdUsuario == usuario.IdUsuario && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
                           select r.Codigo).ToListAsync();

        var token = GenerarJwt(usuario.IdUsuario, usuario.IdPersona, usuario.NombreUsuario, roles);

        return new LoginResponse
        {
            IdUsuario = usuario.IdUsuario,
            IdPersona = usuario.IdPersona,
            Usuario   = usuario.NombreUsuario,
            Estado    = usuario.Estado,
            Roles     = roles,
            Token     = token
        };
    }

    private string GenerarJwt(long idUsuario, long idPersona, string usuario, List<string> roles)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("idUsuario", idUsuario.ToString()),
            new("idPersona", idPersona.ToString()),
            new("usuario",   usuario),
            new(JwtRegisteredClaimNames.Sub, idUsuario.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var rol in roles)
            claims.Add(new Claim(ClaimTypes.Role, rol));

        var expHours = int.TryParse(_config["Jwt:ExpirationHours"], out var h) ? h : 8;

        var jwt = new JwtSecurityToken(
            issuer:            _config["Jwt:Issuer"],
            audience:          _config["Jwt:Audience"],
            claims:            claims,
            expires:           DateTime.UtcNow.AddHours(expHours),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
