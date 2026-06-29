namespace Xpay.Api.Models;

public class PassportBrebLlave
{
    public long    IdBrebLlave                        { get; set; }
    public string  TipoSujeto                         { get; set; } = string.Empty; // USUARIO / COMERCIO
    public long?   IdUsuario                          { get; set; }
    public long?   IdComercio                         { get; set; }
    public long    IdWallet                           { get; set; }
    public string  KeyType                            { get; set; } = string.Empty; // ID / PHONE / EMAIL / ALPHA / BCODE
    public string  KeyValueMasked                     { get; set; } = string.Empty;
    public string  KeyValueHash                       { get; set; } = string.Empty;
    public string? KeyValueEncrypted                  { get; set; }
    public string? PassportCustomerId                 { get; set; }
    public string? PassportAccountId                  { get; set; }
    public string? PassportKeyId                      { get; set; }
    public string? OwnerIdentificationType            { get; set; }
    public string? OwnerIdentificationNumberMasked    { get; set; }
    public string? OwnerNameMasked                    { get; set; }
    public string? ParticipantName                    { get; set; }
    public string? ParticipantIdentificationNumber    { get; set; }
    public string? AccountType                        { get; set; }
    public string? AccountNumberMasked                { get; set; }
    public string  Estado                             { get; set; } = "PENDIENTE_VALIDACION";
    public DateTime  FechaRegistro                    { get; set; }
    public DateTime? FechaValidacion                  { get; set; }
    public DateTime? FechaActualizacion               { get; set; }
    public bool    EsActiva                           { get; set; } = true;
    public long?   CreatedByUsuario                   { get; set; }
    public long?   UpdatedByUsuario                   { get; set; }
}
