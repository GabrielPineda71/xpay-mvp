namespace Xpay.Api.DTOs;

public record RegistroInicialRequest(
    string Usuario,
    string Password,
    string Celular
);

public record RegistroInicialResultDto(
    long IdUsuario,
    long IdPersona
);
