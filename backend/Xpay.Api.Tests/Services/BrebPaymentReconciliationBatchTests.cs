using Microsoft.Extensions.Logging.Abstractions;
using Xpay.Api.Integrations.Passport;
using Xpay.Api.Models;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Services;

// XPAY-381 FASE 5 — tests offline/puros de BrebPaymentReconciliationBatch.
// CERO DbContext real, CERO red — mismo criterio que
// BrebPaymentStateMachineTests/WalletReservationCalculatorTests: ningún
// test de este repositorio usa una base de datos real.
//
// Requisitos #1-6 (PROCESSING/PENDING retenido, SETTLED/REJECTED finalizan
// exactamente una vez, idempotencia ante repetición) ya están cubiertos por
// BrebPaymentStateMachineTests (Decide/Classify, sin cambios en XPAY-381) —
// esta clase prueba la capa de ORQUESTACIÓN del reconciliador (qué se llama,
// con qué argumentos, y el aislamiento de errores), no la decisión
// financiera en sí, que sigue viviendo exclusivamente en
// BrebPaymentStateMachine/ApplyPassportPaymentStatusAsync.
public class BrebPaymentReconciliationBatchTests
{
    private static PassportBrebRetiro Retiro(long id, string paymentId) => new()
    {
        IdBrebRetiro = id,
        Estado = "ENVIADO_PASSPORT",
        PassportPaymentId = paymentId,
    };

    private static PassportPaymentResponse Respuesta(string id, string status) => new()
    {
        Id = id,
        Status = status,
    };

    // Recorder de invocaciones a "aplicarEstado" — sustituye a
    // BrebPaymentService.ApplyPassportPaymentStatusAsync en el test; no
    // toca DB, sólo registra con qué argumentos fue llamado.
    private sealed class AplicarEstadoRecorder
    {
        public readonly List<(long IdBrebRetiro, string? PaymentId, string? Status)> Llamadas = new();

