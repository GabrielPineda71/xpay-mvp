namespace Xpay.Api.Models;

// XPAY-385 — CAPA 2 (tesorería real de XPAY), distinta de la Wallet
// individual del usuario (CAPA 1). Representa una cuenta operativa real de
// XPAY en un proveedor externo (hoy: Passport/Coopcentral). NUNCA almacena
// el account_id completo — solo su fingerprint (Xpay.Api.Common.
// Fingerprint) y el NOMBRE de la variable de configuración donde vive el
// valor real (ConfigKeyReference), nunca el valor mismo.
//
// IdCuentaLedger es una FK REAL hacia LedgerCuenta.IdCuenta (no un código
// de texto duplicado) — vínculo inequívoco y con integridad referencial
// entre esta entidad y el catálogo contable (110103 en Sandbox).
public class CuentaOperativa
{
    public long   IdCuentaOperativa    { get; set; }
    public string Proveedor            { get; set; } = string.Empty; // p.ej. "PASSPORT"
    public string Institucion          { get; set; } = string.Empty; // p.ej. "COOPCENTRAL"
    public string Moneda               { get; set; } = string.Empty; // "COP"
    public string Ambiente             { get; set; } = string.Empty; // "SANDBOX" | "PRODUCCION"
    public string AccountIdFingerprint { get; set; } = string.Empty;
    public string ConfigKeyReference   { get; set; } = string.Empty; // p.ej. "PASSPORT_ACCOUNT_ID"
    public long   IdCuentaLedger       { get; set; }
    public string Estado               { get; set; } = "ACTIVA";
    public DateTime  FechaCreacion     { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}
