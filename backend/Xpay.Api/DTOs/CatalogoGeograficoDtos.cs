namespace Xpay.Api.DTOs;

public record PaisResponse(
    long   IdPais,
    string Codigo,
    string Nombre
);

public record DepartamentoResponse(
    long   IdDepartamento,
    string CodigoDivipola,
    string Nombre
);

public record CiudadResponse(
    long   IdCiudad,
    string CodigoDivipola,
    string Nombre,
    string Tipo
);
