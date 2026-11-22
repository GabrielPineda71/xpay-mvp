export const PASOS_PREPARACION = ['CORREO', 'IDENTIDAD', 'DATOS', 'PIN', 'ACTIVAR', 'WALLET', 'REVISION'] as const;
export type PasoPreparacionWallet = typeof PASOS_PREPARACION[number];
export const TEXTO_TERMINOS_WALLET = 'Declaro que tengo dieciocho (18) años cumplidos y he leído y acepto los Términos y Condiciones de XPAY.';
export const TEXTO_DATOS_WALLET = 'Autorizo el tratamiento de mis datos personales para el registro, la verificación de identidad y las operaciones de mi Wallet, según la Política de Tratamiento de Datos Personales de XPAY.';
interface DocumentoAceptacion { version: string; fecha: string; url: string; sha256: string; aceptados: boolean; }
export interface EstadoPreparacionWallet {
  habilitado: boolean;
  esRegistroPublico: boolean;
  emailVerificado: boolean;
  estadoKyc: string;
  identidadVerificada: boolean;
  perfilCompleto: boolean;
  direccion: string | null;
  idDepartamento: number | null;
  idCiudad: number | null;
  departamento: string | null;
  ciudad: string | null;
  pin: { configurado: boolean; bloqueado: boolean; longitudActual?: number | null; longitudRequerida: number };
  walletHabilitada: boolean;
  siguientePaso: PasoPreparacionWallet;
  puedeActivar: boolean;
  terminos?: DocumentoAceptacion;
  politicaDatos?: DocumentoAceptacion;
  puedeActualizarIdentidad?: boolean;
  mensaje?: string | null;
}
export interface DatosPreparacionWallet { direccion: string; idDepartamento: number; idCiudad: number; }

function urlDocumentoWallet(value: unknown, ruta: string): string | null {
  if (typeof value !== 'string') return null;
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && !url.username && !url.password && !url.port
      && ['app.xpaycol.com', 'app-qa.xpaycol.com'].includes(url.hostname)
      && url.pathname === ruta && !url.search && !url.hash ? url.href : null;
  } catch { return null; }
}
export function urlTerminosWallet(value: unknown): string | null { return urlDocumentoWallet(value, '/legal/v1.3/terminos.pdf'); }
export function urlPoliticaDatosWallet(value: unknown): string | null { return urlDocumentoWallet(value, '/legal/v1.3/politica.pdf'); }
export function terminosWalletValidos(t: EstadoPreparacionWallet['terminos']): boolean {
  return !!t && t.version === '1.3' && typeof t.fecha === 'string' && t.fecha.length > 0
    && typeof t.aceptados === 'boolean' && typeof t.sha256 === 'string'
    && /^[a-fA-F0-9]{64}$/.test(t.sha256) && !!urlTerminosWallet(t.url);
}
export function politicaDatosWalletValida(p: EstadoPreparacionWallet['politicaDatos']): boolean {
  return !!p && p.version === '1.3' && typeof p.fecha === 'string' && p.fecha.length > 0
    && typeof p.aceptados === 'boolean' && typeof p.sha256 === 'string'
    && /^[a-fA-F0-9]{64}$/.test(p.sha256) && !!urlPoliticaDatosWallet(p.url);
}
export function documentosWalletValidos(e: EstadoPreparacionWallet): boolean {
  return terminosWalletValidos(e.terminos) && politicaDatosWalletValida(e.politicaDatos);
}

export function preparacionWalletValida(value: unknown): value is EstadoPreparacionWallet {
  if (!value || typeof value !== 'object') return false;
  const e = value as EstadoPreparacionWallet;
  if (typeof e.habilitado !== 'boolean' || typeof e.esRegistroPublico !== 'boolean') return false;
  // Un flag cerrado solo anuncia que se debe conservar el recorrido anterior.
  if (!e.habilitado || !e.esRegistroPublico) return true;
  return [e.emailVerificado, e.identidadVerificada, e.perfilCompleto, e.walletHabilitada, e.puedeActivar].every(v => typeof v === 'boolean')
    && ['NO_INICIADO', 'PENDIENTE', 'EN_REVISION', 'APROBADO', 'RECHAZADO', 'EXPIRADO', 'ERROR'].includes(e.estadoKyc)
    && PASOS_PREPARACION.includes(e.siguientePaso)
    && !!e.pin && typeof e.pin.configurado === 'boolean' && typeof e.pin.bloqueado === 'boolean'
    && e.pin.longitudRequerida === 6
    && (e.puedeActualizarIdentidad === undefined || typeof e.puedeActualizarIdentidad === 'boolean')
    && (!['DATOS', 'PIN', 'ACTIVAR'].includes(e.siguientePaso)
      || (e.emailVerificado && e.identidadVerificada && e.estadoKyc === 'APROBADO'))
    && (!['PIN', 'ACTIVAR'].includes(e.siguientePaso) || e.perfilCompleto)
    && (e.siguientePaso !== 'ACTIVAR' || (e.pin.configurado && !e.pin.bloqueado && e.pin.longitudActual === 6))
    && (!['ACTIVAR', 'WALLET'].includes(e.siguientePaso) || documentosWalletValidos(e))
    && (e.siguientePaso !== 'WALLET' || e.walletHabilitada);
}

// Estos controles de presentación acompañan las validaciones del servidor.
// Nunca bastan un paso local o un PIN introducido para declarar la Wallet activa.
export function puedeSolicitarActivacion(e: EstadoPreparacionWallet): boolean {
  return e.habilitado && e.esRegistroPublico && e.emailVerificado && e.identidadVerificada
    && e.estadoKyc === 'APROBADO' && e.perfilCompleto
    && e.pin.configurado && !e.pin.bloqueado && e.pin.longitudActual === 6
    && e.siguientePaso === 'ACTIVAR' && e.puedeActivar && documentosWalletValidos(e);
}
