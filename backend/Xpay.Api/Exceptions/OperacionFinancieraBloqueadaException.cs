using Xpay.Api.Common;

namespace Xpay.Api.Exceptions;

// Piloto PROD — corrección 3: una operación que saca dinero nuevo (anticipo o
// retiro Bre-B real) fue bloqueada por ControlOperacionesFinancieras. Los
// controladores la traducen a 503 (pausa / configuración) o 400 (límite).
// No hereda de InvalidOperationException a propósito: ningún catch genérico
// existente debe absorberla.
public class OperacionFinancieraBloqueadaException(MotivoBloqueoFinanciero motivo, string message)
    : Exception(message)
{
    public MotivoBloqueoFinanciero Motivo { get; } = motivo;

    public bool EsLimite =>
        Motivo is MotivoBloqueoFinanciero.TopeOperacionUvb or MotivoBloqueoFinanciero.LimiteDiarioXpay;
}
