using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class LibranzaEmpleadosService
{
    private readonly XpayDbContext                   _db;
    private readonly ILogger<LibranzaEmpleadosService> _logger;

    private static readonly HashSet<string> TiposDoc       = ["CC", "CE", "NIT", "PASAPORTE", "OTRO"];
    private static readonly HashSet<string> Periodicidades  = ["MENSUAL", "QUINCENAL"];
    private static readonly HashSet<string> RolesEmpresa    = ["ADMIN_EMPRESA", "OPERADOR_EMPRESA", "CONSULTA_EMPRESA"];

    private const int MaxFilasImport = 500;
    private static readonly string[] PlantillaHeaders =
    [
        "tipo_documento", "numero_documento", "nombres", "apellidos",
        "celular", "correo", "cargo", "salario_mensual", "periodicidad_pago",
        "dia_pago_1", "dia_pago_2", "fecha_ingreso"
    ];

    public LibranzaEmpleadosService(XpayDbContext db, ILogger<LibranzaEmpleadosService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Autorización empresa ──────────────────────────────────────────────

    public async Task<LibranzaUsuarioEmpresa?> GetAsociacionAsync(long idUsuario)
    {
        return await _db.LibranzaUsuariosEmpresa
            .Where(u => u.IdUsuario == idUsuario && u.Estado == "ACTIVO")
            .FirstOrDefaultAsync();
    }

    // ── Mi convenio ───────────────────────────────────────────────────────

    public async Task<MiConvenioResponse> GetMiConvenioAsync(long idUsuario, long idConvenio)
    {
        var conv = await _db.LibranzaEmpresasConvenio.FindAsync(idConvenio)
            ?? throw new KeyNotFoundException($"Convenio {idConvenio} no encontrado.");

        var asoc = await _db.LibranzaUsuariosEmpresa
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario && u.IdConvenio == idConvenio && u.Estado == "ACTIVO")
            ?? throw new UnauthorizedAccessException("Sin acceso a este convenio.");

        var total   = await _db.LibranzaEmpleados.CountAsync(e => e.IdConvenio == idConvenio);
        var activos = await _db.LibranzaEmpleados.CountAsync(e => e.IdConvenio == idConvenio && e.Estado == "ACTIVO");

        return new MiConvenioResponse
        {
            IdConvenio           = conv.IdConvenio,
            NombreEmpresa        = conv.NombreEmpresa,
            Nit                  = conv.Nit,
            RepresentanteLegal   = conv.RepresentanteLegal,
            EmailContacto        = conv.EmailContacto,
            TelefonoContacto     = conv.TelefonoContacto,
            Estado               = conv.Estado,
            PeriodicidadPago     = conv.PeriodicidadPago,
            DiaPago1             = conv.DiaPago1,
            DiaPago2             = conv.DiaPago2,
            PorcentajeMaximoCupo = conv.PorcentajeMaximoCupo,
            TotalEmpleados       = total,
            EmpleadosActivos     = activos,
            RolEmpresa           = asoc.RolEmpresa,
        };
    }

    // ── Empleados (lista) ─────────────────────────────────────────────────

    public async Task<List<EmpleadoResponse>> GetEmpleadosAsync(long idConvenio)
    {
        return await _db.LibranzaEmpleados
            .Where(e => e.IdConvenio == idConvenio)
            .OrderBy(e => e.Apellidos).ThenBy(e => e.Nombres)
            .Select(e => ToEmpleadoResponse(e))
            .ToListAsync();
    }

    // ── Plantilla CSV ─────────────────────────────────────────────────────

    public byte[] GenerarPlantillaCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", PlantillaHeaders));
        sb.AppendLine("CC,1000099999,Juan,Pérez,3001234567,juan@demo.com,Auxiliar,2000000,MENSUAL,30,,2025-01-15");
        sb.AppendLine("CC,1000099998,María,López,3007654321,maria@demo.com,Analista,3000000,QUINCENAL,15,30,2024-08-01");
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return [..bom, ..body];
    }

    // ── Importar empleados ────────────────────────────────────────────────

    public async Task<ImportarEmpleadosResult> ImportarEmpleadosAsync(
        long    idConvenio,
        Stream  csvStream,
        string  nombreArchivo,
        long    operadorId)
    {
        // Get porcentaje_maximo_cupo from active params
        var param = await _db.LibranzaParametrosEmpresas
            .Where(p => p.IdConvenio == idConvenio && p.Estado == "ACTIVO")
            .OrderByDescending(p => p.IdParametro)
            .FirstOrDefaultAsync();
        var pctCupo = param?.PorcentajeMaximoCupo ?? 30m;

        var lote   = $"IMP-{idConvenio}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var errors = new List<ErrorFilaImportacion>();
        var filas  = new List<EmpleadoFilaParsed>();

        using var reader = new StreamReader(csvStream, Encoding.UTF8);
        var allLines = (await reader.ReadToEndAsync())
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        if (allLines.Length < 2)
            throw new InvalidOperationException("El archivo CSV debe tener al menos un encabezado y una fila de datos.");

        if (allLines.Length - 1 > MaxFilasImport)
            throw new InvalidOperationException($"El archivo excede el máximo de {MaxFilasImport} filas.");

        // Validate header
        var headers = ParseCsvLine(allLines[0]).Select(h => h.ToLowerInvariant().Trim()).ToArray();
        foreach (var req in PlantillaHeaders)
        {
            if (!headers.Contains(req))
                throw new InvalidOperationException($"El CSV no contiene la columna requerida: '{req}'.");
        }

        // Build column index map
        var idx = headers.Select((h, i) => (h, i)).ToDictionary(t => t.h, t => t.i);

        // Parse and validate rows
        for (int i = 1; i < allLines.Length; i++)
        {
            var cols = ParseCsvLine(allLines[i]);
            if (cols.Count < headers.Length)
            {
                errors.Add(new() { Fila = i + 1, Campo = "fila", Mensaje = "Columnas insuficientes." });
                continue;
            }

            string Get(string col) => cols[idx[col]].Trim();

            var fila = new EmpleadoFilaParsed { FilaNum = i + 1 };
            var rowOk = true;

            var tipoDoc = Get("tipo_documento").ToUpperInvariant();
            if (!TiposDoc.Contains(tipoDoc)) { errors.Add(new() { Fila = i + 1, Campo = "tipo_documento", Mensaje = $"Valor '{tipoDoc}' no válido." }); rowOk = false; }
            else fila.TipoDocumento = tipoDoc;

            var numDoc = Get("numero_documento");
            if (string.IsNullOrWhiteSpace(numDoc)) { errors.Add(new() { Fila = i + 1, Campo = "numero_documento", Mensaje = "Obligatorio." }); rowOk = false; }
            else if (numDoc.Length > 50) { errors.Add(new() { Fila = i + 1, Campo = "numero_documento", Mensaje = "Máximo 50 caracteres." }); rowOk = false; }
            else fila.NumeroDocumento = numDoc;

            var nombres = Get("nombres");
            if (string.IsNullOrWhiteSpace(nombres)) { errors.Add(new() { Fila = i + 1, Campo = "nombres", Mensaje = "Obligatorio." }); rowOk = false; }
            else fila.Nombres = nombres;

            fila.Apellidos = Get("apellidos").NullIfEmpty();
            fila.Celular   = Get("celular").NullIfEmpty();
            fila.Correo    = Get("correo").NullIfEmpty();
            fila.Cargo     = Get("cargo").NullIfEmpty();

            if (!decimal.TryParse(Get("salario_mensual"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sal) || sal <= 0)
            { errors.Add(new() { Fila = i + 1, Campo = "salario_mensual", Mensaje = "Debe ser un número mayor que 0." }); rowOk = false; }
            else fila.SalarioMensual = sal;

            var per = Get("periodicidad_pago").ToUpperInvariant();
            if (!Periodicidades.Contains(per)) { errors.Add(new() { Fila = i + 1, Campo = "periodicidad_pago", Mensaje = $"Valor '{per}' no válido (MENSUAL o QUINCENAL)." }); rowOk = false; }
            else fila.PeriodicidadPago = per;

            if (int.TryParse(Get("dia_pago_1"), out var dp1) && dp1 is >= 1 and <= 31) fila.DiaPago1 = dp1;
            if (int.TryParse(Get("dia_pago_2"), out var dp2) && dp2 is >= 1 and <= 31) fila.DiaPago2 = dp2;

            if (DateOnly.TryParse(Get("fecha_ingreso"), out var fi)) fila.FechaIngreso = fi;

            if (rowOk) filas.Add(fila);
        }

        // Persist valid rows
        int creados = 0, actualizados = 0;
        var now = DateTime.UtcNow;
        foreach (var f in filas)
        {
            var cupo = f.SalarioMensual * pctCupo / 100m;
            var existing = await _db.LibranzaEmpleados
                .FirstOrDefaultAsync(e => e.IdConvenio == idConvenio
                    && e.TipoDocumento == f.TipoDocumento
                    && e.NumeroDocumento == f.NumeroDocumento
                    && e.Estado == "ACTIVO");
            if (existing is null)
            {
                _db.LibranzaEmpleados.Add(new LibranzaEmpleado
                {
                    IdConvenio            = idConvenio,
                    TipoDocumento         = f.TipoDocumento,
                    NumeroDocumento       = f.NumeroDocumento,
                    Nombres               = f.Nombres,
                    Apellidos             = f.Apellidos,
                    Celular               = f.Celular,
                    Correo                = f.Correo,
                    Cargo                 = f.Cargo,
                    SalarioMensual        = f.SalarioMensual,
                    PeriodicidadPago      = f.PeriodicidadPago,
                    DiaPago1              = f.DiaPago1,
                    DiaPago2              = f.DiaPago2,
                    FechaIngreso          = f.FechaIngreso,
                    Estado                = "ACTIVO",
                    CupoPreliminar        = cupo,
                    FechaUltimoCalculoCupo = now,
                    OrigenCarga           = "EXCEL",
                    LoteImportacion       = lote,
                    CreatedAt             = now,
                    CreatedByUsuario      = operadorId,
                });
                creados++;
            }
            else
            {
                existing.Nombres               = f.Nombres;
                existing.Apellidos             = f.Apellidos;
                existing.Celular               = f.Celular;
                existing.Correo                = f.Correo;
                existing.Cargo                 = f.Cargo;
                existing.SalarioMensual        = f.SalarioMensual;
                existing.PeriodicidadPago      = f.PeriodicidadPago;
                existing.DiaPago1              = f.DiaPago1;
                existing.DiaPago2              = f.DiaPago2;
                existing.FechaIngreso          = f.FechaIngreso;
                existing.CupoPreliminar        = cupo;
                existing.FechaUltimoCalculoCupo = now;
                existing.OrigenCarga           = "EXCEL";
                existing.LoteImportacion       = lote;
                existing.UpdatedAt             = now;
                existing.UpdatedByUsuario      = operadorId;
                actualizados++;
            }
        }

        // Persist import audit record
        var estadoImp = errors.Count == 0 ? "PROCESADA"
            : filas.Count == 0 ? "ERROR"
            : "PROCESADA_CON_ERRORES";

        var importacion = new LibranzaImportacionEmpleados
        {
            IdConvenio           = idConvenio,
            NombreArchivo        = nombreArchivo,
            LoteImportacion      = lote,
            TotalFilas           = allLines.Length - 1,
            FilasValidas         = filas.Count,
            FilasError           = errors.Count,
            EmpleadosCreados     = creados,
            EmpleadosActualizados = actualizados,
            Estado               = estadoImp,
            ErroresJson          = errors.Count > 0 ? JsonSerializer.Serialize(errors) : null,
            CreatedAt            = now,
            CreatedByUsuario     = operadorId,
        };
        _db.LibranzaImportacionesEmpleados.Add(importacion);
        await _db.SaveChangesAsync();

        return new ImportarEmpleadosResult
        {
            TotalFilas            = importacion.TotalFilas,
            FilasValidas          = importacion.FilasValidas,
            FilasError            = importacion.FilasError,
            EmpleadosCreados      = importacion.EmpleadosCreados,
            EmpleadosActualizados = importacion.EmpleadosActualizados,
            LoteImportacion       = lote,
            Errores               = errors,
        };
    }

    // ── Admin: importaciones log ──────────────────────────────────────────

    public async Task<List<ImportacionResponse>> GetImportacionesAsync(long idConvenio)
    {
        var rows = await _db.LibranzaImportacionesEmpleados
            .Where(i => i.IdConvenio == idConvenio)
            .OrderByDescending(i => i.IdImportacion)
            .ToListAsync();

        return rows.Select(i => new ImportacionResponse
        {
            IdImportacion         = i.IdImportacion,
            IdConvenio            = i.IdConvenio,
            NombreArchivo         = i.NombreArchivo,
            LoteImportacion       = i.LoteImportacion,
            TotalFilas            = i.TotalFilas,
            FilasValidas          = i.FilasValidas,
            FilasError            = i.FilasError,
            EmpleadosCreados      = i.EmpleadosCreados,
            EmpleadosActualizados = i.EmpleadosActualizados,
            Estado                = i.Estado,
            Errores               = i.ErroresJson is not null
                ? JsonSerializer.Deserialize<List<ErrorFilaImportacion>>(i.ErroresJson)!
                : [],
            CreatedAt             = i.CreatedAt,
        }).ToList();
    }

    // ── Admin: usuarios empresa ───────────────────────────────────────────

    public async Task<List<UsuarioEmpresaResponse>> GetUsuariosEmpresaAsync(long idConvenio)
    {
        return await _db.LibranzaUsuariosEmpresa
            .Where(u => u.IdConvenio == idConvenio)
            .OrderBy(u => u.IdUsuarioEmpresa)
            .Select(u => new UsuarioEmpresaResponse
            {
                IdUsuarioEmpresa = u.IdUsuarioEmpresa,
                IdUsuario        = u.IdUsuario,
                IdConvenio       = u.IdConvenio,
                RolEmpresa       = u.RolEmpresa,
                Estado           = u.Estado,
                CreatedAt        = u.CreatedAt,
            })
            .ToListAsync();
    }

    public async Task<UsuarioEmpresaResponse> AsociarUsuarioEmpresaAsync(
        long idConvenio, AsociarUsuarioEmpresaRequest req, long adminId)
    {
        if (req.IdUsuario <= 0)
            throw new InvalidOperationException("idUsuario inválido.");
        if (!RolesEmpresa.Contains(req.RolEmpresa))
            throw new InvalidOperationException($"Rol empresa '{req.RolEmpresa}' no válido.");

        // Check convenio exists
        if (!await _db.LibranzaEmpresasConvenio.AnyAsync(c => c.IdConvenio == idConvenio))
            throw new KeyNotFoundException($"Convenio {idConvenio} no encontrado.");

        // Upsert: si ya existe, reactivar
        var existing = await _db.LibranzaUsuariosEmpresa
            .FirstOrDefaultAsync(u => u.IdUsuario == req.IdUsuario && u.IdConvenio == idConvenio);

        if (existing is not null)
        {
            existing.RolEmpresa       = req.RolEmpresa;
            existing.Estado           = "ACTIVO";
            existing.UpdatedAt        = DateTime.UtcNow;
            existing.UpdatedByUsuario = adminId;
            await _db.SaveChangesAsync();
            return new UsuarioEmpresaResponse
            {
                IdUsuarioEmpresa = existing.IdUsuarioEmpresa,
                IdUsuario        = existing.IdUsuario,
                IdConvenio       = existing.IdConvenio,
                RolEmpresa       = existing.RolEmpresa,
                Estado           = existing.Estado,
                CreatedAt        = existing.CreatedAt,
            };
        }

        var nuevo = new LibranzaUsuarioEmpresa
        {
            IdUsuario        = req.IdUsuario,
            IdConvenio       = idConvenio,
            RolEmpresa       = req.RolEmpresa,
            Estado           = "ACTIVO",
            CreatedAt        = DateTime.UtcNow,
            CreatedByUsuario = adminId,
        };
        _db.LibranzaUsuariosEmpresa.Add(nuevo);
        await _db.SaveChangesAsync();
        return new UsuarioEmpresaResponse
        {
            IdUsuarioEmpresa = nuevo.IdUsuarioEmpresa,
            IdUsuario        = nuevo.IdUsuario,
            IdConvenio       = nuevo.IdConvenio,
            RolEmpresa       = nuevo.RolEmpresa,
            Estado           = nuevo.Estado,
            CreatedAt        = nuevo.CreatedAt,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static EmpleadoResponse ToEmpleadoResponse(LibranzaEmpleado e) => new()
    {
        IdEmpleado       = e.IdEmpleado,
        IdConvenio       = e.IdConvenio,
        TipoDocumento    = e.TipoDocumento,
        NumeroDocumento  = e.NumeroDocumento,
        Nombres          = e.Nombres,
        Apellidos        = e.Apellidos,
        Celular          = e.Celular,
        Correo           = e.Correo,
        Cargo            = e.Cargo,
        SalarioMensual   = e.SalarioMensual,
        PeriodicidadPago = e.PeriodicidadPago,
        DiaPago1         = e.DiaPago1,
        DiaPago2         = e.DiaPago2,
        FechaIngreso     = e.FechaIngreso?.ToString("yyyy-MM-dd"),
        Estado           = e.Estado,
        CupoPreliminar   = e.CupoPreliminar,
        OrigenCarga      = e.OrigenCarga,
        LoteImportacion  = e.LoteImportacion,
        Observaciones    = e.Observaciones,
        CreatedAt        = e.CreatedAt,
        UpdatedAt        = e.UpdatedAt,
    };

    // Minimal CSV parser — handles double-quoted fields with embedded commas/quotes
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private sealed class EmpleadoFilaParsed
    {
        public int      FilaNum          { get; set; }
        public string   TipoDocumento    { get; set; } = string.Empty;
        public string   NumeroDocumento  { get; set; } = string.Empty;
        public string   Nombres          { get; set; } = string.Empty;
        public string?  Apellidos        { get; set; }
        public string?  Celular          { get; set; }
        public string?  Correo           { get; set; }
        public string?  Cargo            { get; set; }
        public decimal  SalarioMensual   { get; set; }
        public string   PeriodicidadPago { get; set; } = string.Empty;
        public int?     DiaPago1         { get; set; }
        public int?     DiaPago2         { get; set; }
        public DateOnly? FechaIngreso    { get; set; }
    }
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
