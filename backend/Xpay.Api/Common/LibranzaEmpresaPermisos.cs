namespace Xpay.Api.Common;

// P0 anticipo - autorizacion por rol_empresa (libranza_usuarios_empresa).
// Separa lectura de escritura. Fail-closed: un rol desconocido, vacio o
// nulo no tiene ningun permiso. Ver migracion 012 (CHECK de rol_empresa).
//
//   ADMIN_EMPRESA     lectura + empleados + recaudo + pago simulado QA
//   OPERADOR_EMPRESA  lectura + empleados + recaudo
//   CONSULTA_EMPRESA  solo lectura
public static class LibranzaEmpresaPermisos
{
    public const string AdminEmpresa    = "ADMIN_EMPRESA";
    public const string OperadorEmpresa = "OPERADOR_EMPRESA";
    public const string ConsultaEmpresa = "CONSULTA_EMPRESA";

    private static string Normalizar(string? rol) => rol?.Trim().ToUpperInvariant() ?? string.Empty;

    public static bool PuedeLeer(string? rol) =>
        Normalizar(rol) is AdminEmpresa or OperadorEmpresa or ConsultaEmpresa;

    // Importar / modificar empleados.
    public static bool PuedeGestionarEmpleados(string? rol) =>
        Normalizar(rol) is AdminEmpresa or OperadorEmpresa;

    // Crear / cancelar lotes de recaudo y generar su QR de pago.
    public static bool PuedeOperarRecaudo(string? rol) =>
        Normalizar(rol) is AdminEmpresa or OperadorEmpresa;

    // Pago simulado QA (solo ambiente no productivo, ademas de este rol).
    public static bool PuedeAplicarPagoSimulado(string? rol) =>
        Normalizar(rol) is AdminEmpresa;
}
