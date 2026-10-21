namespace Xpay.Api.Common;

// P0-B — valores de usuarios.estado. La columna no tiene CHECK: estos son los
// únicos valores que escribe el código. PENDIENTE_VERIFICACION es el estado
// inicial de una cuenta pública hasta verificar su email; NO es ACTIVO, por lo
// que AuthService.LoginAsync la rechaza sin cambios.
public static class EstadosUsuario
{
    public const string Activo                = "ACTIVO";
    public const string PendienteVerificacion = "PENDIENTE_VERIFICACION";
    public const string Bloqueado             = "BLOQUEADO";
    public const string Inactivo              = "INACTIVO";
}
