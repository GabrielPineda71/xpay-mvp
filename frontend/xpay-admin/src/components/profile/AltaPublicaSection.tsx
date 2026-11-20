import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError, get, HttpUncertainError, post } from '../../api/client.ts';
import { useAuth } from '../../auth/AuthContext.tsx';
import './AltaPublicaSection.css';

interface ApiOk<T> { success: boolean; data: T; }
export interface EstadoAltaPublica {
  esRegistroPublico: boolean;
  habilitadaPiloto: boolean;
  emailVerificado: boolean;
  whatsAppVerificado: boolean;
  identidadVerificada: boolean;
  estadoKyc: string;
  estado: string;
  pendientes: string[];
  idWallet?: number | null;
}
interface ConsentimientoKyc {
  documento: { version: string; fecha: string; url: string } | null;
  textoCasilla: string;
  aceptado: boolean;
  habilitado: boolean;
}
interface EstadoKyc { estadoKyc: string; sessionUrl?: string | null; }
const ESTADOS_KYC = new Set(['NO_INICIADO', 'PENDIENTE', 'EN_REVISION', 'APROBADO', 'RECHAZADO', 'EXPIRADO', 'ERROR']);
const ETIQUETAS_KYC: Record<string, string> = {
  NO_INICIADO: 'Pendiente', PENDIENTE: 'En proceso', EN_REVISION: 'En revisión', APROBADO: 'Aprobada',
  RECHAZADO: 'No aprobada', EXPIRADO: 'Sesión vencida', ERROR: 'Pendiente de revisión',
};

// Un enlace persistido nunca se abre sin validar el origen oficial de Veriff.
export function urlSesionVeriff(value: unknown): string | null {
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
      && !url.search && !url.hash ? url.href : null;
  } catch { return null; }
}

