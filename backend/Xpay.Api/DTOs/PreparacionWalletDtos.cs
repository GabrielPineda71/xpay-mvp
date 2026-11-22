namespace Xpay.Api.DTOs;

public sealed record CondicionesWalletResponse(string Version, string Fecha, string Url, string Sha256);
public sealed record PreparacionLlaveResponse(string Opcion, string? Tipo, string? ValorEnmascarado);
public sealed record PreparacionPinResponse(bool Configurado, bool Bloqueado, byte? LongitudActual, int LongitudRequerida = 6);
public sealed record PreparacionWalletResponse(
    bool Habilitado, bool EsRegistroPublico, bool EmailVerificado, string EstadoKyc,
    bool IdentidadVerificada, bool PerfilCompleto, string? Direccion,
    long? IdDepartamento, long? IdCiudad, string? Departamento, string? Ciudad,
    PreparacionLlaveResponse Llave, PreparacionPinResponse Pin,
    bool WalletHabilitada, string SiguientePaso, bool PuedeActivar,
    CondicionesWalletResponse Condiciones, string? Mensaje = null, bool PuedeActualizarIdentidad = false);
public sealed record PreparacionDatosRequest(string? Direccion, long IdDepartamento, long IdCiudad);
public sealed record PreparacionLlaveRequest(bool Omitir, string? TipoLlave, string? ValorLlave, bool ConfirmaPropiedad);
public sealed record ActivarWalletRequest(bool AceptaCondiciones, string? VersionCondiciones);
