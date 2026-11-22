namespace Xpay.Api.DTOs;

public sealed record DocumentoPreparacionResponse(string Version, string Fecha, string Url, string Sha256, bool Aceptados);
public sealed record PreparacionPinResponse(bool Configurado, bool Bloqueado, byte? LongitudActual, int LongitudRequerida = 6);
public sealed record PreparacionWalletResponse(
    bool Habilitado, bool EsRegistroPublico, bool EmailVerificado, string EstadoKyc,
    bool IdentidadVerificada, bool PerfilCompleto, string? Direccion,
    long? IdDepartamento, long? IdCiudad, string? Departamento, string? Ciudad,
    PreparacionPinResponse Pin, bool WalletHabilitada, string SiguientePaso, bool PuedeActivar,
    DocumentoPreparacionResponse Terminos, DocumentoPreparacionResponse PoliticaDatos,
    string? Mensaje = null, bool PuedeActualizarIdentidad = false);
public sealed record PreparacionDatosRequest(string? Direccion, long IdDepartamento, long IdCiudad);
public sealed record ActivarWalletRequest(bool AceptaTerminos = false, string? VersionTerminos = null,
    bool AutorizaDatos = false, string? VersionPoliticaDatos = null);
