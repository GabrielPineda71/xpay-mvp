using Xpay.Api.Common;

namespace Xpay.Api.Authorization;

// XPAY-717 — evidencia KYC persistida que decide la policy "KycAprobado".
//   EstadoKycActual / IdentidadVerificada  → condición canónica (KYC-GATING-001).
//   ProveedorIdentidad                     → personas.identidad_verificada_proveedor
//                                            (quién consolidó la identidad).
//   FilasKycActuales / ProveedorKycActual / EstadoKycFilaActual
//                                          → fila(s) kyc_verificaciones con
//                                            es_actual = 1 del usuario (sesión vigente).
// Clase (no record) para no exponer ToString con datos: nunca debe loguearse.
public sealed class EvidenciaKyc
{
    public string? EstadoKycActual     { get; init; }
    public bool    IdentidadVerificada { get; init; }
    public string? ProveedorIdentidad  { get; init; }
    public int     FilasKycActuales    { get; init; }
    public string? ProveedorKycActual  { get; init; }
    public string? EstadoKycFilaActual { get; init; }
}

// XPAY-717 — regla pura de "KycAprobado". IAmbienteXpay.PermiteMecanismosNoProductivos
// es la ÚNICA autoridad de ambiente:
//   - No productivo (Development/QA/Test): semántica histórica intacta —
//     APROBADO + IdentidadVerificada basta (incluye SIMULACION_QA).
//   - Productivo / fail-closed: además exige identidad consolidada por VERIFF y
//     EXACTAMENTE UNA sesión vigente, de VERIFF y APROBADA. Cero o más de una
//     fila vigente no autoriza.
// Sin DB, configuración ni host.
public static class KycAprobadoRegla
{
    public const string ProveedorVeriff = "VERIFF";
    public const string Aprobado        = "APROBADO";

    public static bool Cumple(EvidenciaKyc evidencia, IAmbienteXpay ambiente)
    {
        ArgumentNullException.ThrowIfNull(evidencia);
        ArgumentNullException.ThrowIfNull(ambiente);

        if (!CumpleCondicionCanonica(evidencia))
            return false;

        if (ambiente.PermiteMecanismosNoProductivos)
            return true;

        return string.Equals(evidencia.ProveedorIdentidad,  ProveedorVeriff, StringComparison.Ordinal)
            && evidencia.FilasKycActuales == 1
            && string.Equals(evidencia.ProveedorKycActual,  ProveedorVeriff, StringComparison.Ordinal)
            && string.Equals(evidencia.EstadoKycFilaActual, Aprobado,        StringComparison.Ordinal);
    }

    // Condición canónica histórica: ambas son necesarias (ver KycAprobadoRequirement).
    internal static bool CumpleCondicionCanonica(EvidenciaKyc evidencia) =>
        string.Equals(evidencia.EstadoKycActual, Aprobado, StringComparison.Ordinal)
        && evidencia.IdentidadVerificada;
}
