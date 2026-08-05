using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Fase USUARIOS-ADMIN-2: solo lectura (listado + detalle).
// Fase USUARIOS-ADMIN-3: activar/inactivar/desbloquear.
// Fase USUARIOS-ADMIN-4: asignar/revocar roles (lista blanca). Sigue sin
// crear usuarios ni editar datos.
// Fase USUARIOS-ADMIN-5: restablecimiento administrativo de contraseña.
public class UsuarioAdminService
{
    private const string RolAdminXpay    = "ADMIN_XPAY";
    private const string RolSuperusuario = "SUPERUSUARIO";

    // Fase USUARIOS-ADMIN-5: alfabeto de la clave temporal — sin caracteres
    // ambiguos (0/O, 1/l/I) para evitar errores de transcripción manual al
    // comunicar la clave al usuario.
    private const string AlfabetoMayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string AlfabetoMinusculas = "abcdefghijkmnopqrstuvwxyz";
    private const string AlfabetoDigitos    = "23456789";
    private const string AlfabetoEspeciales = "!@#$%^&*()-_=+";
    private const int    LongitudClaveTemporal = 14;

    // Fase USUARIOS-ADMIN-4: lista blanca exacta de roles asignables desde
    // este módulo — nunca una lista negra. ADMIN_XPAY, OPERADOR_XPAY,
    // TESORERIA_XPAY y COMERCIO son roles técnicos heredados, deliberadamente
    // fuera de esta lista (decisión de producto, USUARIOS-ADMIN-4).
    private static readonly string[] RolesAsignables =
        { RolSuperusuario, "CARTERA_XPAY", "COMERCIAL_XPAY", "GERENTE_XPAY" };

    private readonly XpayDbContext _db;
    public UsuarioAdminService(XpayDbContext db) => _db = db;

    public async Task<object> ListarUsuariosAsync(
        string? texto, string? estado, string? rol, bool soloBloqueados,
        int page, int pageSize)
    {
        if (page < 1)       page     = 1;
        if (pageSize < 1)   pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query =
            from u in _db.Usuarios
            join p in _db.Personas on u.IdPersona equals p.IdPersona
            select new { u, p };

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            query = query.Where(x =>
                x.u.NombreUsuario.Contains(t) ||
                x.p.NumeroDocumento.Contains(t) ||
                x.p.PrimerNombre.Contains(t) ||
                x.p.PrimerApellido.Contains(t) ||
                (x.p.Email != null && x.p.Email.Contains(t)));
        }

        if (soloBloqueados)
        {
            query = query.Where(x => x.u.Estado == "BLOQUEADO");
        }
        else if (!string.IsNullOrWhiteSpace(estado))
        {
            query = query.Where(x => x.u.Estado == estado);
        }

        if (!string.IsNullOrWhiteSpace(rol))
        {
            var idRol = await _db.Roles
                .Where(r => r.Codigo == rol)
                .Select(r => (long?)r.IdRol)
                .FirstOrDefaultAsync();

            // Rol inexistente en el catálogo → filtro sin coincidencias posibles,
            // nunca se trata como "sin filtro" (evitaría exponer todo el listado).
            query = idRol.HasValue
                ? query.Where(x => _db.UsuarioRoles.Any(ur => ur.IdUsuario == x.u.IdUsuario && ur.IdRol == idRol.Value && ur.Estado == "ACTIVO"))
                : query.Where(_ => false);
        }

        var total = await query.CountAsync();

        var pagina = await query
            .OrderByDescending(x => x.u.IdUsuario)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var idsUsuariosPagina = pagina.Select(x => x.u.IdUsuario).ToList();
        var rolesPorUsuario = await (
            from ur in _db.UsuarioRoles
            join r in _db.Roles on ur.IdRol equals r.IdRol
            where idsUsuariosPagina.Contains(ur.IdUsuario) && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
            select new { ur.IdUsuario, r.Codigo }
        ).ToListAsync();

