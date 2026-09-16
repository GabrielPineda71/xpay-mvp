namespace Xpay.Api.DTOs;

public class RegistrarLlaveRequest
{
    public string KeyType  { get; set; } = string.Empty; // ID / PHONE / EMAIL / ALPHA / BCODE
    public string KeyValue { get; set; } = string.Empty;
    public long?  IdComercio { get; set; }               // solo para contexto COMERCIO
}

public class SimularValidacionLlaveRequest
{
    public long   IdBrebLlave { get; set; }
    public string Estado      { get; set; } = string.Empty; // VALIDADA / RECHAZADA
    public string? Motivo     { get; set; }
}

public class SimularRetiroRequest
{
    public decimal Valor       { get; set; }
    public long?   IdComercio  { get; set; } // solo para contexto COMERCIO
}

public class MiLlaveResponse
{
    public long    IdBrebLlave    { get; set; }
    public string  TipoSujeto     { get; set; } = string.Empty;
    public string  KeyType        { get; set; } = string.Empty;
    public string  KeyValueMasked { get; set; } = string.Empty;
    public string  Estado         { get; set; } = string.Empty;
    public DateTime? FechaRegistro   { get; set; }
    public DateTime? FechaValidacion { get; set; }

    // XPAY-371 — distingue una llave VALIDADA por resolución real de
    // Passport (POST /v1/resolve-key, ver BrebKeyResolutionResponseMapper)
    // de una VALIDADA sólo por el botón admin QA
    // (POST /api/breb/admin/simular-validacion-llave). No requiere columna
    // nueva: se deriva de la presencia de campos que SÓLO una resolución
    // real puebla (OwnerNameMasked). Ver BrebService.ToLlaveResponse.
    public bool    ResolucionVerificadaPassport { get; set; }
}

// XPAY-371 — request para POST /api/breb/mi-llave/resolver. KeyValue aquí
// NO es "una llave arbitraria del frontend" (prohibido) — es la
// reconfirmación del valor que el usuario YA registró; se valida contra el
// hash almacenado de la llave activa de su propia Wallet ANTES de llamar a
// Passport (ver BrebKeyResolutionRequestBuilder). Sin este campo no habría
// forma de reconstruir el key_value en claro, porque PassportBrebLlave
// nunca lo persiste (sólo hash + máscara).
public class ResolverLlaveRequest
{
    public string KeyValue { get; set; } = string.Empty;
}

// XPAY-371 FASE 4 — contrato sanitizado para que UserWalletPage.tsx pueda
// mostrar "Esta es la cuenta asociada a tu llave Bre-B" antes de confirmar
// un retiro. Implementación de la UI queda fuera de alcance de XPAY-371
// (backend solamente) — este DTO es el contrato que la consumirá.
public class MiLlaveResolveResponse
{
    public long    IdBrebLlave                  { get; set; }
    public string  KeyType                      { get; set; } = string.Empty;
    public string  KeyValueMasked               { get; set; } = string.Empty;
    public string  Estado                       { get; set; } = string.Empty;
    public bool    ResolucionVerificadaPassport  { get; set; }
    public string  TitularNombreMasked          { get; set; } = string.Empty;
    public string? TitularIdentificacionTipo    { get; set; }
    public string? TitularIdentificacionMasked  { get; set; }
    public string? EntidadFinanciera            { get; set; }
    public string? TipoCuenta                   { get; set; }
    public string? CuentaMasked                 { get; set; }
    public string? VigenteHasta                 { get; set; }
}

public class BrebRetiroResponse
{
    public long    IdBrebRetiro      { get; set; }
    public string  TipoSujeto        { get; set; } = string.Empty;
    public decimal Valor             { get; set; }
    public string  Moneda            { get; set; } = "COP";
    public string  Estado            { get; set; } = string.Empty;
    public string  ReferenciaInterna { get; set; } = string.Empty;
    public string  KeyValueMasked    { get; set; } = string.Empty;
    public DateTime FechaSolicitud   { get; set; }
    public string? MotivoRechazo     { get; set; }

    // XPAY-373 — sólo poblados para retiros del flujo REAL
    // (BrebPaymentService); null para retiros simulados (Fase 64), que
    // nunca llaman Passport. Nunca el ID completo — sólo fingerprint.
    public string? PaymentIdFingerprint    { get; set; }
    public string? ResolutionIdFingerprint { get; set; }
}

