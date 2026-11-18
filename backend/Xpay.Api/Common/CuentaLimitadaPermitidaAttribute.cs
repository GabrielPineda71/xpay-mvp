namespace Xpay.Api.Common;

// Excepción explícita por acción al aislamiento de cuentas de autorregistro.
// No se admite a nivel de controlador: una acción nueva permanece cerrada
// hasta revisar sus efectos. No concede autenticación ni evita otras políticas.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CuentaLimitadaPermitidaAttribute : Attribute { }
