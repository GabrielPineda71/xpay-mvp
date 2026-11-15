export const LONGITUD_PIN = 5;
export const LONGITUD_PIN_ANTERIOR = 7;
export const MENSAJE_FORMATO_PIN = `El PIN transaccional debe tener ${LONGITUD_PIN} dígitos.`;
export const MENSAJE_ACTUALIZAR_PIN = `Si tu PIN tiene ${LONGITUD_PIN_ANTERIOR} dígitos, cámbialo por uno de ${LONGITUD_PIN} en Perfil antes de operar.`;

// Conserva los dígitos de más para que un PIN anterior pegado no se envíe recortado.
export function soloDigitosPin(valor: string): string {
  return valor.replace(/[^0-9]/g, '');
}

export function esPinValido(pin: string): boolean {
  return pin.length === LONGITUD_PIN && soloDigitosPin(pin) === pin;
}

export function esPinActualValido(pin: string): boolean {
  return (pin.length === LONGITUD_PIN || pin.length === LONGITUD_PIN_ANTERIOR) && soloDigitosPin(pin) === pin;
}