        public Task Aplicar(long idBrebRetiro, string? paymentId, string? status,
            PassportPaymentErrorResponse? error, CancellationToken ct)
        {
            Llamadas.Add((idBrebRetiro, paymentId, status));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProcesarLoteAsync_Settled_AplicaEstadoConStatusRecibido()
    {
        var client = new FakePassportPaymentClient(
            respuestas: new() { ["pay-1"] = Respuesta("pay-1", "SETTLED") });
        var recorder = new AplicarEstadoRecorder();

        var resultados = await BrebPaymentReconciliationBatch.ProcesarLoteAsync(
            new[] { Retiro(1, "pay-1") }, client, recorder.Aplicar, NullLogger.Instance, CancellationToken.None);

        Assert.True(resultados.Single().Exitoso);
        Assert.Equal(("pay-1", "SETTLED"), (recorder.Llamadas.Single().PaymentId, recorder.Llamadas.Single().Status));
    }

    [Fact]
    public async Task ProcesarLoteAsync_ProcessingYPending_AplicaEstadoSinCambiarSemantica()
    {
        var client = new FakePassportPaymentClient(respuestas: new()
        {
            ["pay-1"] = Respuesta("pay-1", "PROCESSING"),
            ["pay-2"] = Respuesta("pay-2", "PENDING"),
        });
        var recorder = new AplicarEstadoRecorder();

        var resultados = await BrebPaymentReconciliationBatch.ProcesarLoteAsync(
            new[] { Retiro(1, "pay-1"), Retiro(2, "pay-2") },
            client, recorder.Aplicar, NullLogger.Instance, CancellationToken.None);

        Assert.All(resultados, r => Assert.True(r.Exitoso));
        Assert.Contains(recorder.Llamadas, l => l is (1, "pay-1", "PROCESSING"));
        Assert.Contains(recorder.Llamadas, l => l is (2, "pay-2", "PENDING"));
    }

    // FASE 5 test #7 — una excepción de Passport (Retrieve) para un retiro
    // no debe llamar "aplicarEstado" para ESE retiro: nunca se toca Wallet
    // ante duda.
    [Fact]
    public async Task ProcesarLoteAsync_ExcepcionEnRetrieve_NuncaLlamaAplicarEstado()
    {
        var client = new FakePassportPaymentClient(
            excepciones: new() { ["pay-1"] = new PassportTransportException("timeout simulado") });
        var recorder = new AplicarEstadoRecorder();

        var resultados = await BrebPaymentReconciliationBatch.ProcesarLoteAsync(
            new[] { Retiro(1, "pay-1") }, client, recorder.Aplicar, NullLogger.Instance, CancellationToken.None);

        Assert.False(resultados.Single().Exitoso);
        Assert.Empty(recorder.Llamadas);
    }

    // FASE 5 test #8 — un retiro con error no bloquea a los demás del mismo
    // lote.
    [Fact]
    public async Task ProcesarLoteAsync_UnRetiroFalla_LosDemasSeProcesanIgual()
    {
        var client = new FakePassportPaymentClient(
            respuestas: new() { ["pay-2"] = Respuesta("pay-2", "SETTLED") },
            excepciones: new() { ["pay-1"] = new PassportTransportException("fallo simulado retiro 1") });
        var recorder = new AplicarEstadoRecorder();

        var resultados = await BrebPaymentReconciliationBatch.ProcesarLoteAsync(
            new[] { Retiro(1, "pay-1"), Retiro(2, "pay-2") },
            client, recorder.Aplicar, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, resultados.Count);
        Assert.False(resultados.Single(r => r.IdBrebRetiro == 1).Exitoso);
        Assert.True(resultados.Single(r => r.IdBrebRetiro == 2).Exitoso);
        Assert.Single(recorder.Llamadas); // solo el retiro 2 llegó a aplicarEstado
        Assert.Equal(2, recorder.Llamadas.Single().IdBrebRetiro);
    }

    // FASE 5 test #12 — el reconciliador NUNCA crea un Payment nuevo:
    // FakePassportPaymentClient.CreateBrebPaymentAsync lanza si se invoca;
    // si el lote entero corre sin que eso ocurra, queda probado.
    [Fact]
    public async Task ProcesarLoteAsync_NuncaInvocaCreateBrebPaymentAsync()
    {
        var client = new FakePassportPaymentClient(respuestas: new()
        {
            ["pay-1"] = Respuesta("pay-1", "SETTLED"),
            ["pay-2"] = Respuesta("pay-2", "REJECTED"),
        });
        var recorder = new AplicarEstadoRecorder();

        // Si ProcesarLoteAsync llamara CreateBrebPaymentAsync en cualquier
        // punto, el fake lanzaría NotImplementedException y este test
        // fallaría — no hace falta un contador explícito.
        var resultados = await BrebPaymentReconciliationBatch.ProcesarLoteAsync(
            new[] { Retiro(1, "pay-1"), Retiro(2, "pay-2") },
            client, recorder.Aplicar, NullLogger.Instance, CancellationToken.None);

        Assert.All(resultados, r => Assert.True(r.Exitoso));
        Assert.Equal(new[] { "pay-1", "pay-2" }, client.PaymentIdsConsultados);
    }

    [Fact]
    public async Task ProcesarLoteAsync_LoteVacio_NoLlamaNiClienteNiAplicarEstado()
    {
        var client = new FakePassportPaymentClient();
        var recorder = new AplicarEstadoRecorder();

        var resultados = await BrebPaymentReconciliationBatch.ProcesarLoteAsync(
            Array.Empty<PassportBrebRetiro>(), client, recorder.Aplicar, NullLogger.Instance, CancellationToken.None);

        Assert.Empty(resultados);
        Assert.Empty(client.PaymentIdsConsultados);
        Assert.Empty(recorder.Llamadas);
    }
}
