using Microsoft.EntityFrameworkCore;
using Xpay.Api.Data;
using Xpay.Api.DTOs;

namespace Xpay.Api.Services;

// Commit 1 — Catálogo geográfico (Fase 0, Motor de Evaluación de Crédito
// Datacrédito / onboarding móvil). Solo lectura, solo registros ACTIVO —
// ver database/031_catalogo_geografico.sql.
public class CatalogoGeograficoService(XpayDbContext db)
{
    public async Task<List<PaisResponse>> GetPaisesAsync()
    {
        return await db.CatalogoPaises
            .AsNoTracking()
            .Where(x => x.Estado == "ACTIVO")
            .OrderBy(x => x.Nombre)
            .Select(x => new PaisResponse(x.IdPais, x.Codigo, x.Nombre))
            .ToListAsync();
    }

    public async Task<List<DepartamentoResponse>> GetDepartamentosAsync(long idPais)
    {
        return await db.CatalogoDepartamentos
            .AsNoTracking()
            .Where(x => x.Estado == "ACTIVO" && x.IdPais == idPais)
            .OrderBy(x => x.Nombre)
            .Select(x => new DepartamentoResponse(x.IdDepartamento, x.CodigoDivipola, x.Nombre))
            .ToListAsync();
    }

    public async Task<List<CiudadResponse>> GetCiudadesAsync(long idDepartamento)
    {
        return await db.CatalogoCiudades
            .AsNoTracking()
            .Where(x => x.Estado == "ACTIVO" && x.IdDepartamento == idDepartamento)
            .OrderBy(x => x.Nombre)
            .Select(x => new CiudadResponse(x.IdCiudad, x.CodigoDivipola, x.Nombre, x.Tipo))
            .ToListAsync();
    }
}
