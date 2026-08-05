namespace Xpay.Api.Models;

public class WalletCajaComercio
{
    public long      IdCaja                 { get; set; }
    public long      IdUnidadNegocio        { get; set; }
    public long      IdComercio             { get; set; }
    public long?     IdComercioAliado       { get; set; }
    public long      IdEstablecimiento      { get; set; }
    public long      IdUsuarioCajero        { get; set; }
    public long?     RevisadoPorUsuario     { get; set; }
    public DateOnly  FechaOperativa         { get; set; }
    public DateTime  FechaAperturaUtc       { get; set; }
    public DateTime? FechaCierreUtc         { get; set; }
    public string    Estado                 { get; set; } = "ABIERTA";
    public TimeOnly  HoraLimiteCierre       { get; set; }
    public decimal   FondoInicial           { get; set; }
    public decimal?  EfectivoEsperado       { get; set; }
    public decimal?  EfectivoContado        { get; set; }
    public decimal?  Diferencia             { get; set; }
    public bool      CerradaAutomaticamente { get; set; }
    public string?   ObservacionesCajero    { get; set; }
    public DateTime? FechaRevision          { get; set; }
    public string?   ObservacionesRevision  { get; set; }
    public DateTime  CreatedAt              { get; set; }
}
