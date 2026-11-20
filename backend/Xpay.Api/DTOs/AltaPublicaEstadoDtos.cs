namespace Xpay.Api.DTOs;

public sealed record AltaPublicaEstadoResponse(
    bool EsRegistroPublico,
    bool HabilitadaPiloto,
    bool EmailVerificado,
    bool WhatsAppVerificado,
    bool IdentidadVerificada,
    string EstadoKyc,
    string Estado,
    IReadOnlyList<string> Pendientes,
    long? IdWallet);
