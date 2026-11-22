import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, get, HttpUncertainError, post } from '../../api/client.ts';
import './VerificacionIdentidadPublicaSection.css';

interface ApiOk<T> { success: boolean; data: T; }
interface ConsentimientoKyc {
  documento: { version: string; fecha: string; url: string } | null;
  textoCasilla: string;
  aceptado: boolean;
  habilitado: boolean;
}
interface EstadoKyc { estadoKyc: string; sessionUrl?: string | null; }
interface PerfilContacto { emailVerificado: boolean; }
const ETIQUETAS_KYC: Record<string, string> = {
  NO_INICIADO: 'Pendiente', PENDIENTE: 'En proceso', EN_REVISION: 'En revisión', APROBADO: 'Aprobada',
  RECHAZADO: 'No aprobada', EXPIRADO: 'Sesión vencida', ERROR: 'Pendiente de revisión',
};

// Solo enlaces de sesión del proveedor; nunca se abre una URL arbitraria
// que haya quedado persistida o que llegue en una respuesta incorrecta.
export function urlSesionVeriffPublica(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.username || url.password || url.port
      || !['magic.veriff.me', 'alchemy.veriff.com', 'saas.veriff.com'].includes(url.hostname)
      || !/^\/v\/[^/]+\/?$/.test(url.pathname)) return null;
    return url.href;
  } catch { return null; }
}
function urlDocumento(value: string | undefined): string | null {
  try {
    const url = new URL(value ?? '');
    return url.protocol === 'https:' && !url.username && !url.password && !url.port
      && ['app.xpaycol.com', 'app-qa.xpaycol.com'].includes(url.hostname)
      && url.pathname === '/legal/v1.2/autorizaciones.pdf'
      && !url.search && !url.hash ? url.href : null;
  } catch { return null; }
}

