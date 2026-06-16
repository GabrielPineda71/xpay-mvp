namespace Xpay.Api.Models;
public class Auditoria
{
    public long IdAuditoria { get; set; }
    public long? IdUsuario { get; set; }
    public long? IdPersona { get; set; }
    public string Modulo { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string? Entidad { get; set; }
    public string? IdEntidad { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public string? Ip { get; set; }
    public string? Dispositivo { get; set; }
    public string Resultado { get; set; } = "EXITOSO";
    public string? Observacion { get; set; }
    public DateTime FechaEvento { get; set; }
}
