namespace Xpay.Api.Models;

// P0 anticipo (047) - lote durable de recaudo empresarial: empresa +
// convenio + corte + detalles + total + pago Passport. Ver
// LibranzaRecaudoService y database/047_libranza_recaudo_p0.sql.
public class LibranzaLoteRecaudo
{
    public long      IdLote                        { get; set; }
    public long      IdConvenio                    { get; set; }
    public DateOnly  FechaCorte                    { get; set; }
    public string    Estado                        { get; set; } = LibranzaLoteEstados.Borrador;
    public string    ReferenciaPublica             { get; set; } = string.Empty;
    public decimal   MontoTotal                    { get; set; }
    public string    Moneda                        { get; set; } = "COP";
    public int       CantidadDetalles              { get; set; }
    public DateTime  FechaCreacion                 { get; set; }
    public long      CreadoPorUsuario              { get; set; }
    public DateTime? FechaCierre                   { get; set; }
    public long?     CerradoPorUsuario             { get; set; }
    public string?   QrEstado                      { get; set; }
    public DateTime? FechaQrIntento                { get; set; }
    public string?   PassportQrId                  { get; set; }
    public string?   QrCodeData                    { get; set; }
    public DateTime? FechaQrGenerado               { get; set; }
    public string?   PassportPaymentId             { get; set; }
    public string?   EndToEndIdentification        { get; set; }
    public DateTime? FechaPagoRecibido             { get; set; }
    public DateTime? FechaFondosConfirmados        { get; set; }
    public DateTime? FechaAplicacion               { get; set; }
    public long?     IdTransaccionLedgerAplicacion { get; set; }
    public long?     IdEventoSettled               { get; set; }
    public string?   UltimoRechazoCodigo           { get; set; }
    public DateTime? FechaUltimoRechazo            { get; set; }
    public string?   MotivoRevision                { get; set; }
}

public class LibranzaLoteRecaudoDetalle
{
    public long      IdDetalle       { get; set; }
    public long      IdLote          { get; set; }
    public long      IdAnticipo      { get; set; }
    public long      IdEmpleado      { get; set; }
    public decimal   MontoCapital    { get; set; }
    public decimal   MontoComision   { get; set; }
    public decimal   MontoIva        { get; set; }
    public decimal   MontoTotal      { get; set; }
    public string    Estado          { get; set; } = LibranzaLoteEstados.DetalleVigente;
    public DateTime  FechaCreacion   { get; set; }
    public DateTime? FechaAplicacion { get; set; }
}

public static class LibranzaLoteEstados
{
    public const string Borrador         = "BORRADOR";
    public const string PendientePago    = "PENDIENTE_PAGO";
    public const string PagoEnProceso    = "PAGO_EN_PROCESO";
    public const string Aplicado         = "APLICADO";
    public const string RequiereRevision = "REQUIERE_REVISION";
    public const string Cancelado        = "CANCELADO";

    public const string DetalleVigente  = "VIGENTE";
    public const string DetalleAplicado = "APLICADO";
    public const string DetalleLiberado = "LIBERADO";

    public const string QrEnCurso   = "EN_CURSO";
    public const string QrGenerado  = "GENERADO";
}