// Esta sección consulta el proceso de la cuenta pública. Solo un clic explícito
// registra la casilla C o inicia Veriff; no habilita Wallet ni productos.
export function VerificacionIdentidadPublicaSection({ guiado = false, onActualizado }: { guiado?: boolean; onActualizado?: () => void } = {}) {
  const [consentimiento, setConsentimiento] = useState<ConsentimientoKyc | null>(null);
  const [kyc, setKyc] = useState<EstadoKyc | null>(null);
  const [correoVerificado, setCorreoVerificado] = useState(false);
  const [autoriza, setAutoriza] = useState(false);
  const [cargando, setCargando] = useState(true);
  const [consultaVigente, setConsultaVigente] = useState(false);
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState('');
  const [aviso, setAviso] = useState('');
  const secuencia = useRef(0);
  const enviando = useRef(false);
  const versionDocumento = useRef<string | undefined>(undefined);

  const cargar = useCallback(async () => {
    if (enviando.current) return;
    const intento = ++secuencia.current;
    setCargando(true); setConsultaVigente(false); setError(''); setAviso('');
    try {
      const [autorizacion, identidad, perfil] = await Promise.all([
        get<ApiOk<ConsentimientoKyc>>('/api/usuarios/alta-publica/consentimiento-kyc'),
        get<ApiOk<EstadoKyc>>('/api/kyc/mi-estado'),
        get<ApiOk<PerfilContacto>>('/api/usuarios/mi-perfil'),
      ]);
      if (intento !== secuencia.current) return;
      if (!autorizacion.success || !identidad.success || !perfil.success
        || !Object.prototype.hasOwnProperty.call(ETIQUETAS_KYC, identidad.data?.estadoKyc)
        || typeof perfil.data?.emailVerificado !== 'boolean'
        || typeof autorizacion.data?.habilitado !== 'boolean'
        || typeof autorizacion.data?.aceptado !== 'boolean'
        || typeof autorizacion.data?.textoCasilla !== 'string') throw new Error('Respuesta no reconocida.');
      if (versionDocumento.current !== autorizacion.data.documento?.version) setAutoriza(false);
      versionDocumento.current = autorizacion.data.documento?.version;
      setConsentimiento(autorizacion.data); setKyc(identidad.data);
      setCorreoVerificado(perfil.data.emailVerificado); setConsultaVigente(true);
    } catch (err) {
      if (intento !== secuencia.current) return;
      setKyc(null); setAutoriza(false);
      setError(err instanceof ApiError && err.status === 404
        ? 'La verificación de identidad aún no está disponible. Puedes conservar y actualizar tu Perfil.'
        : 'No pudimos consultar tu verificación. Actualiza el estado antes de continuar.');
    } finally {
      if (intento === secuencia.current) setCargando(false);
    }
  }, []);

  useEffect(() => { void cargar(); return () => { secuencia.current++; }; }, [cargar]);
  useEffect(() => {
    const refrescar = () => { if (document.visibilityState === 'visible') void cargar(); };
    window.addEventListener('focus', refrescar);
    document.addEventListener('visibilitychange', refrescar);
    return () => { window.removeEventListener('focus', refrescar); document.removeEventListener('visibilitychange', refrescar); };
  }, [cargar]);

  const documentoUrl = urlDocumento(consentimiento?.documento?.url);
  const permiteAutorizacion = consultaVigente && correoVerificado && consentimiento?.habilitado && documentoUrl;
  const permiteVeriff = permiteAutorizacion && consentimiento?.aceptado;
  const sessionUrl = permiteVeriff && kyc?.estadoKyc === 'PENDIENTE' ? urlSesionVeriffPublica(kyc.sessionUrl) : null;

  async function guardarConsentimiento() {
    if (!autoriza || !permiteAutorizacion || !consentimiento?.documento || enviando.current || cargando) return;
    const intento = ++secuencia.current;
    enviando.current = true; setOcupado(true); setError(''); setAviso('');
    try {
      const r = await post<ApiOk<ConsentimientoKyc>>('/api/usuarios/alta-publica/consentimiento-kyc', {
        autorizaBiometria: true, versionDocumento: consentimiento.documento.version,
      });
      if (intento !== secuencia.current) return;
      if (!r.success || !r.data?.aceptado || !r.data.habilitado
        || r.data.documento?.version !== consentimiento.documento.version
        || urlDocumento(r.data.documento?.url) !== documentoUrl) throw new Error('Autorización no confirmada.');
      setConsentimiento(r.data); setAutoriza(false);
      setAviso('Autorización guardada. Ahora puedes iniciar la verificación de identidad.');
    } catch (err) {
      if (intento !== secuencia.current) return;
      setConsultaVigente(false); setAutoriza(false);
      setError(err instanceof HttpUncertainError
        ? 'No pudimos confirmar si se guardó la autorización. Actualiza el estado antes de intentarlo otra vez.'
        : 'No se pudo guardar la autorización. Actualiza el documento y revísalo de nuevo.');
    } finally { enviando.current = false; setOcupado(false); }
  }

  async function iniciarVerificacion() {
    if (!permiteVeriff || kyc?.estadoKyc !== 'NO_INICIADO' || enviando.current || cargando) return;
    const intento = ++secuencia.current;
    enviando.current = true; setOcupado(true); setError(''); setAviso('');
    try {
      const r = await post<ApiOk<EstadoKyc>>('/api/kyc/veriff/session', {});
      if (intento !== secuencia.current) return;
      if (!r.success || r.data?.estadoKyc !== 'PENDIENTE') throw new Error('Sesión no confirmada.');
      setKyc(r.data);
      setAviso('Tu sesión está lista. Pulsa Continuar verificación y ten tu documento a mano.');
    } catch (err) {
      if (intento !== secuencia.current) return;
      setConsultaVigente(false); setKyc(null);
      setError(err instanceof HttpUncertainError
        ? 'No sabemos todavía si se creó tu sesión. Actualiza el estado para recuperarla; no inicies otra.'
        : 'No pudimos iniciar la verificación. Actualiza el estado antes de volver a intentarlo.');
    } finally { enviando.current = false; setOcupado(false); }
  }

  return <section className="profile-section identidad-publica" aria-label="Verificación de mi identidad">
    {!guiado && <><h3 className="profile-section-title">Completa tu registro</h3>
    <p>Confirma tu correo y verifica tu identidad con Veriff. Puedes conservar tu cuenta y consultar Perfil si decides no autorizar la biometría.</p></>}
    {cargando && <p role="status">Consultando tu verificación...</p>}
    {error && <p role="alert" className="breb-msg-err">{error}</p>}
    {aviso && <p role="status" className="breb-msg-ok">{aviso}</p>}
    {!guiado && <ol className="identidad-publica-pasos">
      <li><strong>Correo electrónico</strong><span>{consultaVigente ? correoVerificado ? 'Verificado' : 'Pendiente. Confírmalo en Mi información.' : 'Sin confirmar'}</span></li>
      <li><strong>Identidad con Veriff</strong><span>{cargando ? 'Consultando...' : kyc ? ETIQUETAS_KYC[kyc.estadoKyc] : 'Sin confirmar'}</span></li>
      <li><strong>WhatsApp</strong><span>Verificación pendiente para una próxima etapa. Puedes continuar con Veriff.</span></li>
    </ol>}
    {consentimiento && <div className="identidad-publica-autorizacion">
      {!guiado && <h4>Verifica tu identidad</h4>}
      {(!guiado || !consentimiento.aceptado) && <p>La autorización biométrica es opcional para conservar tu cuenta y necesaria para iniciar Veriff. No autoriza consultas de crédito ni publicidad.</p>}
      {documentoUrl && consentimiento.documento && <a href={documentoUrl} target="_blank" rel="noopener noreferrer">Leer autorizaciones de registro y KYC · {consentimiento.documento.version}</a>}
      {!correoVerificado && consultaVigente && <p>Primero confirma tu correo electrónico en Mi información.</p>}
      {!consentimiento.habilitado && correoVerificado && <p>La verificación de identidad estará disponible cuando XPAY complete su configuración.</p>}
      {consentimiento.habilitado && !documentoUrl && <p role="alert">No pudimos cargar el documento de autorización. Actualiza el estado para continuar.</p>}
      {!consentimiento.aceptado && permiteAutorizacion && <>
        <label className="profile-checkbox identidad-publica-consentimiento">
          <input type="checkbox" checked={autoriza} disabled={ocupado || cargando || !consultaVigente} onChange={e => setAutoriza(e.target.checked)} />
          <span>{consentimiento.textoCasilla}</span>
        </label>
        <button type="button" className="profile-btn-primary" disabled={!autoriza || ocupado || cargando || !consultaVigente} onClick={() => void guardarConsentimiento()}>Guardar autorización</button>
      </>}
      {consentimiento.aceptado && !guiado && <p>Autorización biométrica registrada.</p>}
      {permiteVeriff && kyc?.estadoKyc === 'NO_INICIADO' && <button type="button" className="profile-btn-primary" disabled={ocupado || cargando} onClick={() => void iniciarVerificacion()}>Iniciar verificación de identidad</button>}
      {!cargando && consultaVigente && permiteVeriff && kyc?.estadoKyc === 'PENDIENTE' && (sessionUrl
        ? <a className="profile-btn-primary identidad-publica-continuar" href={sessionUrl} target="_blank" rel="noopener noreferrer">Continuar verificación</a>
        : <p>El enlace para continuar no está disponible. Actualiza el estado para recuperar tu sesión; si continúa igual, contacta a XPAY.</p>)}
      {!cargando && kyc?.estadoKyc === 'EN_REVISION' && <p>Veriff está revisando tu información. Consulta nuevamente el estado más adelante.</p>}
      {!cargando && kyc?.estadoKyc === 'APROBADO' && <p>{guiado ? 'Identidad aprobada. Actualiza el estado para continuar.' : 'Tu identidad fue aprobada. La activación de Wallet y productos financieros requiere la habilitación adicional de XPAY.'}</p>}
      {!cargando && kyc && ['RECHAZADO', 'EXPIRADO', 'ERROR'].includes(kyc.estadoKyc) && <p>Tu verificación requiere revisión de XPAY. Contacta a soporte antes de iniciar otro intento.</p>}
    </div>}
    {!guiado && <p>Crear la cuenta o aprobar Veriff no activa una Wallet, asigna un cupo ni permite operar automáticamente.</p>}
    <button type="button" className="profile-btn-secondary" disabled={cargando || ocupado} onClick={() => void cargar().then(() => onActualizado?.())}>{cargando ? 'Actualizando verificación...' : 'Actualizar estado de verificación'}</button>
  </section>;
}
