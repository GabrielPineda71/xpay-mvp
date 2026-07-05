using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Models;

namespace Xpay.Api.Services;

public class LibranzaService
{
    private readonly XpayDbContext          _db;
    private readonly ILogger<LibranzaService> _logger;

    private static readonly HashSet<string> EstadosConvenio  = ["ACTIVO", "SUSPENDIDO", "CANCELADO"];
    private static readonly HashSet<string> Periodicidades   = ["MENSUAL", "QUINCENAL", "DECADAL"];
    private static readonly HashSet<string> MomentosCobro    = ["ANTICIPADO", "VENCIDO"];
    private static readonly HashSet<string> TiposCobro       = ["FIJO", "PORCENTAJE"];
    private static readonly HashSet<string> EstadosParam     = ["ACTIVO", "INACTIVO"];
    private static readonly HashSet<string> EstadosRango     = ["ACTIVO", "INACTIVO"];

    public LibranzaService(XpayDbContext db, ILogger<LibranzaService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── CONVENIOS ─────────────────────────────────────────────────────────

    public async Task<List<ConvenioResponse>> ListarConveniosAsync()
    {
        return await _db.LibranzaEmpresasConvenio
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => ToConvenioResponse(c))
            .ToListAsync();
    }

    public async Task<ConvenioResponse?> GetConvenioAsync(long id)
    {
        var c = await _db.LibranzaEmpresasConvenio.FindAsync(id);
        return c is null ? null : ToConvenioResponse(c);
    }

    public async Task<ConvenioResponse> CrearConvenioAsync(CrearConvenioRequest req, long adminId)
    {
        ValidarConvenioRequest(req.NombreEmpresa, req.Nit, req.Estado,
            req.PeriodicidadPago, req.PorcentajeMaximoCupo, req.DiaPago1, req.DiaPago2);

        if (await _db.LibranzaEmpresasConvenio.AnyAsync(c => c.Nit == req.Nit.Trim()))
            throw new InvalidOperationException($"Ya existe un convenio con NIT '{req.Nit}'.");

        ValidarDiasPagoSegunPeriodicidad(req.PeriodicidadPago, req.DiaPago1, req.DiaPago2, req.DiaPago3);

        var convenio = new LibranzaEmpresaConvenio
        {
            NombreEmpresa           = req.NombreEmpresa.Trim(),
            Nit                     = req.Nit.Trim(),
            RepresentanteLegal      = req.RepresentanteLegal?.Trim(),
            EmailContacto           = req.EmailContacto?.Trim(),
            TelefonoContacto        = req.TelefonoContacto?.Trim(),
            Direccion               = req.Direccion?.Trim(),
            Estado                  = req.Estado.Trim().ToUpperInvariant(),
            DiaPago1                = req.DiaPago1,
            DiaPago2                = req.DiaPago2,
            DiaPago3                = req.DiaPago3,
            PermiteAnticipodiaPago  = req.PermiteAnticipodiaPago,
            PeriodicidadPago        = req.PeriodicidadPago.Trim().ToUpperInvariant(),
            PorcentajeMaximoCupo    = req.PorcentajeMaximoCupo,
            Observaciones           = req.Observaciones?.Trim(),
            FechaInicio             = req.FechaInicio == default ? DateTime.UtcNow : req.FechaInicio,
            FechaFin                = req.FechaFin,
            CreatedAt               = DateTime.UtcNow,
            CreatedByUsuario        = adminId,
        };

        _db.LibranzaEmpresasConvenio.Add(convenio);
        await _db.SaveChangesAsync();
        _logger.LogInformation("LIBRANZA_CONVENIO_CREAR: id={Id} nit={Nit} admin={Admin}", convenio.IdConvenio, convenio.Nit, adminId);
        return ToConvenioResponse(convenio);
    }

