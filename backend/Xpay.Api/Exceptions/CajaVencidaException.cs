using Xpay.Api.DTOs;

namespace Xpay.Api.Exceptions;

// Extiende TransicionCajaInvalidaException — es el caso específico en que la
// caja resultó lógicamente vencida (EstaVencida, diseño Fase 70.4 sección 9) y
// ya fue auto-sanada (Fase A) antes de rechazar la operación de escritura
// (recargar/iniciar-cuadre/cerrar). Nunca se usa en lecturas — esas se
// auto-sanan en silencio y responden 200 (sección 11). Transporta el CajaDto
// ya actualizado para que el controller lo incluya en el campo "data" del
// 409 (sección 12), y el frontend refresque la UI sin una segunda llamada.
public class CajaVencidaException(string message, CajaDto cajaActual) : TransicionCajaInvalidaException(message)
{
    public CajaDto CajaActual { get; } = cajaActual;
}