        var rolesMap = rolesPorUsuario
            .GroupBy(x => x.IdUsuario)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Codigo).ToList());

        var items = pagina.Select(x => new UsuarioAdminListItemDto
        {
            IdUsuario           = x.u.IdUsuario,
            Usuario             = x.u.NombreUsuario,
            NombreCompleto      = NombreCompleto(x.p.PrimerNombre, x.p.SegundoNombre, x.p.PrimerApellido, x.p.SegundoApellido),
            TipoDocumento       = x.p.TipoDocumento,
            NumeroDocumento     = x.p.NumeroDocumento,
            Email               = x.p.Email,
            Celular             = x.p.Celular,
            Estado              = x.u.Estado,
            IntentosFallidos    = x.u.IntentosFallidos,
            FechaBloqueo        = x.u.FechaBloqueo,
            MotivoBloqueo       = x.u.MotivoBloqueo,
            UltimoIngreso       = x.u.UltimoIngreso,
            RequiereCambioClave = x.u.RequiereCambioClave,
            Roles               = rolesMap.TryGetValue(x.u.IdUsuario, out var r) ? r : new List<string>(),
            FechaCreacion       = x.u.FechaCreacion
        }).ToList();

        return new { items, total, page, pageSize };
    }

    public async Task<UsuarioAdminDetalleDto?> ObtenerDetalleAsync(long idUsuario)
    {
        var registro = await (
            from u in _db.Usuarios
            join p in _db.Personas on u.IdPersona equals p.IdPersona
            where u.IdUsuario == idUsuario
            select new { u, p }
        ).FirstOrDefaultAsync();

        if (registro == null) return null;

        var roles = await (
            from ur in _db.UsuarioRoles
            join r in _db.Roles on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO" && r.Estado == "ACTIVO"
            select new UsuarioAdminRolDto
            {
                IdRol           = r.IdRol,
                Codigo          = r.Codigo,
                Nombre          = r.Nombre,
                FechaAsignacion = ur.FechaAsignacion
            }
        ).ToListAsync();

        var usuarioEntidad = registro.u;
        var personaEntidad = registro.p;
        return new UsuarioAdminDetalleDto
        {
            IdUsuario           = usuarioEntidad.IdUsuario,
            IdPersona           = personaEntidad.IdPersona,
            Usuario             = usuarioEntidad.NombreUsuario,
            NombreCompleto      = NombreCompleto(personaEntidad.PrimerNombre, personaEntidad.SegundoNombre, personaEntidad.PrimerApellido, personaEntidad.SegundoApellido),
            TipoDocumento       = personaEntidad.TipoDocumento,
            NumeroDocumento     = personaEntidad.NumeroDocumento,
            Email               = personaEntidad.Email,
            Celular             = personaEntidad.Celular,
            Direccion           = personaEntidad.Direccion,
            Ciudad              = personaEntidad.Ciudad,
            Departamento        = personaEntidad.Departamento,
            Pais                = personaEntidad.Pais,
            Estado              = usuarioEntidad.Estado,
            EmailVerificado     = usuarioEntidad.EmailVerificado,
            CelularVerificado   = usuarioEntidad.CelularVerificado,
            IntentosFallidos    = usuarioEntidad.IntentosFallidos,
            FechaBloqueo        = usuarioEntidad.FechaBloqueo,
            MotivoBloqueo       = usuarioEntidad.MotivoBloqueo,
            UltimoIngreso       = usuarioEntidad.UltimoIngreso,
            RequiereCambioClave = usuarioEntidad.RequiereCambioClave,
            FechaCreacion       = usuarioEntidad.FechaCreacion,
            FechaActualizacion  = usuarioEntidad.FechaActualizacion,
            Roles               = roles
        };
    }

    // Fase USUARIOS-ADMIN-3: activar — INACTIVO -> ACTIVO exclusivamente.
    // No limpia intentosFallidos/fechaBloqueo/motivoBloqueo: por diseño, este
    // endpoint nunca se ejecuta sobre un usuario BLOQUEADO (esa transición
    // usa DesbloquearAsync), así que esos campos nunca están poblados aquí.
    public async Task<UsuarioAdminDetalleDto> ActivarAsync(long idUsuario, long idAdmin)
    {
        if (idUsuario == idAdmin)
            throw new InvalidOperationException("No puedes activar tu propia cuenta.");

        var usuario = await _db.Usuarios.FindAsync(idUsuario)
            ?? throw new KeyNotFoundException($"Usuario {idUsuario} no encontrado.");

        if (usuario.Estado != "INACTIVO")
            throw new TransicionUsuarioInvalidaException(
                $"El usuario no está INACTIVO (estado actual: {usuario.Estado}). Use el endpoint correspondiente a su estado actual.");

        var estadoAnterior = usuario.Estado;
        usuario.Estado             = "ACTIVO";
        usuario.FechaActualizacion = DateTime.UtcNow;

        RegistrarAuditoria(idAdmin, "USUARIO_ACTIVAR", idUsuario, estadoAnterior, "ACTIVO",
            "Activación manual por administrador.");

        await _db.SaveChangesAsync();
        return (await ObtenerDetalleAsync(idUsuario))!;
    }

    // Fase USUARIOS-ADMIN-3: inactivar — ACTIVO -> INACTIVO exclusivamente.
    // Protege contra auto-inactivación y contra dejar el sistema sin ningún
    // administrador ADMIN_XPAY/SUPERUSUARIO activo.
    public async Task<UsuarioAdminDetalleDto> InactivarAsync(long idUsuario, long idAdmin)
    {
        if (idUsuario == idAdmin)
            throw new InvalidOperationException("No puedes inactivar tu propia cuenta.");

        var usuario = await _db.Usuarios.FindAsync(idUsuario)
            ?? throw new KeyNotFoundException($"Usuario {idUsuario} no encontrado.");

        if (usuario.Estado != "ACTIVO")
            throw new TransicionUsuarioInvalidaException(
                $"El usuario no está ACTIVO (estado actual: {usuario.Estado}). Use el endpoint correspondiente a su estado actual.");

        var tieneRolAdmin = await (
            from ur in _db.UsuarioRoles
            join r in _db.Roles on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO"
               && (r.Codigo == RolAdminXpay || r.Codigo == RolSuperusuario)
            select ur.IdUsuario
        ).AnyAsync();

        if (tieneRolAdmin)
        {
            var otrosAdminsActivos = await (
                from u in _db.Usuarios
                join ur in _db.UsuarioRoles on u.IdUsuario equals ur.IdUsuario
                join r in _db.Roles on ur.IdRol equals r.IdRol
                where u.IdUsuario != idUsuario
                   && u.Estado == "ACTIVO"
                   && ur.Estado == "ACTIVO"
                   && (r.Codigo == RolAdminXpay || r.Codigo == RolSuperusuario)
                select u.IdUsuario
            ).Distinct().CountAsync();

            if (otrosAdminsActivos == 0)
                throw new InvalidOperationException("No se puede inactivar: es el último administrador activo del sistema.");
        }

        var estadoAnterior = usuario.Estado;
        usuario.Estado             = "INACTIVO";
        usuario.FechaActualizacion = DateTime.UtcNow;

        RegistrarAuditoria(idAdmin, "USUARIO_INACTIVAR", idUsuario, estadoAnterior, "INACTIVO",
            "Inactivación manual por administrador.");

        await _db.SaveChangesAsync();
        return (await ObtenerDetalleAsync(idUsuario))!;
    }

    // Fase USUARIOS-ADMIN-3: desbloquear — BLOQUEADO -> ACTIVO exclusivamente.
    // Único camino que limpia intentosFallidos/fechaBloqueo/motivoBloqueo —
    // formaliza en API lo que antes solo era posible por SQL directo.
    public async Task<UsuarioAdminDetalleDto> DesbloquearAsync(long idUsuario, long idAdmin)
    {
        if (idUsuario == idAdmin)
            throw new InvalidOperationException("No puedes desbloquear tu propia cuenta.");

        var usuario = await _db.Usuarios.FindAsync(idUsuario)
            ?? throw new KeyNotFoundException($"Usuario {idUsuario} no encontrado.");

        if (usuario.Estado != "BLOQUEADO")
            throw new TransicionUsuarioInvalidaException(
                $"El usuario no está BLOQUEADO (estado actual: {usuario.Estado}). Use el endpoint correspondiente a su estado actual.");

        var estadoAnterior = usuario.Estado;
        usuario.Estado             = "ACTIVO";
        usuario.IntentosFallidos   = 0;
        usuario.FechaBloqueo       = null;
        usuario.MotivoBloqueo      = null;
        usuario.FechaActualizacion = DateTime.UtcNow;

        RegistrarAuditoria(idAdmin, "USUARIO_DESBLOQUEAR", idUsuario, estadoAnterior, "ACTIVO",
            "Desbloqueo manual por administrador.");

        await _db.SaveChangesAsync();
        return (await ObtenerDetalleAsync(idUsuario))!;
    }

    // Fase USUARIOS-ADMIN-5: restablece la contraseña de otro usuario con una
    // clave temporal generada por el backend (nunca elegida por el admin).
    // Nunca persiste la clave en texto plano; nunca la incluye en auditoría.
    public async Task<(UsuarioAdminDetalleDto Detalle, string ClaveTemporal)> RestablecerClaveAsync(long idUsuario, long idAdmin, string? observacionCruda)
    {
        var observacion = NormalizarObservacion(observacionCruda);

        if (idUsuario == idAdmin)
            throw new InvalidOperationException("No puedes restablecer tu propia contraseña.");

        var usuario = await _db.Usuarios.FindAsync(idUsuario)
            ?? throw new KeyNotFoundException($"Usuario {idUsuario} no encontrado.");

        if (usuario.Estado != "ACTIVO")
            throw new TransicionUsuarioInvalidaException(
                $"El usuario no está ACTIVO (estado actual: {usuario.Estado}). Actívelo o desbloquéelo antes de restablecer la contraseña.");

        if (await ActorTieneRolAsync(idUsuario, RolSuperusuario) && !await ActorTieneRolAsync(idAdmin, RolSuperusuario))
            throw new UnauthorizedAccessException("Solo un usuario con rol SUPERUSUARIO puede restablecer la contraseña de otro SUPERUSUARIO.");

        var claveTemporal = GenerarClaveTemporal();

        usuario.PasswordHash        = BCrypt.Net.BCrypt.HashPassword(claveTemporal);
        usuario.RequiereCambioClave = true;
        usuario.IntentosFallidos    = 0;
        usuario.FechaActualizacion  = DateTime.UtcNow;

        // valorAnterior/valorNuevo quedan null a propósito — nunca se audita
        // ni la clave temporal ni ningún hash.
        RegistrarAuditoria(idAdmin, "USUARIO_RESTABLECER_CLAVE", idUsuario, null, null,
            observacion ?? "Restablecimiento de contraseña por administrador.");

        await _db.SaveChangesAsync();

        var detalle = (await ObtenerDetalleAsync(idUsuario))!;
        return (detalle, claveTemporal);
    }

    // Genera una clave temporal criptográficamente segura con
    // RandomNumberGenerator (nunca System.Random). Garantiza al menos un
    // carácter de cada categoría obligatoria y mezcla el resultado con
    // Fisher-Yates para no dejar las categorías en posiciones predecibles.
    private static string GenerarClaveTemporal()
    {
        var alfabetoCompleto = AlfabetoMayusculas + AlfabetoMinusculas + AlfabetoDigitos + AlfabetoEspeciales;
        var caracteres = new char[LongitudClaveTemporal];

        caracteres[0] = AlfabetoMayusculas[RandomNumberGenerator.GetInt32(AlfabetoMayusculas.Length)];
        caracteres[1] = AlfabetoMinusculas[RandomNumberGenerator.GetInt32(AlfabetoMinusculas.Length)];
        caracteres[2] = AlfabetoDigitos[RandomNumberGenerator.GetInt32(AlfabetoDigitos.Length)];
        caracteres[3] = AlfabetoEspeciales[RandomNumberGenerator.GetInt32(AlfabetoEspeciales.Length)];

        for (var i = 4; i < LongitudClaveTemporal; i++)
            caracteres[i] = alfabetoCompleto[RandomNumberGenerator.GetInt32(alfabetoCompleto.Length)];

        for (var i = caracteres.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
        }

        return new string(caracteres);
    }

    // Agrega la fila de auditoría al mismo ChangeTracker que la modificación
    // del usuario — un único SaveChangesAsync() posterior persiste ambas en
    // la misma transacción implícita de EF Core (misma unidad transaccional).
    private void RegistrarAuditoria(long idAdmin, string accion, long idUsuarioObjetivo, string? valorAnterior, string? valorNuevo, string observacion, string entidad = "usuarios", string? idEntidad = null)
    {
        _db.Auditorias.Add(new Auditoria
        {
            IdUsuario     = idAdmin,
            Modulo        = "USUARIOS_ADMIN",
            Accion        = accion,
            Entidad       = entidad,
            IdEntidad     = idEntidad ?? idUsuarioObjetivo.ToString(),
            ValorAnterior = valorAnterior,
            ValorNuevo    = valorNuevo,
            Resultado     = "EXITOSO",
            Observacion   = observacion,
            FechaEvento   = DateTime.UtcNow
        });
    }

    // Fase USUARIOS-ADMIN-4: normaliza observacion antes de guardarla o
    // auditarla — trim, máximo 500 caracteres (mismo límite que la columna
    // usuario_roles.observacion VARCHAR(500)), null si queda vacía.
    private static string? NormalizarObservacion(string? observacion)
    {
        if (string.IsNullOrWhiteSpace(observacion)) return null;
        var t = observacion.Trim();
        return t.Length > 500 ? t[..500] : t;
    }

    // Fase USUARIOS-ADMIN-4: ¿el usuario tiene el rol indicado ACTIVO ahora
    // mismo? Usado para decidir si el actor puede tocar el rol SUPERUSUARIO
    // — independiente de lo que diga su JWT (que puede estar desactualizado,
    // ver punto 8 del precheck), siempre se valida contra el estado real.
    private async Task<bool> ActorTieneRolAsync(long idUsuario, string codigoRol)
    {
        return await (
            from ur in _db.UsuarioRoles
            join r in _db.Roles on ur.IdRol equals r.IdRol
            where ur.IdUsuario == idUsuario && ur.Estado == "ACTIVO"
               && r.Codigo == codigoRol && r.Estado == "ACTIVO"
            select ur.IdUsuario
        ).AnyAsync();
    }

    // Fase USUARIOS-ADMIN-4: GET /api/admin/roles/asignables — lista blanca
    // filtrada según el privilegio real del actor (nunca SUPERUSUARIO si el
    // actor no lo tiene activo).
    public async Task<List<RolAsignableDto>> ListarRolesAsignablesAsync(long idAdmin)
    {
        var actorEsSuperusuario = await ActorTieneRolAsync(idAdmin, RolSuperusuario);
        var codigosPermitidos = actorEsSuperusuario
            ? RolesAsignables
            : RolesAsignables.Where(c => c != RolSuperusuario).ToArray();

        return await _db.Roles
            .Where(r => codigosPermitidos.Contains(r.Codigo) && r.Estado == "ACTIVO")
            .OrderBy(r => r.Nombre)
            .Select(r => new RolAsignableDto { Codigo = r.Codigo, Nombre = r.Nombre })
            .ToListAsync();
    }

    // Fase USUARIOS-ADMIN-4: asignar un rol de la lista blanca. Si la fila en
    // usuario_roles no existe, se inserta; si existe INACTIVO (revocada
    // antes), se reactiva sin crear una segunda fila (la PK compuesta
    // (id_usuario, id_rol) ya lo impediría a nivel de esquema de todas
    // formas, pero esta lógica evita depender de esa excepción).
    public async Task<UsuarioAdminDetalleDto> AsignarRolAsync(long idUsuario, long idAdmin, string rolCodigo, string? observacionCruda)
    {
        var observacion = NormalizarObservacion(observacionCruda);

        if (idUsuario == idAdmin)
            throw new InvalidOperationException("No puedes asignarte roles a ti mismo.");

        _ = await _db.Usuarios.FindAsync(idUsuario)
            ?? throw new KeyNotFoundException($"Usuario {idUsuario} no encontrado.");

        if (!RolesAsignables.Contains(rolCodigo))
            throw new InvalidOperationException($"El rol '{rolCodigo}' no puede asignarse desde este módulo.");

        if (rolCodigo == RolSuperusuario && !await ActorTieneRolAsync(idAdmin, RolSuperusuario))
            throw new UnauthorizedAccessException("Solo un usuario con rol SUPERUSUARIO puede asignar el rol SUPERUSUARIO.");

        var rol = await _db.Roles.FirstOrDefaultAsync(r => r.Codigo == rolCodigo && r.Estado == "ACTIVO")
            ?? throw new InvalidOperationException($"El rol '{rolCodigo}' no está disponible.");

        var asignacion = await _db.UsuarioRoles
            .FirstOrDefaultAsync(ur => ur.IdUsuario == idUsuario && ur.IdRol == rol.IdRol);

        string valorAnterior;
        if (asignacion == null)
        {
            valorAnterior = "NO_ASIGNADO";
            _db.UsuarioRoles.Add(new UsuarioRol
            {
                IdUsuario       = idUsuario,
                IdRol           = rol.IdRol,
                Estado          = "ACTIVO",
                FechaAsignacion = DateTime.UtcNow,
                AsignadoPor     = idAdmin,
                Observacion     = observacion
            });
        }
        else
        {
            if (asignacion.Estado == "ACTIVO")
                throw new TransicionUsuarioInvalidaException($"El usuario ya tiene el rol '{rolCodigo}' activo.");

            valorAnterior              = "INACTIVO";
            asignacion.Estado          = "ACTIVO";
            asignacion.FechaAsignacion = DateTime.UtcNow;
            asignacion.AsignadoPor     = idAdmin;
            asignacion.FechaRevocacion = null;
            asignacion.RevocadoPor     = null;
            asignacion.Observacion     = observacion;
        }

        RegistrarAuditoria(idAdmin, "USUARIO_ROL_ASIGNAR", idUsuario, valorAnterior, "ACTIVO",
            observacion ?? $"Asignación de rol {rolCodigo}.", entidad: "usuario_roles", idEntidad: $"{idUsuario}:{rol.IdRol}");

        await _db.SaveChangesAsync();
        return (await ObtenerDetalleAsync(idUsuario))!;
    }

    // Fase USUARIOS-ADMIN-4: revocar un rol activo — nunca DELETE, siempre
    // UPDATE de estado (conserva el historial de la asignación original).
    public async Task<UsuarioAdminDetalleDto> RevocarRolAsync(long idUsuario, long idRol, long idAdmin, string? observacionCruda)
    {
        var observacion = NormalizarObservacion(observacionCruda);

        if (idUsuario == idAdmin)
            throw new InvalidOperationException("No puedes revocarte roles a ti mismo.");

        _ = await _db.Usuarios.FindAsync(idUsuario)
            ?? throw new KeyNotFoundException($"Usuario {idUsuario} no encontrado.");

        var rol = await _db.Roles.FindAsync(idRol)
            ?? throw new KeyNotFoundException($"Rol {idRol} no encontrado.");

        var asignacion = await _db.UsuarioRoles
            .FirstOrDefaultAsync(ur => ur.IdUsuario == idUsuario && ur.IdRol == idRol);
        if (asignacion == null || asignacion.Estado != "ACTIVO")
            throw new TransicionUsuarioInvalidaException($"El usuario no tiene el rol '{rol.Codigo}' activo.");

        if (rol.Codigo == RolSuperusuario && !await ActorTieneRolAsync(idAdmin, RolSuperusuario))
            throw new UnauthorizedAccessException("Solo un usuario con rol SUPERUSUARIO puede revocar el rol SUPERUSUARIO.");

        // Protección del último administrador — aplica a ADMIN_XPAY y
        // SUPERUSUARIO aunque ADMIN_XPAY ya no sea asignable desde este
        // módulo (puede existir en usuarios previos, ej. ci_admin_xpay).
        if (rol.Codigo == RolAdminXpay || rol.Codigo == RolSuperusuario)
        {
            var otrosAdminsActivos = await (
                from u in _db.Usuarios
                join ur2 in _db.UsuarioRoles on u.IdUsuario equals ur2.IdUsuario
                join r2 in _db.Roles on ur2.IdRol equals r2.IdRol
                where u.IdUsuario != idUsuario
                   && u.Estado == "ACTIVO"
                   && ur2.Estado == "ACTIVO"
                   && r2.Estado == "ACTIVO"
                   && (r2.Codigo == RolAdminXpay || r2.Codigo == RolSuperusuario)
                select u.IdUsuario
            ).Distinct().CountAsync();

            if (otrosAdminsActivos == 0)
                throw new InvalidOperationException("No se puede revocar: es el último administrador activo del sistema.");
        }

        // No dejar al usuario objetivo sin ningún rol activo — regla general,
        // no exclusiva de roles administrativos.
        var otrosRolesActivos = await _db.UsuarioRoles
            .CountAsync(ur => ur.IdUsuario == idUsuario && ur.IdRol != idRol && ur.Estado == "ACTIVO");
        if (otrosRolesActivos == 0)
            throw new InvalidOperationException("No se puede revocar: el usuario quedaría sin ningún rol asignado.");

        asignacion.Estado          = "INACTIVO";
        asignacion.FechaRevocacion = DateTime.UtcNow;
        asignacion.RevocadoPor     = idAdmin;
        asignacion.Observacion     = observacion;

        RegistrarAuditoria(idAdmin, "USUARIO_ROL_REVOCAR", idUsuario, "ACTIVO", "INACTIVO",
            observacion ?? $"Revocación de rol {rol.Codigo}.", entidad: "usuario_roles", idEntidad: $"{idUsuario}:{idRol}");

        await _db.SaveChangesAsync();
        return (await ObtenerDetalleAsync(idUsuario))!;
    }

    private static string NombreCompleto(string primerNombre, string? segundoNombre, string primerApellido, string? segundoApellido)
    {
        var partes = new[] { primerNombre, segundoNombre, primerApellido, segundoApellido }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(' ', partes);
    }
}
