using Xpay.Api.Common;
using Xpay.Api.Controllers;
using Xpay.Api.Services;
using Xunit;

namespace Xpay.Api.Tests.Controllers;

// XPAY-656 — mensaje de IniciarEvaluacion por resultado del orquestador, vía el
// seam interno CarteraOrdinariaController.MensajeIniciarEvaluacion (sin
// construir el controller). SIN red, SIN base de datos, SIN datos personales.
public sealed class CarteraOrdinariaControllerMensajeTests
{
    private static OrquestacionEvaluacionResultado Res(
        OrquestacionEvaluacionEstado estado, string? estadoSolicitud = null)
        => new(estado, estadoSolicitud, CarteraDecisionCrediticia.Pendiente, null, null, null);

    [Fact]
    public void DetenidaTrasConsulta_mensajeTecnicoNeutro()
    {
        var mensaje = CarteraOrdinariaController.MensajeIniciarEvaluacion(
            Res(OrquestacionEvaluacionEstado.DetenidaTrasConsulta, CarteraSolicitudCupoEstados.EnEvaluacion));

        Assert.Equal(
            "Evaluación detenida después de la consulta de riesgo y antes de la decisión crediticia.",
            mensaje);
        Assert.DoesNotContain("aprob", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cupo", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("score", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ACCEPTED", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"\d", mensaje);
    }

    // La extracción del switch no altera los mensajes existentes.
    [Theory]
    [InlineData(OrquestacionEvaluacionEstado.AutorizacionNoDisponible,
        "No pudimos completar la evaluación. La solicitud requiere revisión.")]
    [InlineData(OrquestacionEvaluacionEstado.DatosPersonaInsuficientes,
        "No pudimos completar la evaluación con la información disponible. La solicitud requiere revisión.")]
    [InlineData(OrquestacionEvaluacionEstado.RequiereReconciliacion,
        "La evaluación quedó en un estado que requiere revisión.")]
    [InlineData(OrquestacionEvaluacionEstado.Procesada, "Evaluación procesada.")]
    public void ResultadosExistentes_conservanMensaje(OrquestacionEvaluacionEstado estado, string esperado)
    {
        Assert.Equal(esperado, CarteraOrdinariaController.MensajeIniciarEvaluacion(Res(estado)));
    }

    [Fact]
    public void RevisionManual_conservaMensaje()
    {
        var mensaje = CarteraOrdinariaController.MensajeIniciarEvaluacion(
            Res(OrquestacionEvaluacionEstado.Procesada, CarteraSolicitudCupoEstados.PendienteRevisionManual));

        Assert.Equal("Tu solicitud pasó a revisión manual.", mensaje);
    }
}
