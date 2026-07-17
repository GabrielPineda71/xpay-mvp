using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xpay.Api.DTOs;
using Xpay.Api.Services;

namespace Xpay.Api.Controllers;

[ApiController]
[Route("api/cartera-ordinaria")]
[Authorize]
public class CarteraOrdinariaController(CarteraOrdinariaService svc) : ControllerBase
{
    private long IdUsuarioActual => long.Parse(User.FindFirst("sub")?.Value ?? "0");
    private bool EsAdmin => User.IsInRole("ADMIN") || User.IsInRole("OPERADOR");

    // ── ADMIN: Parámetros de utilización ──────────────────────────────
    [HttpGet("admin/parametros")]
    public async Task<IActionResult> GetParametros()
    {
        if (!EsAdmin) return Forbid();
        return Ok(await svc.GetParametrosAsync());
    }

    [HttpPut("admin/parametros/{tipo}")]
    public async Task<IActionResult> UpsertParametro(string tipo, [FromBody] UpsertParametroUtilizacionRequest req)
    {
        if (!EsAdmin) return Forbid();
        var tipos = new[] { "COMPRA_COMERCIO", "AVANCE_WALLET" };
        if (!tipos.Contains(tipo.ToUpperInvariant()))
            return BadRequest("tipo_utilizacion debe ser COMPRA_COMERCIO o AVANCE_WALLET");
        try
        {
            var result = await svc.UpsertParametroAsync(tipo.ToUpperInvariant(), req, IdUsuarioActual);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── ADMIN: Gastos de cobranza ─────────────────────────────────────
    [HttpGet("admin/gastos-cobranza")]
    public async Task<IActionResult> GetGastosCobranza()
    {
        if (!EsAdmin) return Forbid();
        return Ok(await svc.GetGastosCobranzaAsync());
    }

    [HttpPost("admin/gastos-cobranza")]
    public async Task<IActionResult> CreateGastoCobranza([FromBody] UpsertGastosCobranzaRequest req)
    {
        if (!EsAdmin) return Forbid();
        var result = await svc.UpsertGastoCobranzaAsync(null, req);
        return Ok(result);
    }

    [HttpPut("admin/gastos-cobranza/{id:long}")]
    public async Task<IActionResult> UpdateGastoCobranza(long id, [FromBody] UpsertGastosCobranzaRequest req)
    {
        if (!EsAdmin) return Forbid();
        try
        {
            var result = await svc.UpsertGastoCobranzaAsync(id, req);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ── ADMIN: Política de crédito ─────────────────────────────────────
    [HttpGet("admin/politica")]
    public async Task<IActionResult> GetPolitica()
    {
        if (!EsAdmin) return Forbid();
        var politica = await svc.GetPoliticaVigenteAsync();
        return politica is null ? NotFound() : Ok(politica);
    }

    [HttpPut("admin/politica")]
    public async Task<IActionResult> UpsertPolitica([FromBody] UpsertPoliticaCreditoRequest req)
    {
        if (!EsAdmin) return Forbid();
        var result = await svc.UpsertPoliticaAsync(req, IdUsuarioActual);
        return Ok(result);
    }

    // ── ADMIN: Cupos ──────────────────────────────────────────────────
    [HttpGet("admin/cupos")]
    public async Task<IActionResult> GetCupos()
    {
        if (!EsAdmin) return Forbid();
        return Ok(await svc.GetCuposAsync());
    }

    [HttpPost("admin/cupos")]
    public async Task<IActionResult> AsignarCupo([FromBody] AsignarCupoRequest req)
    {
        if (!EsAdmin) return Forbid();
        try
        {
            var result = await svc.AsignarCupoAsync(req, IdUsuarioActual);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── USUARIO: Mi cupo ──────────────────────────────────────────────
    [HttpGet("mi-cupo")]
    public async Task<IActionResult> GetMiCupo()
    {
        var cupo = await svc.GetMiCupoAsync(IdUsuarioActual);
        return cupo is null ? NotFound(new { error = "No tienes un cupo ordinario activo" }) : Ok(cupo);
    }

    // ── USUARIO: Simulador ────────────────────────────────────────────
    [HttpPost("simular")]
    public async Task<IActionResult> SimularUtilizacion([FromBody] SimularUtilizacionRequest req)
    {
        try
        {
            var result = await svc.SimularUtilizacionAsync(req, IdUsuarioActual);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── USUARIO/ADMIN: Parámetros públicos ────────────────────────────
    [HttpGet("parametros/{tipo}")]
    public async Task<IActionResult> GetParametroPublico(string tipo)
    {
        var param = await svc.GetParametroByTipoAsync(tipo.ToUpperInvariant());
        return param is null ? NotFound() : Ok(param);
    }
}
