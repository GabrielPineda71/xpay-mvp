namespace Xpay.Api.Common;

// P0-B1 — fallo técnico de configuración/infraestructura de identidad: unidad
// XPAY_COL ausente o ambigua, rol USUARIO_FINAL no disponible, transacción
// ajena, o configuración del enlace inválida en un ambiente productivo. NO
// deriva de InvalidOperationException a propósito: los controladores tratan
// InvalidOperationException como error de negocio (400 con su mensaje); este
// fallo es técnico y debe terminar en el 500 genérico.
public sealed class IdentidadConfiguracionException : Exception
{
    public IdentidadConfiguracionException(string message) : base(message) { }
}
