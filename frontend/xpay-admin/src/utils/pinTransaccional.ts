export const LONGITUD_PIN = 6;
export const MENSAJE_FORMATO_PIN = `El PIN transaccional debe tener ${LONGITUD_PIN} dígitos.`;
export const MENSAJE_ACTUALIZAR_PIN = 'Actualiza tu PIN de 5 o 7 dígitos por uno de 6 antes de realizar transacciones monetarias.';

export interface EstadoPin {
  configurado: boolean;
  bloqueado: boolean;
  restablecido: boolean;
  intentosRestantes: number;
  longitudRequerida: number;
  requiereActualizacion: boolean;
}

export function estadoPinValido(estado: EstadoPin | null | undefined): estado is EstadoPin {
  return estado != null && typeof estado.configurado === 'boolean' && typeof estado.bloqueado === 'boolean'
    && typeof estado.requiereActualizacion === 'boolean' && estado.longitudRequerida === LONGITUD_PIN;
}

// Conserva los dígitos de más para que un PIN anterior pegado no se envíe recortado.
export function soloDigitosPin(valor: string): string {
  return valor.replace(/[^0-9]/g, '');
}

export function esPinValido(pin: string): boolean {
  return pin.length === LONGITUD_PIN && soloDigitosPin(pin) === pin;
}

export function esPinActualValido(pin: string): boolean {
  return [5, LONGITUD_PIN, 7].includes(pin.length) && soloDigitosPin(pin) === pin;
}
