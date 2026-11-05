import { ApiError, HttpUncertainError } from '../api/client.ts';

export const MENSAJE_RATE_LIMIT = 'Has realizado varios intentos. Intenta nuevamente más tarde.';

interface OpcionesMensaje {
  // true solo donde el backend devuelve en 400 un mensaje fijo y seguro
  // (verificar-email, restablecer-clave). Nunca se compara su texto: se
  // muestra tal cual, como texto React.
  mostrar400: boolean;
  incierto:   string;
  generico:   string;
}

// P0-B frontend — mensaje seguro para un fallo de las pantallas públicas de
// identidad. 429 por status estructurado (patrón XPAY-402); un timeout de
// POST es incertidumbre, nunca fallo; cualquier otro error (404 en
// productivo, 500, red) usa el texto genérico del llamador sin exponer el
// detalle del backend.
export function mensajeErrorIdentidad(err: unknown, opciones: OpcionesMensaje): string {
  if (err instanceof ApiError && err.status === 429) return MENSAJE_RATE_LIMIT;
  if (err instanceof HttpUncertainError) return opciones.incierto;
  if (opciones.mostrar400 && err instanceof ApiError && err.status === 400 && err.message) return err.message;
  return opciones.generico;
}
