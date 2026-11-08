// P0-B frontend — réplica en cliente de AuthService.ValidarPoliticaClave
// (backend), con los mismos mensajes y el mismo orden. Es solo ayuda de UX:
// la política autoritativa sigue en el backend, que además rechaza dos
// reglas que aquí no se pueden comprobar porque la página no conoce al
// usuario: que la clave contenga el nombre de usuario y que sea igual a la
// actual.
//
// Equivalencia JS/.NET: .NET evalúa char.IsUpper/IsLower/IsDigit por unidad
// UTF-16 (categorías Unicode Lu, Ll y Nd) y mide la longitud en unidades
// UTF-16; aquí se recorre igual, unidad por unidad, de modo que una letra o
// dígito fuera del plano básico (par sustituto) no cuenta en ninguno de los
// dos lados. Única diferencia posible: la versión de las tablas Unicode del
// motor JS frente a las de .NET 8 para caracteres asignados recientemente;
// en ese caso decide el backend.
export const CARACTERES_ESPECIALES_CLAVE = '!@#$%^&*()-_=+';
export const LONGITUD_MINIMA_CLAVE = 8;
export const LONGITUD_MAXIMA_CLAVE = 128;

// Mismo texto que RecuperacionClaveService.MensajeConfirmacionDistinta.
export const MENSAJE_CONFIRMACION_DISTINTA = 'La contraseña nueva y su confirmación no coinciden.';

const MAYUSCULA = /^\p{Lu}$/u;
const MINUSCULA = /^\p{Ll}$/u;
const DIGITO    = /^\p{Nd}$/u;

function algunaUnidad(clave: string, cumple: (unidad: string) => boolean): boolean {
  for (let i = 0; i < clave.length; i++) {
    if (cumple(clave[i])) return true;
  }
  return false;
}

// null si cumple; si no, el mismo mensaje que devolvería el backend.
export function validarPoliticaClave(clave: string): string | null {
  if (clave.length < LONGITUD_MINIMA_CLAVE || clave.length > LONGITUD_MAXIMA_CLAVE)
    return 'La contraseña debe tener entre 8 y 128 caracteres.';
  if (!algunaUnidad(clave, u => MAYUSCULA.test(u)))
    return 'La contraseña debe incluir al menos una letra mayúscula.';
  if (!algunaUnidad(clave, u => MINUSCULA.test(u)))
    return 'La contraseña debe incluir al menos una letra minúscula.';
  if (!algunaUnidad(clave, u => DIGITO.test(u)))
    return 'La contraseña debe incluir al menos un dígito.';
  if (!algunaUnidad(clave, u => CARACTERES_ESPECIALES_CLAVE.includes(u)))
    return 'La contraseña debe incluir al menos un carácter especial (!@#$%^&*()-_=+).';
  return null;
}
