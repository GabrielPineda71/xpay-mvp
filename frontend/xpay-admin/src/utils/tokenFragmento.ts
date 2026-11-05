// P0-B frontend — token de un solo uso de los enlaces de identidad
// (verificación de email y recuperación de contraseña). El backend lo emite
// como 32 bytes en base64url sin relleno (TokenUsuarioService): exactamente
// 43 caracteres [A-Za-z0-9_-]. Viaja SOLO en el fragmento (#token=...), que
// el navegador no envía al servidor; nunca se acepta desde la query string.
// Función pura: no guarda ni registra nada.
const FORMATO_TOKEN = /^[A-Za-z0-9_-]{43}$/;

// Token si el fragmento trae exactamente un parámetro "token" con el formato
// exacto; null en cualquier otro caso (y entonces no se llama al backend).
export function leerTokenDeFragmento(hash: string): string | null {
  if (!hash.startsWith('#')) return null;
  const valores = new URLSearchParams(hash.slice(1)).getAll('token');
  if (valores.length !== 1) return null;
  return FORMATO_TOKEN.test(valores[0]) ? valores[0] : null;
}