// Solo lectura hasta que la persona autoriza expresamente y solicita iniciar
// Veriff. No acepta documentos ni activa una Wallet al montar o actualizar.
export function AltaPublicaSection() {
  const [estado, setEstado] = useState<EstadoAltaPublica | null>(null);
  const [consentimiento, setConsentimiento] = useState<ConsentimientoKyc | null>(null);
  const [kyc, setKyc] = useState<EstadoKyc | null>(null);
  const [autoriza, setAutoriza] = useState(false);
  const [cargando, setCargando] = useState(true);
  const [consultaVigente, setConsultaVigente] = useState(false);
  const [ocupado, setOcupado] = useState(false);
  const [noDisponible, setNoDisponible] = useState(false);
  const [error, setError] = useState('');
  const [aviso, setAviso] = useState('');
  const secuencia = useRef(0);
  const versionDocumento = useRef<string | undefined>(undefined);
  const { logout } = useAuth();
  const navigate = useNavigate();

  const cargar = useCallback(async () => {
    const intento = ++secuencia.current;
    setCargando(true); setConsultaVigente(false); setError('');
    try {
      const alta = await get<ApiOk<EstadoAltaPublica>>('/api/usuarios/alta-publica/estado');
      if (intento !== secuencia.current) return;
      if (!alta.success || !Array.isArray(alta.data?.pendientes)) throw new Error('Respuesta no reconocida.');
      if (!alta.data.esRegistroPublico) { setEstado(alta.data); setNoDisponible(true); return; }
      const [consentimientoActual, identidad] = await Promise.all([
        get<ApiOk<ConsentimientoKyc>>('/api/usuarios/alta-publica/consentimiento-kyc'),
        get<ApiOk<EstadoKyc>>('/api/kyc/mi-estado'),
      ]);
      if (intento !== secuencia.current) return;
      if (!alta.success || !consentimientoActual.success || !identidad.success
        || !ESTADOS_KYC.has(identidad.data?.estadoKyc) || !Array.isArray(alta.data?.pendientes))
        throw new Error('Respuesta no reconocida.');
      setEstado(alta.data); setKyc(identidad.data); setConsultaVigente(true);
      setNoDisponible(false);
      // Si el documento cambió mientras la persona revisaba, la nueva casilla
      // vuelve a estar desmarcada y requiere un nuevo acto explícito.
      if (versionDocumento.current !== consentimientoActual.data.documento?.version) setAutoriza(false);
      versionDocumento.current = consentimientoActual.data.documento?.version;
      setConsentimiento(consentimientoActual.data);
    } catch (err) {
      if (intento !== secuencia.current) return;
      setKyc(null);
      if (err instanceof ApiError && err.status === 404) setNoDisponible(true);
      else setError('No pudimos consultar tu proceso de activación. Vuelve a actualizar el estado.');
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

  async function guardarConsentimiento() {
    if (!autoriza || !consentimiento?.habilitado || !consentimiento.documento || !consultaVigente || ocupado || cargando) return;
    setOcupado(true); setError(''); setAviso('');
    try {
      const r = await post<ApiOk<ConsentimientoKyc>>('/api/usuarios/alta-publica/consentimiento-kyc', {
        autorizaBiometria: true, versionDocumento: consentimiento.documento.version,
      });
      if (!r.success || !r.data?.aceptado) throw new Error('No se pudo confirmar la autorización.');
      setConsentimiento(r.data); setAutoriza(false);
      setAviso('Autorización guardada. Ahora puedes iniciar la verificación de identidad.');
    } catch (err) {
      setConsultaVigente(false); setAutoriza(false);
      setError(err instanceof HttpUncertainError
        ? 'No pudimos confirmar si se guardó la autorización. Actualiza el estado antes de intentarlo otra vez.'
        : 'No se pudo guardar la autorización. Actualiza el documento y revísalo de nuevo.');
    } finally { setOcupado(false); }
  }

  async function iniciarVerificacion() {
    if (!consentimiento?.aceptado || !estado?.emailVerificado || !kyc || !consultaVigente || ocupado || cargando
      || !['NO_INICIADO', 'RECHAZADO', 'EXPIRADO', 'ERROR'].includes(kyc.estadoKyc)) return;
    setOcupado(true); setError(''); setAviso('');
    secuencia.current++;
    try {
      const r = await post<ApiOk<EstadoKyc>>('/api/kyc/veriff/session', {});
      if (!r.success || !ESTADOS_KYC.has(r.data?.estadoKyc)) throw new Error('Respuesta no reconocida.');
      setKyc(r.data);
      setAviso(r.data.estadoKyc === 'PENDIENTE'
        ? 'Tu sesión está lista. Pulsa Continuar verificación y ten tu documento a mano.'
        : 'Consulta el estado de tu verificación antes de continuar.');
    } catch (err) {
      setConsultaVigente(false); setKyc(null);
      setError(err instanceof HttpUncertainError
        ? 'No sabemos todavía si se creó tu sesión. Actualiza el estado para recuperarla; no inicies otra.'
        : 'No pudimos iniciar la verificación. Actualiza el estado antes de volver a intentarlo.');
    } finally { setOcupado(false); }
  }

  if (noDisponible || estado?.esRegistroPublico === false) return null;
  const sessionUrl = kyc?.estadoKyc === 'PENDIENTE' ? urlSesionVeriff(kyc.sessionUrl) : null;
  const documentoUrl = urlDocumento(consentimiento?.documento?.url);
  const puedeIniciar = consultaVigente && consentimiento?.habilitado && consentimiento.aceptado && estado?.emailVerificado
    && kyc && ['NO_INICIADO', 'RECHAZADO', 'EXPIRADO', 'ERROR'].includes(kyc.estadoKyc);

  return <section className="profile-section alta-publica" aria-label="Activación de mi Wallet">
    <h3 className="profile-section-title">Activa tu Wallet paso a paso</h3>
    <p>Tu cuenta te permite ingresar a Perfil. Para operar, completa las verificaciones y espera la habilitación de XPAY.</p>
    {cargando && <p role="status">Consultando tu activación...</p>}
    {error && <p role="alert" className="breb-msg-err">{error}</p>}
    {aviso && <p role="status" className="breb-msg-ok">{aviso}</p>}
    {estado && <>
      <ol className="alta-publica-pasos">
        <li><strong>Correo</strong><span>{estado.emailVerificado ? 'Verificado' : 'Pendiente. Verifícalo en Mi información.'}</span></li>
        <li><strong>WhatsApp</strong><span>{estado.whatsAppVerificado ? 'Verificado' : 'Pendiente. Verifícalo en Mi información cuando el servicio esté disponible.'}</span></li>
        <li><strong>Identidad</strong><span>{cargando ? 'Consultando...' : kyc ? ETIQUETAS_KYC[kyc.estadoKyc] : 'Sin confirmar'}</span></li>
        <li><strong>Revisión de XPAY</strong><span>{estado.habilitadaPiloto ? 'Habilitada' : 'Pendiente de verificar los requisitos y la pertenencia a tu empresa.'}</span></li>
      </ol>
      {!estado.habilitadaPiloto && <p>La empresa debe incluirte en su base de empleados. XPAY revisará la coincidencia con tu documento verificado antes de habilitar el anticipo de nómina. Registrarte o verificar tu identidad no asigna un cupo automáticamente.</p>}
      {estado.pendientes.length > 0 && <div className="alta-publica-pendientes"><strong>Para continuar</strong><ul>{estado.pendientes.map((pendiente, i) => <li key={`${i}-${pendiente}`}>{pendiente}</li>)}</ul></div>}
      {estado.habilitadaPiloto && <div className="alta-publica-lista">
        <p>XPAY habilitó tu cuenta para el piloto. Vuelve a ingresar para abrir tu Wallet y crear tu PIN transaccional de 6 dígitos.</p>
        <button type="button" className="profile-btn-primary" onClick={() => { logout(); navigate('/login'); }}>Ingresar a mi Wallet</button>
      </div>}
    </>}
    {!estado?.habilitadaPiloto && consentimiento && <div className="alta-publica-identidad">
      <h4>Verifica tu identidad</h4>
      <p>Esta autorización es opcional para conservar tu cuenta y necesaria para iniciar Veriff. No autoriza consultas de crédito ni publicidad.</p>
      {documentoUrl && consentimiento.documento && <a href={documentoUrl} target="_blank" rel="noopener noreferrer">Leer autorizaciones de registro y KYC · {consentimiento.documento.version}</a>}
      {!consentimiento.habilitado && <p>La verificación de identidad estará disponible cuando XPAY complete su configuración.</p>}
      {consentimiento.habilitado && !documentoUrl && <p role="alert">No pudimos cargar el documento de autorización. Actualiza el estado para continuar.</p>}
      {!consentimiento.aceptado && consentimiento.habilitado && documentoUrl && <>
        <label className="profile-checkbox alta-publica-consentimiento">
          <input type="checkbox" checked={autoriza} disabled={ocupado || cargando || !consultaVigente} onChange={e => setAutoriza(e.target.checked)} />
          <span>{consentimiento.textoCasilla}</span>
        </label>
        <button type="button" className="profile-btn-primary" disabled={!autoriza || ocupado || cargando || !consultaVigente} onClick={() => void guardarConsentimiento()}>Guardar autorización</button>
      </>}
      {consentimiento.aceptado && <p>Autorización biométrica registrada.</p>}
      {puedeIniciar && <button type="button" className="profile-btn-primary" disabled={ocupado || cargando} onClick={() => void iniciarVerificacion()}>Iniciar verificación de identidad</button>}
      {!cargando && kyc?.estadoKyc === 'PENDIENTE' && (sessionUrl
        ? <a className="profile-btn-primary alta-publica-continuar" href={sessionUrl} target="_blank" rel="noopener noreferrer">Continuar verificación</a>
        : <p>El enlace para continuar no está disponible. Actualiza el estado para recuperar tu sesión.</p>)}
      {!cargando && kyc?.estadoKyc === 'EN_REVISION' && <p>Veriff está revisando tu información. Consulta nuevamente el estado más adelante.</p>}
      {!cargando && kyc?.estadoKyc === 'APROBADO' && <p>Tu identidad fue aprobada. La habilitación de la Wallet y del anticipo sigue sujeta a los demás requisitos y a la revisión de XPAY.</p>}
    </div>}
    <button type="button" className="profile-btn-secondary" disabled={cargando || ocupado} onClick={() => void cargar()}>{cargando ? 'Actualizando activación...' : 'Actualizar estado de activación'}</button>
  </section>;
}
