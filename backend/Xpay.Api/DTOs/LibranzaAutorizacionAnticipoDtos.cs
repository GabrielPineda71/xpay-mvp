namespace Xpay.Api.DTOs;

public sealed class LibranzaAutorizacionesAnticipoResponse
{
    public List<LibranzaAutorizacionAnticipoResponse> Items { get; set; } = [];
    public int Pagina { get; set; }
    public int Total { get; set; }
    public int TotalPaginas { get; set; }
}

public sealed class LibranzaAutorizacionAnticipoResponse
{
    public long IdEmpleadoUsuario { get; set; }
    public long IdEmpleado { get; set; }
    public long IdUsuario { get; set; }
    public long IdConvenio { get; set; }
    public string Empresa { get; set; } = "";
    public string Empleado { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string DocumentoMasked { get; set; } = "";
    public bool EmailVerificado { get; set; }
    public bool IdentidadVerificada { get; set; }
    public bool WalletActiva { get; set; }
    public bool OrigenPublico { get; set; }
    public string Estado { get; set; } = "PENDIENTE";
    public bool PuedeAutorizar { get; set; }
    public bool PuedeRevocar { get; set; }
    public List<string> Razones { get; set; } = [];
    public string Huella { get; set; } = "";
    public DateTime? FechaAutorizacion { get; set; }
    public DateTime? FechaRevocacion { get; set; }
}

public sealed class CambiarAutorizacionAnticipoRequest
{
    public long IdEmpleadoUsuario { get; set; }
    public string Huella { get; set; } = "";
    public string ReferenciaRevision { get; set; } = "";
    public bool ConfirmaAutorizacion { get; set; }
}
