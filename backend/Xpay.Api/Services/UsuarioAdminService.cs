using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Exceptions;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

// Fase USUARIOS-ADMIN-2: solo lectura (listado + detalle).
// Fase USUARIOS-ADMIN-3: activar/inactivar/desbloquear. Sigue sin crear,
// editar, restablecer clave ni asignar roles — eso queda para subfases
// posteriores, ya planeadas pero no autorizadas todavía.
public class UsuarioAdminService
{
    private const string RolAdminXpay    = "ADMIN_XPAY";
    private const string RolSuperusuario = "SUPERUSUARIO";

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

    // Agrega la fila de auditoría al mismo ChangeTracker que la modificación
    // del usuario — un único SaveChangesAsync() posterior persiste ambas en
    // la misma transacción implícita de EF Core (misma unidad transaccional).
    private void RegistrarAuditoria(long idAdmin, string accion, long idUsuarioObjetivo, string valorAnterior, string valorNuevo, string observacion)
    {
        _db.Auditorias.Add(new Auditoria
        {
            IdUsuario     = idAdmin,
            Modulo        = "USUARIOS_ADMIN",
            Accion        = accion,
            Entidad       = "usuarios",
            IdEntidad     = idUsuarioObjetivo.ToString(),
            ValorAnterior = valorAnterior,
            ValorNuevo    = valorNuevo,
            Resultado     = "EXITOSO",
            Observacion   = observacion,
            FechaEvento   = DateTime.UtcNow
        });
    }

    private static string NombreCompleto(string primerNombre, string? segundoNombre, string primerApellido, string? segundoApellido)
    {
        var partes = new[] { primerNombre, segundoNombre, primerApellido, segundoApellido }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(' ', partes);
    }
}
