using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.Exceptions;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Common;

// Bloqueo financiero, fase visual — sin base de datos. El contexto usa una
// cadena ficticia que nunca llega a abrirse: cada bloqueo se lanza antes de
// cualquier acceso a la base.
public sealed class BloqueoFinancieroTests
{
    private const string CadenaFicticia = "Server=127.0.0.1,1;Database=bloqueo_ficticio;Connect Timeout=1";

    private static XpayDbContext Contexto(ControlOperacionesFinancieras? control = null)
    {
        var builder = new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(CadenaFicticia);
        if (control is not null) builder.AddInterceptors(new BloqueoFinancieroInterceptor(control));
        return new XpayDbContext(builder.Options);
    }

    // ── Detección de escrituras financieras ────────────────────────────────

    [Fact]
    public void Asientos_y_movimientos_nuevos_son_financieros()
    {
        using var a = Contexto(); a.LedgerTransacciones.Add(new LedgerTransaccion());
        using var b = Contexto(); b.LedgerMovimientos.Add(new LedgerMovimiento());
        using var c = Contexto(); c.WalletMovimientos.Add(new WalletMovimiento());
        Assert.True(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(a.ChangeTracker));
        Assert.True(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(b.ChangeTracker));
        Assert.True(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(c.ChangeTracker));
    }

    [Fact]
    public void Wallet_nueva_con_saldo_cero_no_es_financiera_pero_con_saldo_si()
    {
        using var cero = Contexto();
        cero.WalletSaldos.Add(new WalletSaldo { IdWallet = 1 });
        Assert.False(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(cero.ChangeTracker));

        using var conSaldo = Contexto();
        conSaldo.WalletSaldos.Add(new WalletSaldo { IdWallet = 1, SaldoDisponible = 1m });
        Assert.True(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(conSaldo.ChangeTracker));
    }

    [Fact]
    public void Cambio_de_saldo_es_financiero_pero_solo_la_fecha_no()
    {
        using var ctx = Contexto();
        var saldo = new WalletSaldo { IdWallet = 1, SaldoDisponible = 10m };
        ctx.WalletSaldos.Attach(saldo);

        saldo.FechaActualizacion = DateTime.UtcNow;
        Assert.False(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(ctx.ChangeTracker));

        saldo.SaldoDisponible = 20m;
        Assert.True(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(ctx.ChangeTracker));
    }

    [Fact]
    public void Cupos_y_utilizaciones_de_cartera_son_obligaciones()
    {
        using var nuevo = Contexto();
        nuevo.CarteraCuposOrdinarios.Add(new CarteraCupoOrdinario { IdUsuario = 1, IdWallet = 1, CupoAprobado = 100m });
        Assert.True(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(nuevo.ChangeTracker));

        using var existente = Contexto();
        var cupo = new CarteraCupoOrdinario { IdCupo = 1, IdUsuario = 1, IdWallet = 1, CupoAprobado = 100m };
        existente.CarteraCuposOrdinarios.Attach(cupo);
        cupo.Observaciones = "solo nota";
        Assert.False(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(existente.ChangeTracker));
        cupo.CupoUsado = 50m;
        Assert.True(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(existente.ChangeTracker));

        using var utilizacion = Contexto();
        utilizacion.CarteraUtilizaciones.Add(new CarteraUtilizacion());
        Assert.True(BloqueoFinancieroInterceptor.HayEscrituraFinanciera(utilizacion.ChangeTracker));
    }

    // ── El interceptor bloquea al guardar, antes de tocar la base ─────────

    [Fact]
    public async Task Interceptor_cerrado_rechaza_el_guardado_sin_abrir_conexion()
    {
        await using var ctx = Contexto(ControlesFinancierosPrueba.CerradoEnProduccion());
        ctx.LedgerTransacciones.Add(new LedgerTransaccion());

        var ex = await Assert.ThrowsAsync<OperacionFinancieraBloqueadaException>(() => ctx.SaveChangesAsync());
        Assert.Equal(MotivoBloqueoFinanciero.Pausa, ex.Motivo);
        Assert.Throws<OperacionFinancieraBloqueadaException>(() => ctx.SaveChanges());
    }

