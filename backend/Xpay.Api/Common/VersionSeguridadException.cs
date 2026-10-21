namespace Xpay.Api.Common;

// P0-B — fallo de contrato/infraestructura de version_seguridad: no hay
// transacción del llamador, o el UPDATE atómico no afectó exactamente 1 fila.
// NO deriva de InvalidOperationException a propósito: los controladores
// tratan InvalidOperationException como error de negocio (400 con su
// mensaje); este fallo es técnico y debe terminar en el 500 genérico.
public sealed class VersionSeguridadException : Exception
{
    public VersionSeguridadException(string message) : base(message) { }
}
