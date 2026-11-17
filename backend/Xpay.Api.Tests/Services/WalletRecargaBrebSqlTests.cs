using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Common;
using Xpay.Api.Data;
using Xpay.Api.DTOs;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xpay.Api.Tests.Common;
using Xunit;

namespace Xpay.Api.Tests.Services;

[Collection("SqlIntegration")]
public sealed class WalletRecargaBrebSqlTests
{
    private const string Cuenta = "11111111-1111-4111-8111-111111111111";
    private const string Cliente = "22222222-2222-4222-8222-222222222222";
    private const string Llave = "33333333-3333-4333-8333-333333333333";

    [Fact]
    public async Task Generar_qr_reutiliza_la_referencia_y_deja_el_valor_a_cargo_del_banco()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados, crearQr: false);
            var clienteQr = new ClienteQr();
            await using var db = Contexto(cs);
            var servicio = QrServicio(db, clienteQr);

            var primero = await servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario);
            var segundo = await servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario);

            Assert.Equal(primero.IdRecargaQr, segundo.IdRecargaQr);
            Assert.Equal(primero.Referencia, segundo.Referencia);
            Assert.Equal("ACTIVO", segundo.Estado);
            Assert.StartsWith("W", segundo.Referencia);
            Assert.Equal(17, segundo.Referencia.Length);
            var solicitud = Assert.Single(clienteQr.Solicitudes);
            Assert.Equal(PassportQrType.STATIC, solicitud.Type);
            Assert.Equal(Llave, solicitud.KeyId);
            Assert.Null(solicitud.Amount);
            Assert.Equal("0.00", solicitud.Vat!.VatValue);
            Assert.Equal("0.00", solicitud.Inc!.IncValue);
            Assert.Null(solicitud.Tip);
            await SinAbonoAsync(cs, wallet.IdWallet);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Cliente_real_serializa_el_qr_de_recarga_sin_propina_ni_monto()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados, crearQr: false);
            var transporte = new TransporteQr();
            await using var db = Contexto(cs);
            var servicio = QrServicio(db, new PassportQrClient(transporte));

            var resultado = await servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario);

            Assert.Equal("ACTIVO", resultado.Estado);
            using var json = JsonDocument.Parse(Assert.Single(transporte.Cuerpos));
            Assert.False(json.RootElement.TryGetProperty("tip", out _));
            Assert.False(json.RootElement.TryGetProperty("amount", out _));
            Assert.Equal("STATIC", json.RootElement.GetProperty("type").GetString());
            Assert.Equal("0.00", json.RootElement.GetProperty("vat").GetProperty("vat_value").GetString());
            Assert.Equal("0.00", json.RootElement.GetProperty("inc").GetProperty("inc_value").GetString());
            await SinAbonoAsync(cs, wallet.IdWallet);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Rechazo_400_libera_el_intento_y_permite_reintentar_la_misma_referencia()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados, crearQr: false);
            var cliente = new ClienteQr
            {
                ErrorInicial = new PassportTransportException("Solicitud rechazada.", 400, "invalid_parameter_value", null),
            };
            await using var db = Contexto(cs);
            var servicio = QrServicio(db, cliente);

            await Assert.ThrowsAsync<PassportTransportException>(() => servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario));
            var pendiente = await db.WalletRecargaBrebQrs.AsNoTracking().SingleAsync(q => q.IdWallet == wallet.IdWallet);
            Assert.Equal("PENDIENTE", pendiente.Estado);
            Assert.Null(pendiente.FechaIntento);

            var resultado = await servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario);

            Assert.Equal("ACTIVO", resultado.Estado);
            Assert.Equal(pendiente.Referencia, resultado.Referencia);
            Assert.Equal(2, cliente.Solicitudes.Count);
            Assert.All(cliente.Solicitudes, solicitud => Assert.Equal(pendiente.Referencia, solicitud.QrCodeReference));
            Assert.Equal(1, await db.WalletRecargaBrebQrs.CountAsync(q => q.IdWallet == wallet.IdWallet));
            await SinAbonoAsync(cs, wallet.IdWallet);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Fallo_antes_de_crear_el_qr_libera_el_intento_sin_llamar_a_creacion()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados, crearQr: false);
            var cliente = new ClienteQr();
            var claves = new ClaveHttp { ErrorInicial = new PassportTransportException("Consulta no disponible.") };
            await using var db = Contexto(cs);
            var servicio = QrServicio(db, cliente, claves);

            await Assert.ThrowsAsync<PassportTransportException>(() => servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario));

            Assert.Empty(cliente.Solicitudes);
            Assert.Equal("PENDIENTE", (await servicio.ObtenerAsync(wallet.IdPersona, wallet.IdUsuario))!.Estado);
            var resultado = await servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario);
            Assert.Equal("ACTIVO", resultado.Estado);
            Assert.Single(cliente.Solicitudes);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("408")]
    [InlineData("protocolo")]
    public async Task Creacion_con_resultado_incierto_conserva_el_intento_para_evitar_otra_creacion(string caso)
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados, crearQr: false);
            var cliente = new ClienteQr
            {
                ErrorInicial = caso switch
                {
                    "408" => new PassportTransportException("Sin confirmación.", 408, null, null),
                    "protocolo" => new PassportProtocolException("Respuesta no verificable."),
                    _ => new PassportTransportException("Sin respuesta."),
                },
            };
            await using var db = Contexto(cs);
            var servicio = QrServicio(db, cliente);

            Assert.IsAssignableFrom<PassportException>(await Record.ExceptionAsync(
                () => servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario)));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => servicio.GenerarAsync(wallet.IdPersona, wallet.IdUsuario));

            Assert.Contains("en curso", error.Message);
            Assert.Single(cliente.Solicitudes);
            var pendiente = await db.WalletRecargaBrebQrs.AsNoTracking().SingleAsync(q => q.IdWallet == wallet.IdWallet);
            Assert.Equal("GENERANDO", pendiente.Estado);
            Assert.NotNull(pendiente.FechaIntento);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Solicitud_simultanea_respeta_el_intento_que_ya_esta_creando_el_qr()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<WalletRecargaBrebQrResponse>? primera = null;
        try
        {
            var wallet = await SembrarAsync(cs, creados, crearQr: false);
            var cliente = new ClienteQr { AntesDeResponder = async () => { inicio.SetResult(); await continuar.Task; } };
            await using var db = Contexto(cs);
            primera = QrServicio(db, cliente).GenerarAsync(wallet.IdPersona, wallet.IdUsuario);
            await inicio.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var concurrente = Contexto(cs);
            var segundoCliente = new ClienteQr();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                QrServicio(concurrente, segundoCliente).GenerarAsync(wallet.IdPersona, wallet.IdUsuario));
            Assert.Empty(segundoCliente.Solicitudes);

            continuar.SetResult();
            Assert.Equal("ACTIVO", (await primera).Estado);
            Assert.Single(cliente.Solicitudes);
        }
        finally
        {
            continuar.TrySetResult();
            try { if (primera is not null) await primera; }
            finally { await LimpiarAsync(cs, creados); }
        }
    }

    [Fact]
    public async Task Consulta_de_qr_exige_que_la_persona_y_el_usuario_correspondan_a_la_misma_wallet()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var propio = await SembrarAsync(cs, creados);
            var ajeno = await SembrarAsync(cs, creados);
            await using var db = Contexto(cs);
            var servicio = QrServicio(db, new ClienteQr());

            await Assert.ThrowsAsync<KeyNotFoundException>(() => servicio.ObtenerAsync(propio.IdPersona, ajeno.IdUsuario));
            var resultado = await servicio.ObtenerAsync(propio.IdPersona, propio.IdUsuario);

            Assert.NotNull(resultado);
            Assert.Equal(propio.Referencia, resultado.Referencia);
            Assert.NotEqual(ajeno.Referencia, resultado.Referencia);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Webhook_liquidado_acredita_la_wallet_y_registra_el_banco_y_la_obligacion()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var pago = NuevoPago(creados);

            var resultado = await ProcesarAsync(cs, Evento(pago, wallet.Referencia, 25_000m));

            Assert.Equal(PassportWebhookProcessResult.Processed, resultado.Result);
            await using var db = Contexto(cs);
            var recarga = await db.WalletRecargasBreb.AsNoTracking().SingleAsync(r => r.IdWallet == wallet.IdWallet);
            Assert.Equal("APLICADA", recarga.Estado);
            Assert.Equal(pago, recarga.PassportPaymentId);
            Assert.Equal(25_000m, recarga.Valor);
            Assert.Equal(25_000m, await SaldoAsync(cs, wallet.IdWallet));

            var movimiento = await db.WalletMovimientos.AsNoTracking().SingleAsync(m => m.IdWallet == wallet.IdWallet);
            Assert.Equal(25_000m, movimiento.Valor);
            Assert.Equal(0m, movimiento.SaldoAntes);
            Assert.Equal(25_000m, movimiento.SaldoDespues);
            Assert.Equal(recarga.IdTransaccionLedger, movimiento.IdTransaccionLedger);
            var asiento = await db.LedgerTransacciones.SingleAsync(t => t.IdTransaccionLedger == recarga.IdTransaccionLedger);
            Assert.Equal(25_000m, asiento.ValorTotal);
            var lineas = await (from movimientoLedger in db.LedgerMovimientos.AsNoTracking()
                                join cuenta in db.LedgerCuentas.AsNoTracking() on movimientoLedger.IdCuenta equals cuenta.IdCuenta
                                where movimientoLedger.IdTransaccionLedger == recarga.IdTransaccionLedger
                                orderby cuenta.Codigo
                                select new { cuenta.Codigo, movimientoLedger.Naturaleza, movimientoLedger.Valor }).ToListAsync();
            Assert.Equal(new[] { ("110103", "D", 25_000m), ("210101", "C", 25_000m) },
                lineas.Select(l => (l.Codigo, l.Naturaleza, l.Valor)));
            Assert.True((await db.PassportWebhookEvents.SingleAsync(e => e.PassportPaymentId == pago)).Processed);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Reintentos_simultaneos_del_mismo_pago_acreditan_una_sola_vez()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var pago = NuevoPago(creados);
            var raw = Evento(pago, wallet.Referencia, 12_345m);

            await Task.WhenAll(ProcesarAsync(cs, raw), ProcesarAsync(cs, raw));
            await ProcesarAsync(cs, raw);

            await using var db = Contexto(cs);
            Assert.Equal(12_345m, await SaldoAsync(cs, wallet.IdWallet));
            Assert.Equal(1, await db.WalletRecargasBreb.CountAsync(r => r.IdWallet == wallet.IdWallet));
            Assert.Equal(1, await db.WalletMovimientos.CountAsync(m => m.IdWallet == wallet.IdWallet));
            Assert.Equal(1, await db.PassportWebhookEvents.CountAsync(e => e.PassportPaymentId == pago));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Dos_pagos_distintos_al_mismo_qr_acreditan_su_suma_sin_perder_actualizaciones()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var primero = Evento(NuevoPago(creados), wallet.Referencia, 10_000m);
            var segundo = Evento(NuevoPago(creados), wallet.Referencia, 17_000m);

            await Task.WhenAll(ProcesarAsync(cs, primero), ProcesarAsync(cs, segundo));

            await using var db = Contexto(cs);
            Assert.Equal(27_000m, await SaldoAsync(cs, wallet.IdWallet));
            var recargas = await db.WalletRecargasBreb.AsNoTracking().Where(r => r.IdWallet == wallet.IdWallet).ToListAsync();
            Assert.Equal(2, recargas.Count);
            Assert.Equal(2, recargas.Select(r => r.IdTransaccionLedger).Distinct().Count());
            Assert.Equal(2, await db.WalletMovimientos.CountAsync(m => m.IdWallet == wallet.IdWallet));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("sin_firma")]
    [InlineData("solo_confirmado")]
    [InlineData("rechazado")]
    [InlineData("otra_cuenta")]
    [InlineData("otra_moneda")]
    [InlineData("cero")]
    [InlineData("negativo")]
    [InlineData("precision")]
    [InlineData("referencia_ajena")]
    public async Task Entrega_sin_liquidacion_valida_no_crea_saldo_ni_asiento(string caso)
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var pago = NuevoPago(creados);
            var evento = caso switch
            {
                "solo_confirmado" => "payment.inbound.confirmed",
                "rechazado" => "payment.inbound.rejected",
                _ => "payment.inbound.settled",
            };
            var valor = caso switch { "cero" => 0m, "negativo" => -1m, "precision" => 100.001m, _ => 10_000m };
            var referencia = caso == "referencia_ajena" ? "WAAAAAAAAAAAAAAAA" : wallet.Referencia;
            var raw = Evento(pago, referencia, valor, evento,
                caso == "otra_cuenta" ? "otra-cuenta" : Cuenta, caso == "otra_moneda" ? "USD" : "COP");

            await ProcesarAsync(cs, raw, firmaValida: caso != "sin_firma");

            await SinAbonoAsync(cs, wallet.IdWallet);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Received_posterior_completa_el_settled_previo_y_aplica_una_sola_recarga()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var pago = NuevoPago(creados);
            var settled = JsonSerializer.Serialize(new { Event = "payment.inbound.settled", payment_id = pago, status = "SETTLED" });
            await RegistrarEventoAsync(cs, settled);

            await ProcesarAsync(cs, Evento(pago, wallet.Referencia, 21_000m, "payment.inbound.received"));

            await using var db = Contexto(cs);
            Assert.Equal(21_000m, await SaldoAsync(cs, wallet.IdWallet));
            Assert.Equal(1, await db.WalletRecargasBreb.CountAsync(r => r.IdWallet == wallet.IdWallet));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Settled_sin_datos_consulta_el_pago_autenticado_antes_de_acreditar()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var pago = NuevoPago(creados);
            var cliente = new FakePassportPaymentClient(new() { [pago] = new PassportPaymentResponse
            {
                Id = pago, Status = "SETTLED", Direction = "INBOUND", AccountId = Cuenta,
                QrCodeReference = LibranzaRecaudoReferencia.PrefijoPassportQr + wallet.Referencia,
                Amount = new PassportPaymentAmountResponse { Value = 19_000m, Currency = "COP" },
            } });
            var raw = JsonSerializer.Serialize(new { Event = "payment.inbound.settled", payment_id = pago, status = "SETTLED" });

            await ProcesarAsync(cs, raw, passport: cliente);

            Assert.Equal(19_000m, await SaldoAsync(cs, wallet.IdWallet));
            Assert.Equal(pago, Assert.Single(cliente.PaymentIdsConsultados));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Theory]
    [InlineData("PROCESSING")]
    [InlineData("SETTLED_SIN_MONTO")]
    public async Task Consulta_del_pago_retrasada_conserva_el_evento_para_un_reintento_exitoso(string estadoInicial)
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var pago = NuevoPago(creados);
            var raw = JsonSerializer.Serialize(new
            {
                Event = "payment.inbound.settled", payment_id = pago, status = "SETTLED",
                qr_code_reference = LibranzaRecaudoReferencia.PrefijoPassportQr + wallet.Referencia,
            });
            var pendiente = new FakePassportPaymentClient(new() { [pago] = new PassportPaymentResponse
            {
                Id = pago, Status = estadoInicial == "PROCESSING" ? "PROCESSING" : "SETTLED",
                Direction = "INBOUND", AccountId = Cuenta,
                QrCodeReference = LibranzaRecaudoReferencia.PrefijoPassportQr + wallet.Referencia,
                Amount = estadoInicial == "PROCESSING"
                    ? new PassportPaymentAmountResponse { Value = 16_000m, Currency = "COP" } : null,
            } });

            await Assert.ThrowsAsync<InvalidOperationException>(() => ProcesarAsync(cs, raw, passport: pendiente));

            await SinAbonoAsync(cs, wallet.IdWallet);
            await using (var db = Contexto(cs))
                Assert.False((await db.PassportWebhookEvents.SingleAsync(e => e.PassportPaymentId == pago)).Processed);
            var liquidado = new FakePassportPaymentClient(new() { [pago] = new PassportPaymentResponse
            {
                Id = pago, Status = "SETTLED", Direction = "INBOUND", AccountId = Cuenta,
                QrCodeReference = LibranzaRecaudoReferencia.PrefijoPassportQr + wallet.Referencia,
                Amount = new PassportPaymentAmountResponse { Value = 16_000m, Currency = "COP" },
            } });

            await ProcesarAsync(cs, raw, passport: liquidado);
            await ProcesarAsync(cs, raw, passport: liquidado);

            Assert.Equal(16_000m, await SaldoAsync(cs, wallet.IdWallet));
            await using var verificacion = Contexto(cs);
            Assert.Equal(1, await verificacion.WalletRecargasBreb.CountAsync(r => r.IdWallet == wallet.IdWallet));
            Assert.Equal(1, await verificacion.WalletMovimientos.CountAsync(m => m.IdWallet == wallet.IdWallet));
            Assert.Equal(1, await verificacion.PassportWebhookEvents.CountAsync(e => e.PassportPaymentId == pago));
            Assert.Equal(pago, Assert.Single(liquidado.PaymentIdsConsultados));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Datos_firmados_con_cuentas_contradictorias_quedan_sin_abono()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var pago = NuevoPago(creados);
            await ProcesarAsync(cs, Evento(pago, wallet.Referencia, 15_000m, "payment.inbound.received"));

            await ProcesarAsync(cs, Evento(pago, wallet.Referencia, 15_000m, cuenta: "otra-cuenta"));

            await SinAbonoAsync(cs, wallet.IdWallet);
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Pausa_financiera_conserva_el_evento_pendiente_para_reintentar_el_abono()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            var pago = NuevoPago(creados);
            var raw = Evento(pago, wallet.Referencia, 8_000m);

            Assert.NotNull(await Record.ExceptionAsync(() => ProcesarAsync(cs, raw,
                control: ControlesFinancierosPrueba.CerradoEnProduccion())));
            await SinAbonoAsync(cs, wallet.IdWallet);
            await using (var db = Contexto(cs))
                Assert.False((await db.PassportWebhookEvents.SingleAsync(e => e.PassportPaymentId == pago)).Processed);

            await ProcesarAsync(cs, raw);

            Assert.Equal(8_000m, await SaldoAsync(cs, wallet.IdWallet));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    [Fact]
    public async Task Wallet_bloqueada_conserva_el_pago_para_aplicarlo_al_habilitarla()
    {
        if (!TryConnection(out var cs)) return;
        var creados = new Creados();
        try
        {
            var wallet = await SembrarAsync(cs, creados);
            await using (var db = Contexto(cs))
                await db.Wallets.Where(w => w.IdWallet == wallet.IdWallet).ExecuteUpdateAsync(s => s.SetProperty(w => w.Estado, "BLOQUEADA"));
            var raw = Evento(NuevoPago(creados), wallet.Referencia, 6_000m);

            Assert.NotNull(await Record.ExceptionAsync(() => ProcesarAsync(cs, raw)));
            await SinAbonoAsync(cs, wallet.IdWallet);
            await using (var db = Contexto(cs))
                await db.Wallets.Where(w => w.IdWallet == wallet.IdWallet).ExecuteUpdateAsync(s => s.SetProperty(w => w.Estado, "ACTIVA"));

            await ProcesarAsync(cs, raw);

            Assert.Equal(6_000m, await SaldoAsync(cs, wallet.IdWallet));
        }
        finally { await LimpiarAsync(cs, creados); }
    }

    private sealed record Sembrado(long IdWallet, long IdPersona, long IdUsuario, string Referencia);

    private sealed class Creados
    {
        public List<long> Personas { get; } = [];
        public List<long> Usuarios { get; } = [];
        public List<long> Wallets { get; } = [];
        public List<string> Pagos { get; } = [];
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [PassportOptions.EnvOperationalAccountId] = Cuenta,
        [PassportOptions.EnvOperationalCustomerId] = Cliente,
        [PassportOptions.EnvCollectionKeyId] = Llave,
    }).Build();

    private static async Task<Sembrado> SembrarAsync(string cs, Creados creados, bool crearQr = true)
    {
        await using var db = Contexto(cs);
        var unidad = await db.Database.SqlQueryRaw<long>(
            "SELECT id_unidad_negocio AS Value FROM unidades_negocio WHERE codigo = {0}", "XPAY_COL").SingleAsync();
        var fecha = DateTime.UtcNow;
        var sufijo = Guid.NewGuid().ToString("N")[..12];
        var persona = new Persona { IdUnidadNegocio = unidad, PrimerNombre = "Recarga", PrimerApellido = "Sintetica",
            Pais = "Colombia", Estado = "ACTIVA", FechaCreacion = fecha };
        db.Personas.Add(persona);
        await db.SaveChangesAsync();
        creados.Personas.Add(persona.IdPersona);
        var usuario = new Usuario { IdPersona = persona.IdPersona, NombreUsuario = "recbreb_" + sufijo,
            PasswordHash = "x", Estado = "ACTIVO", FechaCreacion = fecha };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        creados.Usuarios.Add(usuario.IdUsuario);
        var wallet = new Wallet { IdPersona = persona.IdPersona, IdUnidadNegocio = unidad, TipoWallet = "PERSONA",
            NombreWallet = "Recarga sintetica " + sufijo, Estado = "ACTIVA", FechaCreacion = fecha };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        creados.Wallets.Add(wallet.IdWallet);
        db.WalletSaldos.Add(new WalletSaldo { IdWallet = wallet.IdWallet, FechaActualizacion = fecha });
        var referencia = "W" + LibranzaRecaudoReferencia.Generar();
        if (crearQr)
            db.WalletRecargaBrebQrs.Add(new WalletRecargaBrebQr
            {
                IdWallet = wallet.IdWallet, IdUsuario = usuario.IdUsuario, Referencia = referencia,
                Estado = "ACTIVO", PassportAccountId = Cuenta, PassportCustomerId = Cliente, PassportKeyId = Llave,
                PassportQrId = "qr-" + sufijo, QrCodeData = "000201010211" + sufijo,
                FechaCreacion = fecha, FechaGeneracion = fecha,
            });
        await db.SaveChangesAsync();
        return new Sembrado(wallet.IdWallet, persona.IdPersona, usuario.IdUsuario, referencia);
    }

    private static string NuevoPago(Creados creados)
    {
        var id = Guid.NewGuid().ToString("D");
        creados.Pagos.Add(id);
        return id;
    }

    private static string Evento(string pago, string referencia, decimal valor,
        string evento = "payment.inbound.settled", string cuenta = Cuenta, string moneda = "COP") =>
        JsonSerializer.Serialize(new
        {
            Event = evento, payment_id = pago, account_id = cuenta,
            status = evento == "payment.inbound.settled" ? "SETTLED" : evento == "payment.inbound.rejected" ? "REJECTED" : "PROCESSING",
            qr_code_reference = LibranzaRecaudoReferencia.PrefijoPassportQr + referencia,
            amount = new { value = valor.ToString(CultureInfo.InvariantCulture), currency = moneda },
        });

    private static async Task<PassportWebhookProcessOutcome> ProcesarAsync(string cs, string raw,
        bool firmaValida = true, ControlOperacionesFinancieras? control = null, IPassportPaymentClient? passport = null)
    {
        Assert.True(PassportWebhookPayloadParser.TryParse(raw, out var payload));
        await using var db = Contexto(cs);
        control ??= ControlesFinancierosPrueba.Habilitado();
        var recaudo = new LibranzaRecaudoAplicacionService(db, Config(), NullLogger<LibranzaRecaudoAplicacionService>.Instance, control);
        var recarga = new WalletRecargaBrebService(db, Config(), control,
            passport ?? new FakePassportPaymentClient(), NullLogger<WalletRecargaBrebService>.Instance);
        var webhook = new PassportWebhookService(db, TimeProvider.System, recaudo,
            NullLogger<PassportWebhookService>.Instance, recarga);
        return await webhook.ProcessAsync(raw, payload, firmaValida);
    }

    private static WalletRecargaBrebQrService QrServicio(XpayDbContext db, IPassportQrClient cliente, IPassportHttpClient? claves = null) => new(
        db, Config(), cliente, claves ?? new ClaveHttp(),
        new FakePassportCustomerAccountClient(new PassportAccountResponse { Id = Cuenta, CustomerId = Cliente, Status = "ACTIVE" }),
        ControlesFinancierosPrueba.Habilitado());

    private sealed class ClienteQr : IPassportQrClient
    {
        public List<PassportCreateQrCodeRequest> Solicitudes { get; } = [];
        public Exception? ErrorInicial { get; init; }
        public Func<Task>? AntesDeResponder { get; init; }
        public async Task<PassportQrCodeResponse> CreateQrCodeAsync(PassportCreateQrCodeRequest request, CancellationToken cancellationToken = default)
        {
            Solicitudes.Add(request);
            if (AntesDeResponder is not null) await AntesDeResponder();
            if (Solicitudes.Count == 1 && ErrorInicial is not null) throw ErrorInicial;
            return RespuestaQr(request);
        }

        public Task<PassportDecodeQrCodeResponse> DecodeQrCodeAsync(PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(PassportDecodeQrCodeResponse? Response, string? RawResponseJson)> DecodeQrCodeWithRawCaptureAsync(PassportDecodeQrCodeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ClaveHttp : IPassportHttpClient
    {
        public Exception? ErrorInicial { get; set; }
        public Task<TResponse?> GetAsync<TResponse>(string relativePath, CancellationToken cancellationToken = default)
        {
            Assert.Equal("/v1/keys/" + Llave, relativePath);
            if (ErrorInicial is { } error)
            {
                ErrorInicial = null;
                throw error;
            }
            return Task.FromResult((TResponse?)(object)new PassportKeyResponse { Id = Llave, AccountId = Cuenta, Status = "ACTIVE" });
        }

        public Task<TResponse?> PostAsync<TRequest, TResponse>(string relativePath, TRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(TResponse? Response, string? RawResponseJson)> PostWithRawCaptureAsync<TRequest, TResponse>(string relativePath, TRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResponse?> PatchAsync<TResponse>(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static PassportQrCodeResponse RespuestaQr(PassportCreateQrCodeRequest request) => new()
    {
        Id = Guid.NewGuid().ToString("D"), Status = "ACTIVE", Type = "STATIC", KeyId = request.KeyId,
        CustomerId = request.CustomerId,
        QrCodeReference = LibranzaRecaudoReferencia.PrefijoPassportQr + request.QrCodeReference,
        QrCodeData = "000201010211" + request.QrCodeReference,
    };

    private sealed class TransporteQr : IPassportHttpClient
    {
        public List<string> Cuerpos { get; } = [];
        public Task<TResponse?> PostAsync<TRequest, TResponse>(string relativePath, TRequest body, CancellationToken cancellationToken = default)
        {
            Assert.Equal("/v1/qrcodes", relativePath);
            var solicitud = Assert.IsType<PassportCreateQrCodeRequest>(body);
            Cuerpos.Add(JsonSerializer.Serialize(solicitud));
            return Task.FromResult((TResponse?)(object)RespuestaQr(solicitud));
        }

        public Task<TResponse?> GetAsync<TResponse>(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(TResponse? Response, string? RawResponseJson)> PostWithRawCaptureAsync<TRequest, TResponse>(string relativePath, TRequest body, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResponse?> PatchAsync<TResponse>(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task RegistrarEventoAsync(string cs, string raw)
    {
        Assert.True(PassportWebhookPayloadParser.TryParse(raw, out var payload));
        await using var db = Contexto(cs);
        db.PassportWebhookEvents.Add(new PassportWebhookEvent
        {
            EventType = payload.EventName, PassportPaymentId = payload.PaymentId,
            PayloadHash = PassportWebhookPayloadParser.ComputePayloadHash(raw),
            PayloadSanitizedJson = PassportWebhookPayloadParser.BuildSanitizedJson(payload), SignatureValid = true,
            FechaRecibido = DateTime.UtcNow, Processed = true,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<decimal> SaldoAsync(string cs, long idWallet)
    {
        await using var db = Contexto(cs);
        return await db.WalletSaldos.Where(s => s.IdWallet == idWallet).Select(s => s.SaldoDisponible).SingleAsync();
    }

    private static async Task SinAbonoAsync(string cs, long idWallet)
    {
        Assert.Equal(0m, await SaldoAsync(cs, idWallet));
        await using var db = Contexto(cs);
        Assert.False(await db.WalletRecargasBreb.AnyAsync(r => r.IdWallet == idWallet));
        Assert.False(await db.WalletMovimientos.AnyAsync(m => m.IdWallet == idWallet));
    }

    private static XpayDbContext Contexto(string cs) =>
        new(new DbContextOptionsBuilder<XpayDbContext>().UseSqlServer(cs).Options);

    private static bool TryConnection(out string cs)
    {
        cs = Environment.GetEnvironmentVariable("ConnectionStrings__XpayConnection") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(cs)) return true;
        Assert.False(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")),
            "ConnectionStrings__XpayConnection es obligatoria en CI para las pruebas de recarga Bre-B.");
        return false;
    }

    private static async Task LimpiarAsync(string cs, Creados creados)
    {
        await using var db = Contexto(cs);
        var asientos = await db.WalletRecargasBreb.Where(r => creados.Wallets.Contains(r.IdWallet))
            .Select(r => r.IdTransaccionLedger).ToListAsync();
        await db.WalletMovimientos.Where(m => creados.Wallets.Contains(m.IdWallet)).ExecuteDeleteAsync();
        await db.LedgerMovimientos.Where(m => asientos.Contains(m.IdTransaccionLedger)).ExecuteDeleteAsync();
        await db.WalletRecargasBreb.Where(r => creados.Wallets.Contains(r.IdWallet)).ExecuteDeleteAsync();
        await db.WalletRecargaBrebQrs.Where(q => creados.Wallets.Contains(q.IdWallet)).ExecuteDeleteAsync();
        await db.PassportWebhookEvents.Where(e => e.PassportPaymentId != null && creados.Pagos.Contains(e.PassportPaymentId)).ExecuteDeleteAsync();
        await db.LedgerTransacciones.Where(t => asientos.Contains(t.IdTransaccionLedger)).ExecuteDeleteAsync();
        await db.Auditorias.Where(a => a.IdUsuario != null && creados.Usuarios.Contains(a.IdUsuario.Value)).ExecuteDeleteAsync();
        await db.WalletSaldos.Where(s => creados.Wallets.Contains(s.IdWallet)).ExecuteDeleteAsync();
        await db.Wallets.Where(w => creados.Wallets.Contains(w.IdWallet)).ExecuteDeleteAsync();
        await db.Usuarios.Where(u => creados.Usuarios.Contains(u.IdUsuario)).ExecuteDeleteAsync();
        await db.Personas.Where(p => creados.Personas.Contains(p.IdPersona)).ExecuteDeleteAsync();
    }
}
