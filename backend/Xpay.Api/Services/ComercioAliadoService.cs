using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class ComercioAliadoService
{
    private readonly XpayDbContext                     _db;
    private readonly ILogger<ComercioAliadoService>   _logger;
    private readonly string                            _uploadsRoot;

    private static readonly HashSet<string> EstadosComercio = [
        "BORRADOR","EN_REVISION","APROBADO","RECHAZADO","ACTIVO","INACTIVO"
    ];
    private static readonly HashSet<string> TiposPersona     = ["NATURAL","JURIDICA"];
    private static readonly HashSet<string> TiposDocumento   = ["CC","CE","NIT","PASAPORTE","OTRO"];
    private static readonly HashSet<string> TiposDocArchivo  = [
        "CONTRATO","CAMARA_COMERCIO","RUT","DOCUMENTO_REPRESENTANTE","FORMULARIO_SOLICITUD"
    ];
    private static readonly HashSet<string> RolesSolicitados  = ["ADMIN_COMERCIO","CAJERO"];
    private static readonly HashSet<string> ContentTypesOk    = ["application/pdf","image/jpeg","image/png","image/jpg"];
    private static readonly HashSet<string> ExtensionesOk     = [".pdf",".jpg",".jpeg",".png"];
    private const long MaxFileBytes = 5 * 1024 * 1024; // 5 MB

    public ComercioAliadoService(
        XpayDbContext db,
        ILogger<ComercioAliadoService> logger,
        IWebHostEnvironment env)
    {
        _db          = db;
        _logger      = logger;
        _uploadsRoot = Path.Combine(env.ContentRootPath, "uploads", "comercios");
        Directory.CreateDirectory(_uploadsRoot);
    }

    // ── Comercios aliados ─────────────────────────────────────────────────────

    public async Task<List<ComercioAliadoListItem>> ListarAsync()
    {
        return await _db.ComerciosAliados
            .OrderByDescending(c => c.IdComercioAliado)
            .Select(c => new ComercioAliadoListItem
            {
                IdComercioAliado = c.IdComercioAliado,
                RazonSocial      = c.RazonSocial,
                NombreComercial  = c.NombreComercial,
                Nit              = c.Nit,
                Ciudad           = c.Ciudad,
                Estado           = c.Estado,
                FechaSolicitud   = c.FechaSolicitud,
                CreatedAt        = c.CreatedAt,
            })
            .ToListAsync();
    }

    public async Task<ComercioAliadoResponse> GetByIdAsync(long id)
    {
        var c = await _db.ComerciosAliados.FindAsync(id)
            ?? throw new KeyNotFoundException($"Comercio aliado {id} no encontrado.");
        return ToResponse(c);
    }

    public async Task<ComercioAliadoResponse> CrearAsync(CrearComercioAliadoRequest req, long adminId)
    {
        ValidarCrearComercio(req);
        if (await _db.ComerciosAliados.AnyAsync(c => c.Nit == req.Nit.Trim()))
            throw new InvalidOperationException($"Ya existe un comercio aliado con NIT '{req.Nit}'.");

        var now = DateTime.UtcNow;
        var c = new ComercioAliado
        {
            IdComercioExistente   = req.IdComercioExistente,
            RazonSocial           = req.RazonSocial.Trim(),
            NombreComercial       = req.NombreComercial.Trim(),
            Nit                   = req.Nit.Trim(),
            TipoPersona           = req.TipoPersona.Trim().ToUpperInvariant(),
            ActividadEconomica    = req.ActividadEconomica?.Trim(),
            CodigoCiiu            = req.CodigoCiiu?.Trim(),
            DireccionPrincipal    = req.DireccionPrincipal?.Trim(),
            Ciudad                = req.Ciudad?.Trim(),
            Departamento          = req.Departamento?.Trim(),
            Telefono              = req.Telefono?.Trim(),
            Correo                = req.Correo?.Trim(),
            SitioWeb              = req.SitioWeb?.Trim(),
            Estado                = req.Estado.Trim().ToUpperInvariant(),
            CondicionesComerciales = req.CondicionesComerciales?.Trim(),
            FechaSolicitud        = now,
            FechaInicioConvenio   = ParseDateOnly(req.FechaInicioConvenio),
            FechaFinConvenio      = ParseDateOnly(req.FechaFinConvenio),
            Observaciones         = req.Observaciones?.Trim(),
            CreatedAt             = now,
            CreatedByUsuario      = adminId,
        };
        _db.ComerciosAliados.Add(c);
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_ALIADO_CREAR id={Id} nit={Nit} admin={Admin}", c.IdComercioAliado, c.Nit, adminId);
        return ToResponse(c);
    }

    public async Task<ComercioAliadoResponse> ActualizarAsync(long id, ActualizarComercioAliadoRequest req, long adminId)
    {
        var c = await _db.ComerciosAliados.FindAsync(id)
            ?? throw new KeyNotFoundException($"Comercio aliado {id} no encontrado.");

        if (!string.IsNullOrWhiteSpace(req.RazonSocial))        c.RazonSocial         = req.RazonSocial.Trim();
        if (!string.IsNullOrWhiteSpace(req.NombreComercial))     c.NombreComercial     = req.NombreComercial.Trim();
        if (!string.IsNullOrWhiteSpace(req.TipoPersona))
        {
            var tp = req.TipoPersona.Trim().ToUpperInvariant();
            if (!TiposPersona.Contains(tp)) throw new InvalidOperationException("tipo_persona debe ser NATURAL o JURIDICA.");
            c.TipoPersona = tp;
        }
        if (!string.IsNullOrWhiteSpace(req.Estado))
        {
            var est = req.Estado.Trim().ToUpperInvariant();
            if (!EstadosComercio.Contains(est)) throw new InvalidOperationException($"Estado inválido: {est}.");
            c.Estado = est;
            if (est == "APROBADO" && !c.FechaAprobacion.HasValue)
                c.FechaAprobacion = DateTime.UtcNow;
        }
        c.ActividadEconomica    = req.ActividadEconomica?.Trim()     ?? c.ActividadEconomica;
        c.CodigoCiiu            = req.CodigoCiiu?.Trim()             ?? c.CodigoCiiu;
        c.DireccionPrincipal    = req.DireccionPrincipal?.Trim()     ?? c.DireccionPrincipal;
        c.Ciudad                = req.Ciudad?.Trim()                  ?? c.Ciudad;
        c.Departamento          = req.Departamento?.Trim()            ?? c.Departamento;
        c.Telefono              = req.Telefono?.Trim()                ?? c.Telefono;
        c.Correo                = req.Correo?.Trim()                  ?? c.Correo;
        c.SitioWeb              = req.SitioWeb?.Trim()                ?? c.SitioWeb;
        c.CondicionesComerciales = req.CondicionesComerciales?.Trim() ?? c.CondicionesComerciales;
        c.Observaciones         = req.Observaciones?.Trim()           ?? c.Observaciones;
        if (req.FechaInicioConvenio != null) c.FechaInicioConvenio = ParseDateOnly(req.FechaInicioConvenio);
        if (req.FechaFinConvenio    != null) c.FechaFinConvenio    = ParseDateOnly(req.FechaFinConvenio);
        if (req.IdComercioExistente.HasValue) c.IdComercioExistente = req.IdComercioExistente;
        c.UpdatedAt        = DateTime.UtcNow;
        c.UpdatedByUsuario = adminId;

        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_ALIADO_ACTUALIZAR id={Id} admin={Admin}", id, adminId);
        return ToResponse(c);
    }

    public async Task<ComercioAliadoResponse> ActivarAsync(long id, long adminId)
        => await CambiarEstadoAsync(id, "ACTIVO", adminId, "COMERCIO_ALIADO_ACTIVAR");

    public async Task<ComercioAliadoResponse> InactivarAsync(long id, long adminId)
        => await CambiarEstadoAsync(id, "INACTIVO", adminId, "COMERCIO_ALIADO_INACTIVAR");

    private async Task<ComercioAliadoResponse> CambiarEstadoAsync(long id, string nuevoEstado, long adminId, string logEvent)
    {
        var c = await _db.ComerciosAliados.FindAsync(id)
            ?? throw new KeyNotFoundException($"Comercio aliado {id} no encontrado.");
        c.Estado           = nuevoEstado;
        c.UpdatedAt        = DateTime.UtcNow;
        c.UpdatedByUsuario = adminId;
        await _db.SaveChangesAsync();
        _logger.LogInformation("{Event} id={Id} admin={Admin}", logEvent, id, adminId);
        return ToResponse(c);
    }

    // ── Representantes legales ────────────────────────────────────────────────

    public async Task<List<RepresentanteResponse>> ListarRepresentantesAsync(long idComercioAliado)
    {
        return await _db.ComercioRepresentantesLegales
            .Where(r => r.IdComercioAliado == idComercioAliado)
            .OrderBy(r => r.IdRepresentante)
            .Select(r => ToRepresentanteResponse(r))
            .ToListAsync();
    }

    public async Task<RepresentanteResponse> CrearRepresentanteAsync(long idComercioAliado, CrearRepresentanteRequest req, long adminId)
    {
        await ExistirComercioAsync(idComercioAliado);
        var td = (req.TipoDocumento ?? string.Empty).Trim().ToUpperInvariant();
        if (!TiposDocumento.Contains(td)) throw new InvalidOperationException($"tipo_documento inválido: {td}.");
        if (string.IsNullOrWhiteSpace(req.NumeroDocumento)) throw new InvalidOperationException("numero_documento es obligatorio.");
        if (string.IsNullOrWhiteSpace(req.Nombres)) throw new InvalidOperationException("nombres es obligatorio.");

        var now = DateTime.UtcNow;
        var r = new ComercioRepresentanteLegal
        {
            IdComercioAliado         = idComercioAliado,
            TipoDocumento            = td,
            NumeroDocumento          = req.NumeroDocumento.Trim(),
            Nombres                  = req.Nombres.Trim(),
            Apellidos                = req.Apellidos?.Trim(),
            Celular                  = req.Celular?.Trim(),
            Correo                   = req.Correo?.Trim(),
            Cargo                    = req.Cargo?.Trim(),
            FechaExpedicionDocumento = ParseDateOnly(req.FechaExpedicionDocumento),
            Estado                   = "ACTIVO",
            CreatedAt                = now,
        };
        _db.ComercioRepresentantesLegales.Add(r);
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_REPRESENTANTE_CREAR id={Id} comercio={C} admin={A}", r.IdRepresentante, idComercioAliado, adminId);
        return ToRepresentanteResponse(r);
    }

    public async Task<RepresentanteResponse> ActualizarRepresentanteAsync(long idRepresentante, ActualizarRepresentanteRequest req, long adminId)
    {
        var r = await _db.ComercioRepresentantesLegales.FindAsync(idRepresentante)
            ?? throw new KeyNotFoundException($"Representante {idRepresentante} no encontrado.");
        if (!string.IsNullOrWhiteSpace(req.TipoDocumento))
        {
            var td = req.TipoDocumento.Trim().ToUpperInvariant();
            if (!TiposDocumento.Contains(td)) throw new InvalidOperationException($"tipo_documento inválido: {td}.");
            r.TipoDocumento = td;
        }
        if (!string.IsNullOrWhiteSpace(req.NumeroDocumento)) r.NumeroDocumento          = req.NumeroDocumento.Trim();
        if (!string.IsNullOrWhiteSpace(req.Nombres))          r.Nombres                  = req.Nombres.Trim();
        r.Apellidos                = req.Apellidos?.Trim()  ?? r.Apellidos;
        r.Celular                  = req.Celular?.Trim()    ?? r.Celular;
        r.Correo                   = req.Correo?.Trim()     ?? r.Correo;
        r.Cargo                    = req.Cargo?.Trim()      ?? r.Cargo;
        if (req.FechaExpedicionDocumento != null) r.FechaExpedicionDocumento = ParseDateOnly(req.FechaExpedicionDocumento);
        if (!string.IsNullOrWhiteSpace(req.Estado))
        {
            var est = req.Estado.Trim().ToUpperInvariant();
            if (est != "ACTIVO" && est != "INACTIVO") throw new InvalidOperationException("Estado debe ser ACTIVO o INACTIVO.");
            r.Estado = est;
        }
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_REPRESENTANTE_ACTUALIZAR id={Id} admin={A}", idRepresentante, adminId);
        return ToRepresentanteResponse(r);
    }

    // ── Establecimientos ──────────────────────────────────────────────────────

    public async Task<List<EstablecimientoResponse>> ListarEstablecimientosAsync(long idComercioAliado)
    {
        return await _db.ComercioEstablecimientos
            .Where(e => e.IdComercioAliado == idComercioAliado)
            .OrderBy(e => e.IdEstablecimiento)
            .Select(e => ToEstablecimientoResponse(e))
            .ToListAsync();
    }

    public async Task<EstablecimientoResponse> CrearEstablecimientoAsync(long idComercioAliado, CrearEstablecimientoRequest req, long adminId)
    {
        await ExistirComercioAsync(idComercioAliado);
        if (string.IsNullOrWhiteSpace(req.NombreEstablecimiento))
            throw new InvalidOperationException("nombre_establecimiento es obligatorio.");

        var now = DateTime.UtcNow;
        var est = new ComercioEstablecimiento
        {
            IdComercioAliado      = idComercioAliado,
            NombreEstablecimiento = req.NombreEstablecimiento.Trim(),
            Direccion             = req.Direccion?.Trim(),
            Ciudad                = req.Ciudad?.Trim(),
            Telefono              = req.Telefono?.Trim(),
            Responsable           = req.Responsable?.Trim(),
            Estado                = "ACTIVO",
            CreatedAt             = now,
        };
        _db.ComercioEstablecimientos.Add(est);
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_ESTABLECIMIENTO_CREAR id={Id} comercio={C} admin={A}", est.IdEstablecimiento, idComercioAliado, adminId);
        return ToEstablecimientoResponse(est);
    }

    public async Task<EstablecimientoResponse> ActualizarEstablecimientoAsync(long idEstablecimiento, ActualizarEstablecimientoRequest req, long adminId)
    {
        var est = await _db.ComercioEstablecimientos.FindAsync(idEstablecimiento)
            ?? throw new KeyNotFoundException($"Establecimiento {idEstablecimiento} no encontrado.");
        if (!string.IsNullOrWhiteSpace(req.NombreEstablecimiento)) est.NombreEstablecimiento = req.NombreEstablecimiento.Trim();
        est.Direccion   = req.Direccion?.Trim()   ?? est.Direccion;
        est.Ciudad      = req.Ciudad?.Trim()       ?? est.Ciudad;
        est.Telefono    = req.Telefono?.Trim()     ?? est.Telefono;
        est.Responsable = req.Responsable?.Trim()  ?? est.Responsable;
        if (!string.IsNullOrWhiteSpace(req.Estado))
        {
            var e = req.Estado.Trim().ToUpperInvariant();
            if (e != "ACTIVO" && e != "INACTIVO") throw new InvalidOperationException("Estado debe ser ACTIVO o INACTIVO.");
            est.Estado = e;
        }
        est.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_ESTABLECIMIENTO_ACTUALIZAR id={Id} admin={A}", idEstablecimiento, adminId);
        return ToEstablecimientoResponse(est);
    }

    // ── Usuarios solicitados ──────────────────────────────────────────────────

    public async Task<List<UsuarioSolicitadoResponse>> ListarUsuariosSolicitadosAsync(long idComercioAliado)
    {
        return await _db.ComercioUsuariosSolicitados
            .Where(u => u.IdComercioAliado == idComercioAliado)
            .OrderBy(u => u.IdUsuarioSolicitado)
            .Select(u => ToUsuarioSolicitadoResponse(u))
            .ToListAsync();
    }

    public async Task<UsuarioSolicitadoResponse> CrearUsuarioSolicitadoAsync(long idComercioAliado, CrearUsuarioSolicitadoRequest req, long adminId)
    {
        await ExistirComercioAsync(idComercioAliado);
        if (string.IsNullOrWhiteSpace(req.Nombres)) throw new InvalidOperationException("nombres es obligatorio.");
        var rol = (req.RolSolicitado ?? string.Empty).Trim().ToUpperInvariant();
        if (!RolesSolicitados.Contains(rol)) throw new InvalidOperationException($"rol_solicitado inválido: {rol}. Valores: ADMIN_COMERCIO, CAJERO.");

        var now = DateTime.UtcNow;
        var u = new ComercioUsuarioSolicitado
        {
            IdComercioAliado  = idComercioAliado,
            IdEstablecimiento = req.IdEstablecimiento,
            IdUsuario         = req.IdUsuario,
            Nombres           = req.Nombres.Trim(),
            Correo            = req.Correo?.Trim(),
            Celular           = req.Celular?.Trim(),
            RolSolicitado     = rol,
            Estado            = "PENDIENTE_CREACION",
            CreatedAt         = now,
        };
        _db.ComercioUsuariosSolicitados.Add(u);
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_USUARIO_SOLICITAR id={Id} comercio={C} admin={A}", u.IdUsuarioSolicitado, idComercioAliado, adminId);
        return ToUsuarioSolicitadoResponse(u);
    }

    public async Task<UsuarioSolicitadoResponse> ActualizarUsuarioSolicitadoAsync(long idUsuarioSolicitado, ActualizarUsuarioSolicitadoRequest req, long adminId)
    {
        var u = await _db.ComercioUsuariosSolicitados.FindAsync(idUsuarioSolicitado)
            ?? throw new KeyNotFoundException($"Usuario solicitado {idUsuarioSolicitado} no encontrado.");
        if (!string.IsNullOrWhiteSpace(req.Nombres))      u.Nombres       = req.Nombres.Trim();
        u.Correo  = req.Correo?.Trim()  ?? u.Correo;
        u.Celular = req.Celular?.Trim() ?? u.Celular;
        if (!string.IsNullOrWhiteSpace(req.RolSolicitado))
        {
            var rol = req.RolSolicitado.Trim().ToUpperInvariant();
            if (!RolesSolicitados.Contains(rol)) throw new InvalidOperationException($"rol_solicitado inválido: {rol}.");
            u.RolSolicitado = rol;
        }
        if (!string.IsNullOrWhiteSpace(req.Estado))
        {
            var est = req.Estado.Trim().ToUpperInvariant();
            if (!new[] {"PENDIENTE_CREACION","CREADO","INACTIVO"}.Contains(est))
                throw new InvalidOperationException($"Estado inválido: {est}.");
            u.Estado = est;
        }
        if (req.IdEstablecimiento.HasValue) u.IdEstablecimiento = req.IdEstablecimiento;
        if (req.IdUsuario.HasValue)         u.IdUsuario         = req.IdUsuario;
        u.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_USUARIO_ACTUALIZAR id={Id} admin={A}", idUsuarioSolicitado, adminId);
        return ToUsuarioSolicitadoResponse(u);
    }

    // ── Documentos ────────────────────────────────────────────────────────────

    public async Task<List<DocumentoResponse>> ListarDocumentosAsync(long idComercioAliado)
    {
        return await _db.ComercioDocumentos
            .Where(d => d.IdComercioAliado == idComercioAliado && d.Estado == "ACTIVO")
            .OrderBy(d => d.IdDocumento)
            .Select(d => ToDocumentoResponse(d))
            .ToListAsync();
    }

    public async Task<CompleitudDocumentalResponse> GetCompleitudAsync(long idComercioAliado)
    {
        var docs = await _db.ComercioDocumentos
            .Where(d => d.IdComercioAliado == idComercioAliado && d.Estado == "ACTIVO")
            .Select(d => d.TipoDocumento)
            .ToListAsync();

        var resp = new CompleitudDocumentalResponse
        {
            Contrato               = docs.Contains("CONTRATO"),
            CamaraComercio         = docs.Contains("CAMARA_COMERCIO"),
            Rut                    = docs.Contains("RUT"),
            DocumentoRepresentante = docs.Contains("DOCUMENTO_REPRESENTANTE"),
            FormularioSolicitud    = docs.Contains("FORMULARIO_SOLICITUD"),
        };
        resp.TotalCargados = new[] { resp.Contrato, resp.CamaraComercio, resp.Rut, resp.DocumentoRepresentante, resp.FormularioSolicitud }
            .Count(b => b);
        return resp;
    }

    public async Task<DocumentoResponse> SubirDocumentoAsync(
        long idComercioAliado, string tipoDocumento, IFormFile archivo, string? observaciones, long adminId)
    {
        await ExistirComercioAsync(idComercioAliado);

        var tipo = tipoDocumento.Trim().ToUpperInvariant();
        if (!TiposDocArchivo.Contains(tipo))
            throw new InvalidOperationException($"tipo_documento inválido: {tipo}.");

        if (archivo == null || archivo.Length == 0)
            throw new InvalidOperationException("Archivo vacío o no recibido.");

        if (archivo.Length > MaxFileBytes)
            throw new InvalidOperationException($"El archivo supera el límite de 5 MB ({archivo.Length} bytes).");

        var ext = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (!ExtensionesOk.Contains(ext))
            throw new InvalidOperationException($"Extensión no permitida: '{ext}'. Se permiten: .pdf, .jpg, .jpeg, .png.");

        var ct = archivo.ContentType?.ToLowerInvariant() ?? string.Empty;
        // Allow image/jpg as alias
        if (!ContentTypesOk.Contains(ct) && ct != "image/jpg")
            throw new InvalidOperationException($"Content-type no permitido: '{ct}'.");

        // Marcar documentos anteriores del mismo tipo como REEMPLAZADO
        var anteriores = await _db.ComercioDocumentos
            .Where(d => d.IdComercioAliado == idComercioAliado && d.TipoDocumento == tipo && d.Estado == "ACTIVO")
            .ToListAsync();
        foreach (var a in anteriores) a.Estado = "REEMPLAZADO";

        // Guardar archivo en disco
        var carpeta = Path.Combine(_uploadsRoot, idComercioAliado.ToString());
        Directory.CreateDirectory(carpeta);
        var nombreSeguro  = $"{Guid.NewGuid():N}_{SanitizarNombre(Path.GetFileName(archivo.FileName))}";
        var rutaAbsoluta  = Path.Combine(carpeta, nombreSeguro);
        var storagePath   = Path.Combine("comercios", idComercioAliado.ToString(), nombreSeguro);

        await using (var stream = File.Create(rutaAbsoluta))
            await archivo.CopyToAsync(stream);

        var now = DateTime.UtcNow;
        var doc = new ComercioDocumento
        {
            IdComercioAliado      = idComercioAliado,
            TipoDocumento         = tipo,
            NombreArchivoOriginal = archivo.FileName,
            StoragePath           = storagePath,
            ContentType           = archivo.ContentType,
            SizeBytes             = archivo.Length,
            Estado                = "ACTIVO",
            Observaciones         = observaciones?.Trim(),
            UploadedAt            = now,
            UploadedByUsuario     = adminId,
        };
        _db.ComercioDocumentos.Add(doc);
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_DOCUMENTO_SUBIR id={Id} tipo={T} comercio={C} admin={A}", doc.IdDocumento, tipo, idComercioAliado, adminId);
        return ToDocumentoResponse(doc);
    }

    public async Task<(Stream stream, string contentType, string fileName)> DescargarDocumentoAsync(long idDocumento)
    {
        var doc = await _db.ComercioDocumentos.FindAsync(idDocumento)
            ?? throw new KeyNotFoundException($"Documento {idDocumento} no encontrado.");

        var rutaAbsoluta = Path.Combine(_uploadsRoot, "..", doc.StoragePath);
        rutaAbsoluta = Path.GetFullPath(rutaAbsoluta);

        // Security: ensure path stays under uploads root
        var uploadsRoot = Path.GetFullPath(_uploadsRoot);
        if (!rutaAbsoluta.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta de documento inválida.");

        if (!File.Exists(rutaAbsoluta))
            throw new FileNotFoundException($"Archivo del documento {idDocumento} no encontrado en almacenamiento.");

        var ct    = doc.ContentType ?? "application/octet-stream";
        var stream = File.OpenRead(rutaAbsoluta);
        return (stream, ct, doc.NombreArchivoOriginal);
    }

    public async Task EliminarDocumentoAsync(long idDocumento, long adminId)
    {
        var doc = await _db.ComercioDocumentos.FindAsync(idDocumento)
            ?? throw new KeyNotFoundException($"Documento {idDocumento} no encontrado.");
        doc.Estado = "ELIMINADO";
        await _db.SaveChangesAsync();
        _logger.LogInformation("COMERCIO_DOCUMENTO_ELIMINAR id={Id} admin={A}", idDocumento, adminId);
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private async Task ExistirComercioAsync(long idComercioAliado)
    {
        if (!await _db.ComerciosAliados.AnyAsync(c => c.IdComercioAliado == idComercioAliado))
            throw new KeyNotFoundException($"Comercio aliado {idComercioAliado} no encontrado.");
    }

    private static void ValidarCrearComercio(CrearComercioAliadoRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RazonSocial))    throw new InvalidOperationException("razon_social es obligatoria.");
        if (string.IsNullOrWhiteSpace(req.NombreComercial)) throw new InvalidOperationException("nombre_comercial es obligatorio.");
        if (string.IsNullOrWhiteSpace(req.Nit))            throw new InvalidOperationException("nit es obligatorio.");
        var tp = (req.TipoPersona ?? string.Empty).Trim().ToUpperInvariant();
        if (!TiposPersona.Contains(tp)) throw new InvalidOperationException("tipo_persona debe ser NATURAL o JURIDICA.");
        var est = (req.Estado ?? string.Empty).Trim().ToUpperInvariant();
        if (!EstadosComercio.Contains(est)) throw new InvalidOperationException($"Estado inválido: {est}.");
    }

    private static string SanitizarNombre(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        return new string(nombre.Select(c => invalidos.Contains(c) ? '_' : c).ToArray());
    }

    private static DateOnly? ParseDateOnly(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateOnly.TryParse(s, out var d) ? d : null;
    }

    // ── Proyecciones ──────────────────────────────────────────────────────────

    private static ComercioAliadoResponse ToResponse(ComercioAliado c) => new()
    {
        IdComercioAliado       = c.IdComercioAliado,
        IdComercioExistente    = c.IdComercioExistente,
        RazonSocial            = c.RazonSocial,
        NombreComercial        = c.NombreComercial,
        Nit                    = c.Nit,
        TipoPersona            = c.TipoPersona,
        ActividadEconomica     = c.ActividadEconomica,
        CodigoCiiu             = c.CodigoCiiu,
        DireccionPrincipal     = c.DireccionPrincipal,
        Ciudad                 = c.Ciudad,
        Departamento           = c.Departamento,
        Telefono               = c.Telefono,
        Correo                 = c.Correo,
        SitioWeb               = c.SitioWeb,
        Estado                 = c.Estado,
        CondicionesComerciales = c.CondicionesComerciales,
        FechaSolicitud         = c.FechaSolicitud,
        FechaAprobacion        = c.FechaAprobacion,
        FechaInicioConvenio    = c.FechaInicioConvenio?.ToString("yyyy-MM-dd"),
        FechaFinConvenio       = c.FechaFinConvenio?.ToString("yyyy-MM-dd"),
        Observaciones          = c.Observaciones,
        CreatedAt              = c.CreatedAt,
        UpdatedAt              = c.UpdatedAt,
    };

    private static RepresentanteResponse ToRepresentanteResponse(ComercioRepresentanteLegal r) => new()
    {
        IdRepresentante          = r.IdRepresentante,
        IdComercioAliado         = r.IdComercioAliado,
        TipoDocumento            = r.TipoDocumento,
        NumeroDocumento          = r.NumeroDocumento,
        Nombres                  = r.Nombres,
        Apellidos                = r.Apellidos,
        Celular                  = r.Celular,
        Correo                   = r.Correo,
        Cargo                    = r.Cargo,
        FechaExpedicionDocumento = r.FechaExpedicionDocumento?.ToString("yyyy-MM-dd"),
        Estado                   = r.Estado,
        CreatedAt                = r.CreatedAt,
        UpdatedAt                = r.UpdatedAt,
    };

    private static EstablecimientoResponse ToEstablecimientoResponse(ComercioEstablecimiento e) => new()
    {
        IdEstablecimiento     = e.IdEstablecimiento,
        IdComercioAliado      = e.IdComercioAliado,
        NombreEstablecimiento = e.NombreEstablecimiento,
        Direccion             = e.Direccion,
        Ciudad                = e.Ciudad,
        Telefono              = e.Telefono,
        Responsable           = e.Responsable,
        Estado                = e.Estado,
        CreatedAt             = e.CreatedAt,
        UpdatedAt             = e.UpdatedAt,
    };

    private static UsuarioSolicitadoResponse ToUsuarioSolicitadoResponse(ComercioUsuarioSolicitado u) => new()
    {
        IdUsuarioSolicitado = u.IdUsuarioSolicitado,
        IdComercioAliado    = u.IdComercioAliado,
        IdEstablecimiento   = u.IdEstablecimiento,
        IdUsuario           = u.IdUsuario,
        Nombres             = u.Nombres,
        Correo              = u.Correo,
        Celular             = u.Celular,
        RolSolicitado       = u.RolSolicitado,
        Estado              = u.Estado,
        CreatedAt           = u.CreatedAt,
        UpdatedAt           = u.UpdatedAt,
    };

    private static DocumentoResponse ToDocumentoResponse(ComercioDocumento d) => new()
    {
        IdDocumento           = d.IdDocumento,
        IdComercioAliado      = d.IdComercioAliado,
        TipoDocumento         = d.TipoDocumento,
        NombreArchivoOriginal = d.NombreArchivoOriginal,
        ContentType           = d.ContentType,
        SizeBytes             = d.SizeBytes,
        Estado                = d.Estado,
        Observaciones         = d.Observaciones,
        UploadedAt            = d.UploadedAt,
        UploadedByUsuario     = d.UploadedByUsuario,
    };
}