    public async Task<ConvenioResponse> ActualizarConvenioAsync(long id, ActualizarConvenioRequest req, long adminId)
    {
        var convenio = await _db.LibranzaEmpresasConvenio.FindAsync(id)
            ?? throw new KeyNotFoundException($"Convenio {id} no encontrado.");

        if (!string.IsNullOrWhiteSpace(req.NombreEmpresa))
            convenio.NombreEmpresa = req.NombreEmpresa.Trim();

        if (!string.IsNullOrWhiteSpace(req.Estado))
        {
            var est = req.Estado.Trim().ToUpperInvariant();
            if (!EstadosConvenio.Contains(est))
                throw new InvalidOperationException($"Estado inválido: {est}. Valores: ACTIVO, SUSPENDIDO, CANCELADO.");
            convenio.Estado = est;
        }

        if (!string.IsNullOrWhiteSpace(req.PeriodicidadPago))
        {
            var per = req.PeriodicidadPago.Trim().ToUpperInvariant();
            if (!Periodicidades.Contains(per))
                throw new InvalidOperationException($"Periodicidad inválida: {per}. Valores: MENSUAL, QUINCENAL.");
            convenio.PeriodicidadPago = per;
        }

        if (req.PorcentajeMaximoCupo.HasValue)
        {
            if (req.PorcentajeMaximoCupo < 1 || req.PorcentajeMaximoCupo > 100)
                throw new InvalidOperationException("porcentaje_maximo_cupo debe estar entre 1 y 100.");
            convenio.PorcentajeMaximoCupo = req.PorcentajeMaximoCupo.Value;
        }

        if (req.DiaPago1.HasValue && (req.DiaPago1 < 1 || req.DiaPago1 > 31))
            throw new InvalidOperationException("dia_pago_1 debe estar entre 1 y 31.");
        if (req.DiaPago2.HasValue && (req.DiaPago2 < 1 || req.DiaPago2 > 31))
            throw new InvalidOperationException("dia_pago_2 debe estar entre 1 y 31.");
        if (req.DiaPago3.HasValue && (req.DiaPago3 < 1 || req.DiaPago3 > 31))
            throw new InvalidOperationException("dia_pago_3 debe estar entre 1 y 31.");

        convenio.RepresentanteLegal    = req.RepresentanteLegal?.Trim()  ?? convenio.RepresentanteLegal;
        convenio.EmailContacto         = req.EmailContacto?.Trim()       ?? convenio.EmailContacto;
        convenio.TelefonoContacto      = req.TelefonoContacto?.Trim()    ?? convenio.TelefonoContacto;
        convenio.Direccion             = req.Direccion?.Trim()            ?? convenio.Direccion;
        convenio.Observaciones         = req.Observaciones?.Trim()        ?? convenio.Observaciones;
        if (req.DiaPago1.HasValue)          convenio.DiaPago1               = req.DiaPago1;
        if (req.DiaPago2.HasValue)          convenio.DiaPago2               = req.DiaPago2;
        if (req.DiaPago3.HasValue)          convenio.DiaPago3               = req.DiaPago3;
        if (req.PermiteAnticipodiaPago.HasValue) convenio.PermiteAnticipodiaPago = req.PermiteAnticipodiaPago.Value;
        convenio.FechaFin              = req.FechaFin ?? convenio.FechaFin;
        convenio.UpdatedAt          = DateTime.UtcNow;
        convenio.UpdatedByUsuario   = adminId;

        await _db.SaveChangesAsync();
        _logger.LogInformation("LIBRANZA_CONVENIO_ACTUALIZAR: id={Id} admin={Admin}", id, adminId);
        return ToConvenioResponse(convenio);
    }

    // ── PARÁMETROS ────────────────────────────────────────────────────────

