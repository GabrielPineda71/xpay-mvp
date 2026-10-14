namespace Xpay.Api.Integrations.MiDecisor;

// XPAY-702 - lecturas que necesita el gate QA en modo QA_CLIENT_FULL_FLOW.
// Solo lectura. No devuelve ni registra PII.
public interface IQaClienteRiesgoStore
{
    // true SOLO si el usuario tiene la doble marca persistente de cliente QA:
    //   - persona.IdentidadVerificada == true y
    //     persona.IdentidadVerificadaProveedor == "SIMULACION_QA"
    //     (solo lo escribe QaIdentidadPruebaStore);
    //   - fila kyc_verificaciones es_actual con proveedor "SIMULACION_QA";
    //   - ninguna fila kyc_verificaciones con proveedor "VERIFF";
    //   - usuario.EstadoKycActual == "APROBADO".
    Task<bool> EsClienteQaPreparadoAsync(long idUsuario, CancellationToken cancellationToken = default);

    // Numero de intentos de consulta del usuario que ya salieron de PRE_CALL
    // (ENVIO_INCIERTO o FINALIZADO), en todas sus solicitudes. Una reanudacion
    // que reutiliza el resultado persistido no crea intentos nuevos.
    Task<int> ContarConsultasRealesAsync(long idUsuario, CancellationToken cancellationToken = default);
}
