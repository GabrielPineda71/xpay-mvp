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
}

public class PassportHealthResponse
{
    public bool PassportBaseUrl      { get; set; }
    public bool PassportApiKey       { get; set; }
    public bool PassportApiSecret    { get; set; }
    public bool PassportWebhookSecret { get; set; }
}