    // ── [OperacionFinanciera]: 503 antes de la action ──────────────────────

    [Fact]
    public void Atributo_cerrado_responde_503_y_no_deja_ejecutar()
    {
        var contexto = ContextoRecurso(ControlesFinancierosPrueba.CerradoEnProduccion());
        new OperacionFinancieraAttribute().OnResourceExecuting(contexto);

        var resultado = Assert.IsType<ObjectResult>(contexto.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, resultado.StatusCode);
    }

    [Fact]
    public void Atributo_sin_control_registrado_bloquea()
    {
        var contexto = ContextoRecurso(null);
        new OperacionFinancieraAttribute().OnResourceExecuting(contexto);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(contexto.Result).StatusCode);
    }

    [Fact]
    public void Atributo_habilitado_deja_pasar()
    {
        var abierto = ContextoRecurso(ControlesFinancierosPrueba.AbiertoEnProduccion());
        new OperacionFinancieraAttribute().OnResourceExecuting(abierto);
        Assert.Null(abierto.Result);

        var qa = ContextoRecurso(ControlesFinancierosPrueba.Habilitado());
        new OperacionFinancieraAttribute().OnResourceExecuting(qa);
        Assert.Null(qa.Result);
    }

    [Fact]
    public void Filtro_global_convierte_el_bloqueo_en_503()
    {
        var contexto = new ExceptionContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = new OperacionFinancieraBloqueadaException(MotivoBloqueoFinanciero.Pausa, "Operaciones financieras no disponibles en este momento."),
        };
        new OperacionFinancieraBloqueadaFilter().OnException(contexto);

        Assert.True(contexto.ExceptionHandled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(contexto.Result).StatusCode);
    }

    // ── Webhook de recaudo: no aplica nada con el interruptor cerrado ─────

    [Fact]
    public async Task Recaudo_cerrado_lanza_antes_de_consultar_la_base()
    {
        await using var ctx = Contexto();
        var svc = new LibranzaRecaudoAplicacionService(ctx, new ConfigurationBuilder().Build(),
            NullLogger<LibranzaRecaudoAplicacionService>.Instance, ControlesFinancierosPrueba.CerradoEnProduccion());

        // Si llegara a la base, fallaría con un error de conexión, no con el bloqueo.
        await Assert.ThrowsAsync<OperacionFinancieraBloqueadaException>(() =>
            svc.ProcesarEventoEntranteAsync(PassportInboundEventKind.Settled, "pago-sintetico", signatureValid: true, idEvento: 1));
    }

    [Fact]
    public async Task Recaudo_con_firma_invalida_sigue_sin_efecto_aunque_este_cerrado()
    {
        await using var ctx = Contexto();
        var svc = new LibranzaRecaudoAplicacionService(ctx, new ConfigurationBuilder().Build(),
            NullLogger<LibranzaRecaudoAplicacionService>.Instance, ControlesFinancierosPrueba.CerradoEnProduccion());

        var r = await svc.ProcesarEventoEntranteAsync(PassportInboundEventKind.Settled, "pago-sintetico", signatureValid: false, idEvento: 1);
        Assert.Equal(LibranzaRecaudoAplicacionService.ResFirmaNoValida, r.Resultado);
    }

    private static ResourceExecutingContext ContextoRecurso(ControlOperacionesFinancieras? control)
    {
        var servicios = new ServiceCollection();
        if (control is not null) servicios.AddSingleton(control);
        var http = new DefaultHttpContext { RequestServices = servicios.BuildServiceProvider() };
        return new ResourceExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new List<IValueProviderFactory>());
    }
}
