export const PASOS_PREPARACION = ['CORREO', 'IDENTIDAD', 'DATOS', 'LLAVE', 'PIN', 'ACTIVAR', 'WALLET', 'REVISION'] as const;
export type PasoPreparacionWallet = typeof PASOS_PREPARACION[number];
export const TEXTO_CONDICIONES_WALLET = 'He leído y acepto las Condiciones de habilitación de Wallet en el piloto, versión 1.0, y solicito a XPAY habilitar mi Wallet una vez se cumplan los controles y el límite del piloto.';
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
  llave: { opcion: 'PENDIENTE' | 'REGISTRADA' | 'OMITIDA'; tipo?: string | null; valorEnmascarado?: string | null };
  pin: { configurado: boolean; bloqueado: boolean; longitudActual?: number | null; longitudRequerida: number };
  walletHabilitada: boolean;
  siguientePaso: PasoPreparacionWallet;
  puedeActivar: boolean;
  condiciones?: { version: string; fecha: string; url: string; sha256: string };
  puedeActualizarIdentidad?: boolean;
  mensaje?: string | null;
}
export interface DatosPreparacionWallet { direccion: string; idDepartamento: number; idCiudad: number; }
export interface LlavePreparacionWallet { omitir: boolean; tipoLlave?: string; valorLlave?: string; confirmaPropiedad: boolean; }

export function urlCondicionesWallet(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && !url.username && !url.password && !url.port
      && ['app.xpaycol.com', 'app-qa.xpaycol.com'].includes(url.hostname)
      && url.pathname === '/legal/wallet/v1.0/condiciones.pdf' && !url.search && !url.hash ? url.href : null;
  } catch { return null; }
}
export function condicionesWalletValidas(c: EstadoPreparacionWallet['condiciones']): boolean {
  return !!c && c.version === '1.0' && typeof c.fecha === 'string' && c.fecha.length > 0
    && typeof c.sha256 === 'string' && /^[a-fA-F0-9]{64}$/.test(c.sha256) && !!urlCondicionesWallet(c.url);
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
    && !!e.llave && ['PENDIENTE', 'REGISTRADA', 'OMITIDA'].includes(e.llave.opcion)
    && !!e.pin && typeof e.pin.configurado === 'boolean' && typeof e.pin.bloqueado === 'boolean'
    && e.pin.longitudRequerida === 6
    && (e.puedeActualizarIdentidad === undefined || typeof e.puedeActualizarIdentidad === 'boolean')
    && (!['DATOS', 'LLAVE', 'PIN', 'ACTIVAR'].includes(e.siguientePaso)
      || (e.emailVerificado && e.identidadVerificada && e.estadoKyc === 'APROBADO'))
    && (!['LLAVE', 'PIN', 'ACTIVAR'].includes(e.siguientePaso) || e.perfilCompleto)
    && (!['PIN', 'ACTIVAR'].includes(e.siguientePaso) || e.llave.opcion !== 'PENDIENTE')
    && (e.siguientePaso !== 'ACTIVAR' || (e.pin.configurado && !e.pin.bloqueado && e.pin.longitudActual === 6))
    && (!['ACTIVAR', 'WALLET'].includes(e.siguientePaso) || condicionesWalletValidas(e.condiciones))
    && (e.siguientePaso !== 'WALLET' || e.walletHabilitada);
}

// Estos controles de presentación acompañan las validaciones del servidor.
// Nunca bastan un paso local o un PIN introducido para declarar la Wallet activa.
export function puedeSolicitarActivacion(e: EstadoPreparacionWallet): boolean {
  return e.habilitado && e.esRegistroPublico && e.emailVerificado && e.identidadVerificada
    && e.estadoKyc === 'APROBADO' && e.perfilCompleto && e.llave.opcion !== 'PENDIENTE'
    && e.pin.configurado && !e.pin.bloqueado && e.pin.longitudActual === 6
    && e.siguientePaso === 'ACTIVAR' && e.puedeActivar && condicionesWalletValidas(e.condiciones);
}
