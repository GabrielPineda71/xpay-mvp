namespace Xpay.Api.DTOs;

public sealed class LibranzaVinculacionesResponse
{
    public List<LibranzaCoincidenciaResponse> Items { get; set; } = [];
    public int Pagina { get; set; }
    public int Total { get; set; }
    public int TotalPaginas { get; set; }
}

public sealed class LibranzaCoincidenciaResponse
{
    public long IdEmpleado { get; set; }
    public long IdUsuario { get; set; }
    public long IdConvenio { get; set; }
    public string Empresa { get; set; } = string.Empty;
    public string Empleado { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string DocumentoMasked { get; set; } = string.Empty;
    public decimal CupoPreliminar { get; set; }
    public List<CupoCorteRevisionResponse> CuposPorCorte { get; set; } = [];
    public bool EmailVerificado { get; set; }
    public bool WhatsAppVerificado { get; set; }
    public bool IdentidadVerificada { get; set; }
    public bool Elegible { get; set; }
    public List<string> Razones { get; set; } = [];
    public string Huella { get; set; } = string.Empty;
    public string Estado { get; set; } = "PENDIENTE";
}

public sealed class CupoCorteRevisionResponse
{
    public int NumeroCorte { get; set; }
    public int DiaPago { get; set; }
    public decimal ValorPagoProgramado { get; set; }
    public decimal PorcentajeCupo { get; set; }
    public decimal CupoBase { get; set; }
}

public sealed class AprobarVinculacionesRequest
{
    public List<SeleccionVinculacionRequest> Seleccion { get; set; } = [];
    public bool ConfirmaPertenenciaLaboral { get; set; }
    public string ReferenciaRevision { get; set; } = string.Empty;
}

public sealed class SeleccionVinculacionRequest
{
    public long IdEmpleado { get; set; }
    public long IdUsuario { get; set; }
    public string Huella { get; set; } = string.Empty;
}

public sealed class AprobarVinculacionesResponse
{
    public List<ResultadoVinculacionResponse> Resultados { get; set; } = [];
    public int Aprobados => Resultados.Count(r => r.Aprobado);
    public int Rechazados => Resultados.Count(r => !r.Aprobado);
}

public sealed class ResultadoVinculacionResponse
{
    public long IdEmpleado { get; set; }
    public long IdUsuario { get; set; }
    public bool Aprobado { get; set; }
    public string Mensaje { get; set; } = string.Empty;
}