    public async Task<List<ParametrosResponse>> ListarParametrosAsync(long idConvenio)
    {
        if (!await _db.LibranzaEmpresasConvenio.AnyAsync(c => c.IdConvenio == idConvenio))
            throw new KeyNotFoundException($"Convenio {idConvenio} no encontrado.");

        return await _db.LibranzaParametrosEmpresas
            .Where(p => p.IdConvenio == idConvenio)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => ToParametrosResponse(p))
            .ToListAsync();
    }

    public async Task<ParametrosResponse> CrearParametrosAsync(long idConvenio, CrearParametrosRequest req, long adminId)
    {
        if (!await _db.LibranzaEmpresasConvenio.AnyAsync(c => c.IdConvenio == idConvenio))
            throw new KeyNotFoundException($"Convenio {idConvenio} no encontrado.");

        ValidarParametrosRequest(req.PorcentajeMaximoCupo, req.IvaPorcentaje,
            req.MaxAnticipacionesActivos, req.MomentoCobroComision, req.Estado);

        var param = new LibranzaParametrosEmpresa
        {
            IdConvenio                = idConvenio,
            PorcentajeMaximoCupo      = req.PorcentajeMaximoCupo,
            SalarioMinimoEmpleado     = req.SalarioMinimoEmpleado,
            SalarioMaximoEmpleado     = req.SalarioMaximoEmpleado,
            RequiereValidacionEmpresa = req.RequiereValidacionEmpresa,
            PermiteAnticipoMultiple   = req.PermiteAnticipoMultiple,
            MaxAnticipacionesActivos  = req.MaxAnticipacionesActivos,
            IvaPorcentaje             = req.IvaPorcentaje,
            MomentoCobroComision      = req.MomentoCobroComision.Trim().ToUpperInvariant(),
            Estado                    = req.Estado.Trim().ToUpperInvariant(),
            CreatedAt                 = DateTime.UtcNow,
            CreatedByUsuario          = adminId,
        };

        _db.LibranzaParametrosEmpresas.Add(param);
        await _db.SaveChangesAsync();
        _logger.LogInformation("LIBRANZA_PARAMETROS_CREAR: id={Id} convenio={Conv} admin={Admin}", param.IdParametro, idConvenio, adminId);
        return ToParametrosResponse(param);
    }

    public async Task<ParametrosResponse> ActualizarParametrosAsync(long idParametro, ActualizarParametrosRequest req, long adminId)
    {
        var param = await _db.LibranzaParametrosEmpresas.FindAsync(idParametro)
            ?? throw new KeyNotFoundException($"Parámetros {idParametro} no encontrados.");

        if (req.PorcentajeMaximoCupo.HasValue)
        {
            if (req.PorcentajeMaximoCupo < 1 || req.PorcentajeMaximoCupo > 100)
                throw new InvalidOperationException("porcentaje_maximo_cupo debe estar entre 1 y 100.");
            param.PorcentajeMaximoCupo = req.PorcentajeMaximoCupo.Value;
        }
        if (req.IvaPorcentaje.HasValue)
        {
            if (req.IvaPorcentaje < 0)
                throw new InvalidOperationException("iva_porcentaje no puede ser negativo.");
            param.IvaPorcentaje = req.IvaPorcentaje.Value;
        }
        if (req.MaxAnticipacionesActivos.HasValue)
        {
            if (req.MaxAnticipacionesActivos < 1)
                throw new InvalidOperationException("max_anticipos_activos debe ser >= 1.");
            param.MaxAnticipacionesActivos = req.MaxAnticipacionesActivos.Value;
        }
        if (!string.IsNullOrWhiteSpace(req.MomentoCobroComision))
        {
            var m = req.MomentoCobroComision.Trim().ToUpperInvariant();
            if (!MomentosCobro.Contains(m))
                throw new InvalidOperationException($"momento_cobro_comision inválido: '{m}'. Valores: ANTICIPADO, VENCIDO.");
            param.MomentoCobroComision = m;
        }
        if (!string.IsNullOrWhiteSpace(req.Estado))
        {
            var est = req.Estado.Trim().ToUpperInvariant();
            if (!EstadosParam.Contains(est))
                throw new InvalidOperationException($"Estado inválido: {est}. Valores: ACTIVO, INACTIVO.");
            param.Estado = est;
        }
        if (req.SalarioMinimoEmpleado.HasValue) param.SalarioMinimoEmpleado     = req.SalarioMinimoEmpleado;
        if (req.SalarioMaximoEmpleado.HasValue) param.SalarioMaximoEmpleado     = req.SalarioMaximoEmpleado;
        if (req.RequiereValidacionEmpresa.HasValue) param.RequiereValidacionEmpresa = req.RequiereValidacionEmpresa.Value;
        if (req.PermiteAnticipoMultiple.HasValue)   param.PermiteAnticipoMultiple   = req.PermiteAnticipoMultiple.Value;

        param.UpdatedAt          = DateTime.UtcNow;
        param.UpdatedByUsuario   = adminId;

        await _db.SaveChangesAsync();
        _logger.LogInformation("LIBRANZA_PARAMETROS_ACTUALIZAR: id={Id} convenio={Conv} admin={Admin}", idParametro, param.IdConvenio, adminId);
        return ToParametrosResponse(param);
    }

    // ── RANGOS DE COBRO ───────────────────────────────────────────────────

    public async Task<List<RangoCobroResponse>> ListarRangosAsync(long idConvenio)
    {
        if (!await _db.LibranzaEmpresasConvenio.AnyAsync(c => c.IdConvenio == idConvenio))
            throw new KeyNotFoundException($"Convenio {idConvenio} no encontrado.");

        return await _db.LibranzaRangosCobro
            .Where(r => r.IdConvenio == idConvenio)
            .OrderBy(r => r.ValorDesde)
            .Select(r => ToRangoResponse(r))
            .ToListAsync();
    }

    public async Task<RangoCobroResponse> CrearRangoAsync(long idConvenio, CrearRangoRequest req, long adminId)
    {
        if (!await _db.LibranzaEmpresasConvenio.AnyAsync(c => c.IdConvenio == idConvenio))
            throw new KeyNotFoundException($"Convenio {idConvenio} no encontrado.");

        ValidarRangoRequest(req.ValorDesde, req.ValorHasta, req.TipoCobro, req.ValorCobro, req.Estado);

        if (req.Estado.Trim().ToUpperInvariant() == "ACTIVO")
            await ValidarSolapamientoAsync(idConvenio, req.ValorDesde, req.ValorHasta, excludeId: null);

        var rango = new LibranzaRangoCobro
        {
            IdConvenio       = idConvenio,
            ValorDesde       = req.ValorDesde,
            ValorHasta       = req.ValorHasta,
            TipoCobro        = req.TipoCobro.Trim().ToUpperInvariant(),
            ValorCobro       = req.ValorCobro,
            AplicaIva        = req.AplicaIva,
            Estado           = req.Estado.Trim().ToUpperInvariant(),
            CreatedAt        = DateTime.UtcNow,
            CreatedByUsuario = adminId,
        };

        _db.LibranzaRangosCobro.Add(rango);
        await _db.SaveChangesAsync();
        _logger.LogInformation("LIBRANZA_RANGO_CREAR: id={Id} convenio={Conv} desde={D} hasta={H} admin={Admin}", rango.IdRango, idConvenio, req.ValorDesde, req.ValorHasta, adminId);
        return ToRangoResponse(rango);
    }

    public async Task<RangoCobroResponse> ActualizarRangoAsync(long idRango, ActualizarRangoRequest req, long adminId)
    {
        var rango = await _db.LibranzaRangosCobro.FindAsync(idRango)
            ?? throw new KeyNotFoundException($"Rango {idRango} no encontrado.");

        var nuevoDesde = req.ValorDesde ?? rango.ValorDesde;
        var nuevoHasta = req.ValorHasta ?? rango.ValorHasta;
        var nuevoEstado = (req.Estado?.Trim().ToUpperInvariant()) ?? rango.Estado;

        if (nuevoHasta <= nuevoDesde)
            throw new InvalidOperationException("valor_hasta debe ser mayor que valor_desde.");
        if (nuevoDesde < 0)
            throw new InvalidOperationException("valor_desde no puede ser negativo.");
        if (req.ValorCobro.HasValue && req.ValorCobro < 0)
            throw new InvalidOperationException("valor_cobro no puede ser negativo.");
        if (!string.IsNullOrWhiteSpace(req.TipoCobro) && !TiposCobro.Contains(req.TipoCobro.Trim().ToUpperInvariant()))
            throw new InvalidOperationException($"tipo_cobro inválido: '{req.TipoCobro}'. Valores: FIJO, PORCENTAJE.");
        if (!string.IsNullOrWhiteSpace(req.Estado) && !EstadosRango.Contains(nuevoEstado))
            throw new InvalidOperationException($"Estado inválido: {nuevoEstado}. Valores: ACTIVO, INACTIVO.");

        if (nuevoEstado == "ACTIVO")
            await ValidarSolapamientoAsync(rango.IdConvenio, nuevoDesde, nuevoHasta, excludeId: idRango);

        rango.ValorDesde      = nuevoDesde;
        rango.ValorHasta      = nuevoHasta;
        rango.Estado          = nuevoEstado;
        if (!string.IsNullOrWhiteSpace(req.TipoCobro)) rango.TipoCobro = req.TipoCobro.Trim().ToUpperInvariant();
        if (req.ValorCobro.HasValue) rango.ValorCobro = req.ValorCobro.Value;
        if (req.AplicaIva.HasValue)  rango.AplicaIva  = req.AplicaIva.Value;
        rango.UpdatedAt        = DateTime.UtcNow;
        rango.UpdatedByUsuario = adminId;

        await _db.SaveChangesAsync();
        _logger.LogInformation("LIBRANZA_RANGO_ACTUALIZAR: id={Id} convenio={Conv} admin={Admin}", idRango, rango.IdConvenio, adminId);
        return ToRangoResponse(rango);
    }

    // ── Helpers de validación ─────────────────────────────────────────────

    private static void ValidarConvenioRequest(string nombre, string nit, string estado,
        string periodicidad, decimal cupo, int? dia1, int? dia2)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new InvalidOperationException("nombre_empresa es obligatorio.");
        if (string.IsNullOrWhiteSpace(nit))
            throw new InvalidOperationException("nit es obligatorio.");
        if (!EstadosConvenio.Contains(estado.Trim().ToUpperInvariant()))
            throw new InvalidOperationException($"estado inválido: '{estado}'. Valores: ACTIVO, SUSPENDIDO, CANCELADO.");
        if (!Periodicidades.Contains(periodicidad.Trim().ToUpperInvariant()))
            throw new InvalidOperationException($"periodicidad_pago inválida: '{periodicidad}'. Valores: MENSUAL, QUINCENAL, DECADAL.");
        if (cupo < 1 || cupo > 100)
            throw new InvalidOperationException("porcentaje_maximo_cupo debe estar entre 1 y 100.");
        if (dia1.HasValue && (dia1 < 1 || dia1 > 31))
            throw new InvalidOperationException("dia_pago_1 debe estar entre 1 y 31.");
        if (dia2.HasValue && (dia2 < 1 || dia2 > 31))
            throw new InvalidOperationException("dia_pago_2 debe estar entre 1 y 31.");
    }

    private static void ValidarDiasPagoSegunPeriodicidad(string periodicidad, int? dia1, int? dia2, int? dia3)
    {
        var per = periodicidad.Trim().ToUpperInvariant();
        if (per == "QUINCENAL" && (!dia1.HasValue || !dia2.HasValue))
            throw new InvalidOperationException("QUINCENAL requiere dia_pago_1 y dia_pago_2.");
        if (per == "DECADAL" && (!dia1.HasValue || !dia2.HasValue || !dia3.HasValue))
            throw new InvalidOperationException("DECADAL requiere dia_pago_1, dia_pago_2 y dia_pago_3.");
        var dias = new[] { dia1, dia2, dia3 }.Where(d => d.HasValue).Select(d => d!.Value).ToList();
        if (dias.Distinct().Count() != dias.Count)
            throw new InvalidOperationException("Los días de pago no deben repetirse.");
    }

    private static void ValidarParametrosRequest(decimal cupo, decimal iva, int maxAnt, string momento, string estado)
    {
        if (cupo < 1 || cupo > 100)
            throw new InvalidOperationException("porcentaje_maximo_cupo debe estar entre 1 y 100.");
        if (iva < 0)
            throw new InvalidOperationException("iva_porcentaje no puede ser negativo.");
        if (maxAnt < 1)
            throw new InvalidOperationException("max_anticipos_activos debe ser >= 1.");
        if (!MomentosCobro.Contains(momento.Trim().ToUpperInvariant()))
            throw new InvalidOperationException($"momento_cobro_comision inválido: '{momento}'. Valores: ANTICIPADO, VENCIDO.");
        if (!EstadosParam.Contains(estado.Trim().ToUpperInvariant()))
            throw new InvalidOperationException($"estado inválido: '{estado}'. Valores: ACTIVO, INACTIVO.");
    }

    private static void ValidarRangoRequest(decimal desde, decimal hasta, string tipo, decimal cobro, string estado)
    {
        if (desde < 0)
            throw new InvalidOperationException("valor_desde no puede ser negativo.");
        if (hasta <= desde)
            throw new InvalidOperationException("valor_hasta debe ser mayor que valor_desde.");
        if (cobro < 0)
            throw new InvalidOperationException("valor_cobro no puede ser negativo.");
        if (!TiposCobro.Contains(tipo.Trim().ToUpperInvariant()))
            throw new InvalidOperationException($"tipo_cobro inválido: '{tipo}'. Valores: FIJO, PORCENTAJE.");
        if (!EstadosRango.Contains(estado.Trim().ToUpperInvariant()))
            throw new InvalidOperationException($"estado inválido: '{estado}'. Valores: ACTIVO, INACTIVO.");
    }

    private async Task ValidarSolapamientoAsync(long idConvenio, decimal desde, decimal hasta, long? excludeId)
    {
        var solapado = await _db.LibranzaRangosCobro.AnyAsync(r =>
            r.IdConvenio == idConvenio &&
            r.Estado     == "ACTIVO"   &&
            (excludeId == null || r.IdRango != excludeId) &&
            r.ValorDesde < hasta &&
            r.ValorHasta > desde);

        if (solapado)
            throw new InvalidOperationException(
                $"El rango [{desde:0.00}–{hasta:0.00}] se solapa con un rango activo existente del convenio.");
    }

    // ── Proyecciones ──────────────────────────────────────────────────────

    private static ConvenioResponse ToConvenioResponse(LibranzaEmpresaConvenio c) => new()
    {
        IdConvenio              = c.IdConvenio,
        NombreEmpresa           = c.NombreEmpresa,
        Nit                     = c.Nit,
        RepresentanteLegal      = c.RepresentanteLegal,
        EmailContacto           = c.EmailContacto,
        TelefonoContacto        = c.TelefonoContacto,
        Direccion               = c.Direccion,
        Estado                  = c.Estado,
        DiaPago1                = c.DiaPago1,
        DiaPago2                = c.DiaPago2,
        DiaPago3                = c.DiaPago3,
        PermiteAnticipodiaPago  = c.PermiteAnticipodiaPago,
        PeriodicidadPago        = c.PeriodicidadPago,
        PorcentajeMaximoCupo    = c.PorcentajeMaximoCupo,
        Observaciones           = c.Observaciones,
        FechaInicio             = c.FechaInicio,
        FechaFin                = c.FechaFin,
        CreatedAt               = c.CreatedAt,
        UpdatedAt               = c.UpdatedAt,
    };

    private static ParametrosResponse ToParametrosResponse(LibranzaParametrosEmpresa p) => new()
    {
        IdParametro               = p.IdParametro,
        IdConvenio                = p.IdConvenio,
        PorcentajeMaximoCupo      = p.PorcentajeMaximoCupo,
        SalarioMinimoEmpleado     = p.SalarioMinimoEmpleado,
        SalarioMaximoEmpleado     = p.SalarioMaximoEmpleado,
        RequiereValidacionEmpresa = p.RequiereValidacionEmpresa,
        PermiteAnticipoMultiple   = p.PermiteAnticipoMultiple,
        MaxAnticipacionesActivos  = p.MaxAnticipacionesActivos,
        IvaPorcentaje             = p.IvaPorcentaje,
        MomentoCobroComision      = p.MomentoCobroComision,
        Estado                    = p.Estado,
        CreatedAt                 = p.CreatedAt,
        UpdatedAt                 = p.UpdatedAt,
    };

    private static RangoCobroResponse ToRangoResponse(LibranzaRangoCobro r) => new()
    {
        IdRango    = r.IdRango,
        IdConvenio = r.IdConvenio,
        ValorDesde = r.ValorDesde,
        ValorHasta = r.ValorHasta,
        TipoCobro  = r.TipoCobro,
        ValorCobro = r.ValorCobro,
        AplicaIva  = r.AplicaIva,
        Estado     = r.Estado,
        CreatedAt  = r.CreatedAt,
        UpdatedAt  = r.UpdatedAt,
    };
}
