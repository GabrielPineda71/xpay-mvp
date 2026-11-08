using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

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
        // P0-B3 — todo fallo de autenticación lanza CredencialesInvalidasException
        // con el mismo mensaje público. El orden y los efectos no cambian: solo
        // un usuario ACTIVO con clave incorrecta acumula IntentosFallidos; los
        // caminos sin efectos ejecutan un BCrypt de igualación de tiempo.
        if (request is null || string.IsNullOrWhiteSpace(request.Usuario) || request.Password is null)
        {
            VerificarSinEfecto(request?.Password, HashIgualacion.Value);
            throw new CredencialesInvalidasException();
        }

        var nombreUsuario = request.Usuario.Trim().ToLower();
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario);
        if (usuario == null)
        {
            VerificarSinEfecto(request.Password, HashIgualacion.Value);
            throw new CredencialesInvalidasException();
        }
        if (usuario.Estado != "ACTIVO")
        {
            // Sin contar intento ni tocar la BD (semántica previa); el resultado se descarta.
            VerificarSinEfecto(request.Password, usuario.PasswordHash);
            throw new CredencialesInvalidasException();
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, usuario.PasswordHash))
        {
            // P0-B BACKLOG: IntentosFallidos += 1 es read-modify-write en memoria;
            // dos fallos concurrentes pueden perder un incremento del contador.
            usuario.IntentosFallidos += 1;
            if (usuario.IntentosFallidos >= 5)
            {
                // P0-B — el usuario llegó ACTIVO (filtro de arriba): esta petición
                // causa la transición a BLOQUEADO y revoca sus JWT en la misma
                // transacción. Los fallos 1–4 y los intentos sobre BLOQUEADO no
                // pasan por aquí.
                usuario.Estado        = "BLOQUEADO";
                usuario.FechaBloqueo  = DateTime.UtcNow;
                // P0-B2 — fuente única compartida con la recuperación de contraseña.
                usuario.MotivoBloqueo = BloqueoAutomaticoUsuario.Motivo;
                await GuardarYRevocarAsync(usuario.IdUsuario, releerVersion: false);
            }
            else
            {
                await _db.SaveChangesAsync();
            }
            throw new CredencialesInvalidasException();
        }

        usuario.IntentosFallidos = 0;
        usuario.UltimoIngreso    = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var roles = await (from ur in _db.UsuarioRoles
                           join r in _db.Roles on ur.IdRol equals r.IdRol
                           where ur.IdUsuario == usuario.IdUsuario && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
                           select r.Codigo).ToListAsync();

        var token = GenerarJwt(usuario.IdUsuario, usuario.IdPersona, usuario.NombreUsuario, roles, usuario.RequiereCambioClave, usuario.VersionSeguridad);

        return new LoginResponse
        {
            IdUsuario = usuario.IdUsuario,
            IdPersona = usuario.IdPersona,
            Usuario   = usuario.NombreUsuario,
            Estado    = usuario.Estado,
            Roles     = roles,
            Token     = token,
            RequiereCambioClave = usuario.RequiereCambioClave
        };
    }

    // Fase USUARIOS-ADMIN-5: cambio obligatorio de contraseña tras un
    // restablecimiento administrativo. Opera siempre sobre el usuario del
    // propio token (idUsuario viene del claim, nunca del body) — no recibe id
    // de otro usuario, no es una operación administrativa.
    public async Task<LoginResponse> CambiarClaveObligatoriaAsync(long idUsuario, CambiarClaveObligatoriaRequest request)
    {
        var usuario = await _db.Usuarios.FindAsync(idUsuario)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        if (request.ClaveNueva != request.ConfirmacionClaveNueva)
            throw new InvalidOperationException("La contraseña nueva y su confirmación no coinciden.");

        if (!BCrypt.Net.BCrypt.Verify(request.ClaveActual, usuario.PasswordHash))
            throw new InvalidOperationException("La contraseña actual no coincide.");

        // Prohibición explícita de reutilizar la clave temporal/actual.
        if (request.ClaveNueva == request.ClaveActual)
            throw new InvalidOperationException("La contraseña nueva no puede ser igual a la contraseña actual.");

        ValidarPoliticaClave(request.ClaveNueva, usuario.NombreUsuario);

        usuario.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(request.ClaveNueva);
        usuario.RequiereCambioClave = false;
        usuario.FechaActualizacion = DateTime.UtcNow;
        // P0-B — cambio de clave + revocación atómica; la versión fresca se relee
        // dentro de la misma transacción y el JWT se emite solo tras el commit.
        var versionSeguridad = (await GuardarYRevocarAsync(usuario.IdUsuario, releerVersion: true))!.Value;

        var roles = await (from ur in _db.UsuarioRoles
                           join r in _db.Roles on ur.IdRol equals r.IdRol
                           where ur.IdUsuario == usuario.IdUsuario && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
                           select r.Codigo).ToListAsync();

        // P0-B — versión releída de la BD tras el incremento (la entidad rastreada
        // no ve el ExecuteUpdate): el token nuevo es el único vigente.
        var token = GenerarJwt(usuario.IdUsuario, usuario.IdPersona, usuario.NombreUsuario, roles, usuario.RequiereCambioClave, versionSeguridad);

        return new LoginResponse
        {
            IdUsuario = usuario.IdUsuario,
            IdPersona = usuario.IdPersona,
            Usuario   = usuario.NombreUsuario,
            Estado    = usuario.Estado,
            Roles     = roles,
            Token     = token,
            RequiereCambioClave = usuario.RequiereCambioClave
        };
    }

    // XPAY-400 — cambio VOLUNTARIO de contraseña para un usuario YA
    // autenticado con clave vigente (RequiereCambioClave=false). Deliberadamente
    // NO modifica CambiarClaveObligatoriaAsync — reutiliza únicamente lo seguro
    // de reutilizar: verificación BCrypt de la clave actual y
    // ValidarPoliticaClave (misma política exacta, cero duplicación).
    //
    // Por qué esta clase NO vuelve a comprobar RequiereCambioClave aquí: el
    // endpoint HTTP que llama a este método usa [Authorize] simple (ver
    // AuthController.CambiarClave), que hereda la DefaultPolicy configurada en
    // Program.cs — esa policy ya incluye ClaveVigenteRequirement, que consulta
    // usuarios.requiere_cambio_clave EN VIVO contra la base de datos y devuelve
    // 403 ANTES de que la petición llegue aquí si es true. Es el mismo
    // mecanismo que protege a todo el resto de la aplicación (nunca duplicado
    // manualmente en cada servicio) — replicar la comprobación aquí sería
    // lógica redundante, no una capa de seguridad adicional real.
    //
    // No emite un JWT nuevo (a diferencia del flujo obligatorio). P0-B: el
    // cambio incrementa version_seguridad en la misma transacción que la clave
    // y la auditoría, así que TODOS los JWT emitidos antes (incluido el de esta
    // sesión) dejan de ser vigentes en la siguiente petición.
    public async Task CambiarClaveVoluntariaAsync(long idUsuario, CambiarClaveRequest request)
    {
        var usuario = await _db.Usuarios.FindAsync(idUsuario)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        if (!BCrypt.Net.BCrypt.Verify(request.ClaveActual, usuario.PasswordHash))
            throw new InvalidOperationException("La contraseña actual no coincide.");

        if (request.ClaveNueva == request.ClaveActual)
            throw new InvalidOperationException("La contraseña nueva no puede ser igual a la contraseña actual.");

        ValidarPoliticaClave(request.ClaveNueva, usuario.NombreUsuario);

        usuario.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(request.ClaveNueva);
        usuario.FechaActualizacion = DateTime.UtcNow;

        // Auditoría persistente (tabla `auditoria` ya existente). Deliberadamente
        // ValorAnterior/ValorNuevo quedan NULL — nunca deben contener password,
        // hash actual ni hash nuevo (ver XPAY-400 PASO 8). Observacion neutra,
        // sin ningún dato sensible.
        _db.Auditorias.Add(new Auditoria
        {
            IdUsuario   = idUsuario,
            Modulo      = "SEGURIDAD",
            Accion      = "CAMBIAR_CLAVE",
            Entidad     = "Usuario",
            IdEntidad   = idUsuario.ToString(),
            Resultado   = "EXITOSO",
            Observacion = "Contraseña actualizada por el usuario.",
            FechaEvento = DateTime.UtcNow,
        });

        await GuardarYRevocarAsync(idUsuario, releerVersion: false);
    }

    // P0-B — guarda los cambios rastreados e incrementa version_seguridad de
    // forma atómica en UNA transacción. Si releerVersion, lee la versión
    // resultante dentro de esa misma transacción (AsNoTracking: la entidad
    // rastreada no ve el ExecuteUpdate). Ante cualquier fallo, el dispose de
    // la transacción no confirmada revierte todo y se limpia el ChangeTracker;
    // la excepción original se propaga sin envolver.
    private async Task<int?> GuardarYRevocarAsync(long idUsuario, bool releerVersion)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.SaveChangesAsync();
            await VersionSeguridadUsuario.IncrementarAsync(_db, idUsuario);
            int? version = releerVersion
                ? await _db.Usuarios.AsNoTracking()
                    .Where(u => u.IdUsuario == idUsuario)
                    .Select(u => u.VersionSeguridad)
                    .SingleAsync()
                : null;
            await tx.CommitAsync();
            return version;
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // Fase USUARIOS-ADMIN-5: política mínima aplicada exclusivamente al cambio
    // obligatorio tras un restablecimiento — no se aplica retroactivamente a
    // usuarios existentes ni a registro-final (deuda técnica documentada en el
    // precheck, fuera de este alcance).
    internal static void ValidarPoliticaClave(string clave, string nombreUsuario)
    {
        if (string.IsNullOrEmpty(clave) || clave.Length < 8 || clave.Length > 128)
            throw new InvalidOperationException("La contraseña debe tener entre 8 y 128 caracteres.");
        if (!clave.Any(char.IsUpper))
            throw new InvalidOperationException("La contraseña debe incluir al menos una letra mayúscula.");
        if (!clave.Any(char.IsLower))
            throw new InvalidOperationException("La contraseña debe incluir al menos una letra minúscula.");
        if (!clave.Any(char.IsDigit))
            throw new InvalidOperationException("La contraseña debe incluir al menos un dígito.");
        if (!clave.Any(c => "!@#$%^&*()-_=+".Contains(c)))
            throw new InvalidOperationException("La contraseña debe incluir al menos un carácter especial (!@#$%^&*()-_=+).");
        if (clave.Contains(nombreUsuario, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La contraseña no puede contener el nombre de usuario.");
    }

    // P0-B3 — hash BCrypt válido de un valor aleatorio no recuperable, con el
    // coste por defecto de BCrypt.Net (el mismo de HashPassword en altas y
    // cambios de clave). Se calcula una sola vez, en el primer uso.
    private static readonly Lazy<string> HashIgualacion =
        new(() => BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    // Igualación de tiempo sin efectos: el resultado se descarta y un hash mal
    // formado (o una clave nula) nunca produce una respuesta distinta.
    private static void VerificarSinEfecto(string? clave, string hash)
    {
        try { _ = BCrypt.Net.BCrypt.Verify(clave ?? string.Empty, hash); }
        catch { /* igualación best-effort: sin efecto observable */ }
    }

    // P0-B — versionSeguridad es usuarios.version_seguridad y viaja en el
    // claim "vs" (decimal invariante). Negativa = fallo cerrado.
    private string GenerarJwt(long idUsuario, long idPersona, string usuario, List<string> roles, bool requiereCambioClave, int versionSeguridad)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(versionSeguridad);

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("idUsuario", idUsuario.ToString()),
            new("idPersona", idPersona.ToString()),
            new("usuario",   usuario),
            new(JwtRegisteredClaimNames.Sub, idUsuario.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // Fase USUARIOS-ADMIN-5: informativo para el frontend (redirección
            // de UX) — el enforcement real vive en ClaveVigenteAuthorizationHandler,
            // que consulta la BD en vivo y nunca confía en este claim.
            new("requiereCambioClave", requiereCambioClave.ToString().ToLowerInvariant()),
            // P0-B — revocación: debe coincidir con usuarios.version_seguridad.
            new(VersionSeguridadUsuario.Claim, versionSeguridad.ToString(CultureInfo.InvariantCulture))
        };

        foreach (var rol in roles)
            claims.Add(new Claim(ClaimTypes.Role, rol));

        var expHours = _config.GetValue("Jwt:ExpirationHours", defaultValue: 2);
        if (expHours <= 0) expHours = 2;

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