// XPAY-373 FASE 13 — request de POST /api/breb/retiros/real. Sólo el
// monto: account_id/resolution_id/destination key se obtienen 100%
// server-side (ver BrebPaymentService) — no existe ningún campo en este
// DTO por el que el frontend pueda influirlos.
public class SolicitarRetiroRealRequest
{
    public decimal Monto { get; set; }
}

// XPAY-373 FASE 13 — respuesta sanitizada de un retiro real. Nunca expone
// PassportPaymentId/PassportResolutionId completos.
public class RetiroRealResponse
{
    public long      IdBrebRetiro            { get; set; }
    public decimal   Valor                   { get; set; }
    public string    Moneda                  { get; set; } = "COP";
    public string    Estado                  { get; set; } = string.Empty;
    public string?   PaymentIdFingerprint    { get; set; }
    public string?   ResolutionIdFingerprint { get; set; }
    public DateTime  FechaSolicitud          { get; set; }
    public DateTime? FechaEnvioPassport      { get; set; }
    public string?   MotivoRechazo           { get; set; }
}

public class AdminRetiroResponse
{
    public long    IdBrebRetiro        { get; set; }
    public string  TipoSujeto          { get; set; } = string.Empty;
    public long?   IdUsuario           { get; set; }
    public long?   IdComercio          { get; set; }
    public long    IdWallet            { get; set; }
    public decimal Valor               { get; set; }
    public string  Moneda              { get; set; } = "COP";
    public string  Estado              { get; set; } = string.Empty;
    public string  ReferenciaInterna   { get; set; } = string.Empty;
    public string  KeyValueMasked      { get; set; } = string.Empty;
    public string? MotivoRechazo       { get; set; }
    public long?   IdTransaccionLedger { get; set; }
    public DateTime  FechaSolicitud    { get; set; }
    public DateTime? FechaConfirmacion { get; set; }
    public DateTime? FechaLiquidacion  { get; set; }
    public DateTime? FechaRechazo      { get; set; }

    // XPAY-373 FASE 14 — visibilidad admin del flujo real. Nunca IDs
    // Passport completos.
    public string? PaymentIdFingerprint    { get; set; }
    public string? ResolutionIdFingerprint { get; set; }
    public DateTime? FechaEnvioPassport    { get; set; }
}

public class RechazarRetiroRequest
{
    public string? Motivo { get; set; }
}

public class AdminLlaveResponse
{
    public long    IdBrebLlave     { get; set; }
    public string  TipoSujeto      { get; set; } = string.Empty;
    public long?   IdUsuario       { get; set; }
    public long?   IdComercio      { get; set; }
    public long    IdWallet        { get; set; }
    public string  KeyType         { get; set; } = string.Empty;
    public string  KeyValueMasked  { get; set; } = string.Empty;
    public string  Estado          { get; set; } = string.Empty;
    public DateTime  FechaRegistro   { get; set; }
    public DateTime? FechaValidacion { get; set; }
    public bool    EsActiva        { get; set; }

    // XPAY-371 — ver MiLlaveResponse.ResolucionVerificadaPassport. Permite
    // al admin distinguir, en /api/breb/admin/llaves, cuáles llaves
    // VALIDADA lo fueron por Passport real y cuáles por el botón QA
    // simular-validacion-llave.
    public bool    ResolucionVerificadaPassport { get; set; }
}

// XPAY-372 FASE 4 — DTO sanitizado de GET /api/breb/admin/cuenta-operativa.
// NUNCA expone account_id/customer_id en claro (sólo fingerprint) ni
// ningún dato bancario adicional — sólo lo estrictamente necesario para que
// el admin vea el estado de la cuenta operativa QA.
public class CuentaOperativaResponse
{
    public string    AccountIdFingerprint { get; set; } = string.Empty;
    public string?   Estado               { get; set; }
    public string?   Currency             { get; set; }
    public decimal?  SaldoDisponible      { get; set; }
    public decimal?  SaldoPendiente       { get; set; }
    public DateTime  ConsultadoEnUtc      { get; set; }
}

public class PassportHealthResponse
{
    public bool PassportBaseUrl      { get; set; }
    public bool PassportApiKey       { get; set; }
    public bool PassportApiSecret    { get; set; }
    public bool PassportWebhookSecret { get; set; }
}
