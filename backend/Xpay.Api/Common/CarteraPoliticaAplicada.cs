namespace Xpay.Api.Common;

// XPAY-676B — copia de auditoría de la política que REALMENTE gobierna
// CarteraDecisionEngine. Fuente única: CarteraPolicyParameters.Vigente() (la
// working copy aprobada, auditada en XPAY-254). Se persiste en las columnas
// *_aplicado de cada solicitud nueva para que la auditoría refleje lo que el
// motor aplica, nunca los valores históricos de cartera_politicas_credito
// (seed de la migración 021, que no intervienen en la decisión).
//
//   - Cupo mínimo/máximo: CarteraPolicyParameters.CupoMinimo / CupoMaximo.
//   - Edad: el motor no la recibe como parámetro numérico; el gate 3 de
//     CarteraDecisionEngine acepta automáticamente las bandas 18-21 … 56-65
//     y envía 66+ a revisión manual (XPAY-190 / ACTA 001). Por eso se copian
//     18 y 65 como rango elegible automático; CarteraDecisionEngineTests ata
//     estas constantes al comportamiento real del gate.
//   - Score mínimo: el motor usa bandas de score, no un mínimo único → NULL.
public sealed record CarteraPoliticaAplicada(
    int?    ScoreDatacreditoMinimo,
    decimal CupoMinimo,
    decimal CupoMaximo,
    int     EdadMinimaElegible,
    int     EdadMaximaElegible)
{
    public const int EdadElegibleMinima = 18;
    public const int EdadElegibleMaxima = 65;

    public static CarteraPoliticaAplicada Desde(CarteraPolicyParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new CarteraPoliticaAplicada(
            ScoreDatacreditoMinimo: null,
            CupoMinimo:             p.CupoMinimo,
            CupoMaximo:             p.CupoMaximo,
            EdadMinimaElegible:     EdadElegibleMinima,
            EdadMaximaElegible:     EdadElegibleMaxima);
    }

    public static CarteraPoliticaAplicada Vigente() => Desde(CarteraPolicyParameters.Vigente());
}
