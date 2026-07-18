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
    private long IdUsuarioActual => long.Parse(User.FindFirst("idUsuario")?.Value ?? "0");

    // ── ADMIN: Parámetros de utilización ──────────────────────────────
    [HttpGet("admin/parametros")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetParametros()
        => Ok(await svc.GetParametrosAsync());

    [HttpPut("admin/parametros/{tipo}")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> UpsertParametro(string tipo, [FromBody] UpsertParametroUtilizacionRequest req)
    {
        var tipos = new[] { "COMPRA_COMERCIO", "AVANCE_WALLET" };
        if (!tipos.Contains(tipo.ToUpperInvariant()))
            return BadRequest(new { error = "tipo_utilizacion debe ser COMPRA_COMERCIO o AVANCE_WALLET" });
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
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetGastosCobranza()
        => Ok(await svc.GetGastosCobranzaAsync());

    [HttpPost("admin/gastos-cobranza")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> CreateGastoCobranza([FromBody] UpsertGastosCobranzaRequest req)
        => Ok(await svc.UpsertGastoCobranzaAsync(null, req));

    [HttpPut("admin/gastos-cobranza/{id:long}")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> UpdateGastoCobranza(long id, [FromBody] UpsertGastosCobranzaRequest req)
    {
        try { return Ok(await svc.UpsertGastoCobranzaAsync(id, req)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // ── ADMIN: Política de crédito ─────────────────────────────────────
    [HttpGet("admin/politica")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetPolitica()
    {
        var politica = await svc.GetPoliticaVigenteAsync();
        return politica is null ? NotFound(new { error = "Sin política activa" }) : Ok(politica);
    }

    [HttpPut("admin/politica")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> UpsertPolitica([FromBody] UpsertPoliticaCreditoRequest req)
        => Ok(await svc.UpsertPoliticaAsync(req, IdUsuarioActual));

    // ── ADMIN: Cupos ──────────────────────────────────────────────────
    [HttpGet("admin/cupos")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> GetCupos()
        => Ok(await svc.GetCuposAsync());

    [HttpPost("admin/cupos")]
    [Authorize(Roles = "ADMIN_XPAY,SUPERUSUARIO")]
    public async Task<IActionResult> AsignarCupo([FromBody] AsignarCupoRequest req)
    {
        try { return Ok(await svc.AsignarCupoAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
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
        try { return Ok(await svc.SimularUtilizacionAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── USUARIO: Confirmación real de utilización (AVANCE_WALLET) ─────
    [HttpPost("confirmar-avance-wallet")]
    public async Task<IActionResult> ConfirmarAvanceWallet([FromBody] SimularUtilizacionRequest req)
    {
        try { return Ok(await svc.ConfirmarAvanceWalletAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── USUARIO: Mis créditos y pago manual de cuotas ──────────────────
    [HttpGet("mis-creditos")]
    public async Task<IActionResult> GetMisCreditos()
        => Ok(await svc.GetMisCreditosAsync(IdUsuarioActual));

    [HttpGet("mis-creditos/{idUtilizacion:long}/cuotas")]
    public async Task<IActionResult> GetCuotasCredito(long idUtilizacion)
    {
        try { return Ok(await svc.GetCuotasCreditoAsync(idUtilizacion, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpPost("pagar-cuota-wallet")]
    public async Task<IActionResult> PagarCuotaWallet([FromBody] PagarCuotaWalletRequest req)
    {
        try { return Ok(await svc.PagarCuotaWalletAsync(req, IdUsuarioActual)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ── CUALQUIER ROL AUTENTICADO: Parámetros públicos ────────────────
    [HttpGet("parametros/{tipo}")]
    public async Task<IActionResult> GetParametroPublico(string tipo)
    {
        var param = await svc.GetParametroByTipoAsync(tipo.ToUpperInvariant());
        return param is null ? NotFound() : Ok(param);
    }
}
