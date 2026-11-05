namespace Xpay.Api.DTOs;

// P0 anticipo - lote de recaudo empresarial (ver LibranzaRecaudoService).

public class CrearLoteRecaudoRequest
{
    // Fecha de corte (fecha_pago_programada de los anticipos), YYYY-MM-DD.
    public string FechaCorte { get; set; } = string.Empty;
}

public class LoteRecaudoResponse
{
    public long      IdLote                 { get; set; }
    public long      IdConvenio             { get; set; }
    public string    FechaCorte             { get; set; } = string.Empty;
    public string    Estado                 { get; set; } = string.Empty;
    public string    ReferenciaPublica      { get; set; } = string.Empty;
    public decimal   MontoTotal             { get; set; }
    public string    Moneda                 { get; set; } = "COP";
    public int       CantidadDetalles       { get; set; }
    public DateTime  FechaCreacion          { get; set; }
    public DateTime? FechaCierre            { get; set; }
    public string?   QrEstado               { get; set; }
    public string?   QrCodeData             { get; set; }
    public DateTime? FechaQrGenerado        { get; set; }
    public DateTime? FechaPagoRecibido      { get; set; }
    public DateTime? FechaFondosConfirmados { get; set; }
    public DateTime? FechaAplicacion        { get; set; }
    public string?   MotivoRevision         { get; set; }
    public List<LoteRecaudoDetalleResponse> Detalles { get; set; } = new();
}

public class LoteRecaudoDetalleResponse
{
    public long    IdDetalle       { get; set; }
    public long    IdAnticipo      { get; set; }
    public long    IdEmpleado      { get; set; }
    public string  NombresEmpleado { get; set; } = string.Empty;
    public decimal MontoCapital    { get; set; }
    public decimal MontoComision   { get; set; }
    public decimal MontoIva        { get; set; }
    public decimal MontoTotal      { get; set; }
    public string  Estado          { get; set; } = string.Empty;
}
