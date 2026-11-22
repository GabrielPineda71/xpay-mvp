import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError, get, patch, post } from '../../api/client.ts';
import { useAuth, validarSesionActualizada } from '../../auth/AuthContext.tsx';
import { PreparacionDatosForm } from './PreparacionDatosForm.tsx';
import { PinTransaccionalSection } from './PinTransaccionalSection.tsx';
import { VerificacionIdentidadPublicaSection } from './VerificacionIdentidadPublicaSection.tsx';
import { VerificarContacto } from './VerificarContacto.tsx';
import { MiInformacionSection } from './MiInformacionSection.tsx';
import { ComunicacionesSection } from './ComunicacionesSection.tsx';
import { CambiarContrasenaSection } from './CambiarContrasenaSection.tsx';
import { preparacionWalletValida, puedeSolicitarActivacion, urlTerminosWallet, urlPoliticaDatosWallet, terminosWalletValidos, politicaDatosWalletValida, documentosWalletValidos, TEXTO_TERMINOS_WALLET, TEXTO_DATOS_WALLET, type EstadoPreparacionWallet, type PasoPreparacionWallet } from './preparacionWallet.ts';
import './PreparacionWalletSection.css';

interface ApiOk<T> { success: boolean; data: T; }
const RUTA = '/api/usuarios/preparacion-wallet';
const ETAPAS = ['Correo', 'Identidad', 'Tus datos', 'PIN', 'Wallet'];
const INDICE: Record<PasoPreparacionWallet, number> = { CORREO: 0, IDENTIDAD: 1, DATOS: 2, PIN: 3, ACTIVAR: 4, WALLET: 4, REVISION: 1 };
const TITULOS: Record<PasoPreparacionWallet, string> = {
  CORREO: 'Confirma tu correo', IDENTIDAD: 'Verifica tu identidad', DATOS: '¿Dónde vives?',
  PIN: 'Crea tu PIN de 6 dígitos', ACTIVAR: 'Tu registro está listo',
  WALLET: 'Tu Wallet está habilitada', REVISION: 'Estamos revisando tu registro',
};

// El siguiente paso siempre proviene del servidor. Ninguna consulta inicial,
// retorno de Veriff o guardado de PIN dispara la habilitación de la Wallet.
export function PreparacionWalletSection({ fallback }: { fallback: ReactNode }) {
  const { user, actualizarSesion, logout } = useAuth();
  const navigate = useNavigate();
  const [estado, setEstado] = useState<EstadoPreparacionWallet | null>(null);
  const [anterior, setAnterior] = useState(false);
  const [cargando, setCargando] = useState(true);
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState('');
  const [reingresar, setReingresar] = useState(false);
  const [aceptaTerminos, setAceptaTerminos] = useState(false);
  const [autorizaDatos, setAutorizaDatos] = useState(false);
  const [configuracionAbierta, setConfiguracionAbierta] = useState(false);
  const secuencia = useRef(0);
  const enviando = useRef(false);
  const requiereReingreso = useRef(false);
  const titulo = useRef<HTMLHeadingElement>(null);

  const cargar = useCallback(async () => {
    if (enviando.current || requiereReingreso.current) return;
    const intento = ++secuencia.current;
    setCargando(true); setError('');
    try {
      const r = await get<ApiOk<unknown>>(RUTA);
      if (intento !== secuencia.current) return;
      if (!r.success || !preparacionWalletValida(r.data)) throw new Error('Estado no reconocido.');
      setAnterior(!r.data.habilitado || !r.data.esRegistroPublico);
      setEstado(r.data);
    } catch (err) {
      if (intento !== secuencia.current) return;
      if (err instanceof ApiError && err.status === 404) { setAnterior(true); setEstado(null); }
      else { setEstado(null); setError('No pudimos confirmar tu registro. Actualiza el estado para continuar.'); }
    } finally { if (intento === secuencia.current) setCargando(false); }
  }, []);

  useEffect(() => { void cargar(); return () => { secuencia.current++; }; }, [cargar]);
  useEffect(() => {
    const refrescar = () => { if (document.visibilityState === 'visible') void cargar(); };
    window.addEventListener('focus', refrescar);
    document.addEventListener('visibilitychange', refrescar);
    return () => { window.removeEventListener('focus', refrescar); document.removeEventListener('visibilitychange', refrescar); };
  }, [cargar]);
  const pinActualizado = useCallback(() => { void cargar(); }, [cargar]);
  useEffect(() => { setAceptaTerminos(false); }, [estado?.terminos?.version, estado?.terminos?.url, estado?.terminos?.sha256, estado?.terminos?.aceptados]);
  useEffect(() => { setAutorizaDatos(false); }, [estado?.politicaDatos?.version, estado?.politicaDatos?.url, estado?.politicaDatos?.sha256, estado?.politicaDatos?.aceptados]);
  useEffect(() => { titulo.current?.focus(); }, [estado?.siguientePaso]);

  async function guardar(tipo: 'datos' | 'identidad', body: unknown): Promise<boolean> {
    if (!estado || enviando.current || cargando || requiereReingreso.current) return false;
    if (tipo === 'identidad' && (estado.siguientePaso !== 'REVISION' || estado.puedeActualizarIdentidad !== true || estado.estadoKyc !== 'APROBADO')) return false;
    const intento = ++secuencia.current;
    enviando.current = true; setOcupado(true); setError('');
    try {
      const r = tipo === 'datos' ? await patch<ApiOk<unknown>>(`${RUTA}/datos`, body)
        : await post<ApiOk<unknown>>(`${RUTA}/identidad/actualizar`, {});
      if (intento !== secuencia.current) return false;
      if (!r.success || !preparacionWalletValida(r.data) || !r.data.habilitado || !r.data.esRegistroPublico) throw new Error('Estado no confirmado.');
      setEstado(r.data);
      return true;
    } catch (err) {
      if (intento !== secuencia.current) return false;
      // También un fallo de red puede ocultar un guardado exitoso. Se exige
      // consultar antes de volver a enviar; nunca se repite automáticamente.
      setEstado(null);
      setError(err instanceof ApiError && err.status >= 400 && err.status < 500 && err.status !== 401
        ? `${err.message} Actualiza el estado antes de continuar.`
        : tipo === 'identidad' ? 'No pudimos confirmar la actualización de tu identificación. Actualiza el estado antes de intentarlo otra vez.'
          : 'No pudimos confirmar el guardado. Actualiza el estado antes de intentarlo otra vez.');
      return false;
    } finally { enviando.current = false; setOcupado(false); }
  }

  async function activar() {
    if (!estado || !user || !documentosWalletValidos(estado) || !(estado.terminos!.aceptados || aceptaTerminos)
      || !(estado.politicaDatos!.aceptados || autorizaDatos) || enviando.current || cargando || requiereReingreso.current
      || !(puedeSolicitarActivacion(estado) || (estado.siguientePaso === 'WALLET' && estado.walletHabilitada))) return;
    const intento = ++secuencia.current;
    enviando.current = true; setOcupado(true); setError('');
    try {
      const r = await post<ApiOk<unknown>>(`${RUTA}/activar`, {
        aceptaTerminos: !estado.terminos!.aceptados,
        ...(!estado.terminos!.aceptados ? { versionTerminos: estado.terminos!.version } : {}),
        autorizaDatos: !estado.politicaDatos!.aceptados,
        ...(!estado.politicaDatos!.aceptados ? { versionPoliticaDatos: estado.politicaDatos!.version } : {}),
      });
      if (intento !== secuencia.current) return;
      if (!r.success) throw new Error('Sesión no confirmada.');
      const sesion = validarSesionActualizada(r.data, user);
      actualizarSesion(sesion);
      navigate('/mi-wallet', { replace: true });
    } catch (err) {
      if (intento !== secuencia.current) return;
      setEstado(null); setAceptaTerminos(false); setAutorizaDatos(false);
      if (err instanceof ApiError && [400, 403, 409, 422, 429].includes(err.status)) {
        setError(`${err.message} Actualiza el estado antes de continuar.`);
      } else {
        requiereReingreso.current = true; setReingresar(true);
        setError('No pudimos confirmar la habilitación de tu Wallet. Vuelve a ingresar para consultar su estado.');
      }
    } finally { enviando.current = false; setOcupado(false); }
  }

  if (anterior) return <>{fallback}</>;
  const paso = estado?.siguientePaso;
  const terminos = estado?.terminos;
  const politica = estado?.politicaDatos;
  const aceptacionesListas = (terminos?.aceptados || aceptaTerminos) && (politica?.aceptados || autorizaDatos);
  const terminosPendientes = terminos && terminosWalletValidos(terminos) && !terminos.aceptados ? <div className="onboarding-terminos">
    <a href={urlTerminosWallet(terminos.url)!} target="_blank" rel="noopener noreferrer">Leer Términos y Condiciones · {terminos.version}</a>
    <label className="onboarding-check"><input type="checkbox" checked={aceptaTerminos} disabled={ocupado || cargando} onChange={e => setAceptaTerminos(e.target.checked)} /><span>{TEXTO_TERMINOS_WALLET}</span></label>
  </div> : null;
  const datosPendientes = politica && politicaDatosWalletValida(politica) && !politica.aceptados ? <div className="onboarding-terminos">
    <a href={urlPoliticaDatosWallet(politica.url)!} target="_blank" rel="noopener noreferrer">Leer Política de Tratamiento de Datos Personales · {politica.version}</a>
    <label className="onboarding-check"><input type="checkbox" checked={autorizaDatos} disabled={ocupado || cargando} onChange={e => setAutorizaDatos(e.target.checked)} /><span>{TEXTO_DATOS_WALLET}</span></label>
  </div> : null;
  return <div className="onboarding-page">
    <div className="onboarding-intro"><span className="onboarding-eyebrow">BIENVENIDO A XPAY</span>
      <h1>Prepara tu Wallet</h1><p>Completa un paso a la vez. Guardaremos tu avance.</p></div>
    {estado && paso && <>
      <nav aria-label="Tu avance" className="onboarding-progress"><ol>{ETAPAS.map((etapa, i) => <li key={etapa} aria-current={i === INDICE[paso] ? 'step' : undefined} className={i < INDICE[paso] ? 'completo' : ''}><span aria-hidden="true">{i < INDICE[paso] ? '✓' : i + 1}</span><small>{etapa}</small></li>)}</ol></nav>
      <section className="onboarding-card" aria-labelledby="onboarding-titulo" aria-busy={ocupado || cargando}>
        <p className="onboarding-step">{paso === 'REVISION' ? 'REVISIÓN DE XPAY' : `PASO ${INDICE[paso] + 1} DE 5`}</p>
        <h2 id="onboarding-titulo" tabIndex={-1} ref={titulo}>{TITULOS[paso]}</h2>
        {paso === 'CORREO' && <><p>Revisa tu correo para confirmar tu cuenta. Si necesitas un nuevo código, solicítalo aquí.</p><VerificarContacto canal="email" onVerificado={() => void cargar()} /></>}
        {paso === 'IDENTIDAD' && <><p>Ten tu documento a mano. Veriff te guiará para verificarlo y tomar una foto de tu rostro.</p><VerificacionIdentidadPublicaSection guiado onActualizado={() => void cargar()} /></>}
        {paso === 'DATOS' && <><p>Tu correo y tu identidad ya están verificados. Completa tu dirección para continuar.</p><PreparacionDatosForm key={`${estado.idDepartamento ?? ''}:${estado.idCiudad ?? ''}`} estado={estado} ocupado={ocupado || cargando} onGuardar={datos => guardar('datos', datos)} /></>}
        {paso === 'PIN' && <PinTransaccionalSection compacto obligatorio notificarCargaInicial={false} onActualizado={pinActualizado} />}
        {paso === 'ACTIVAR' && <><p>Confirmaste tu correo, verificaste tu identidad y completaste tus datos y tu PIN.</p>
          {terminosPendientes}
          {datosPendientes}
          <button type="button" className="onboarding-primary" disabled={ocupado || cargando || !aceptacionesListas || !puedeSolicitarActivacion(estado)} onClick={() => void activar()}>{ocupado ? 'Habilitando...' : 'Activar mi Wallet'}</button>
          <small>Los cupos y anticipos de nómina requieren una aprobación adicional. No se asignan al activar tu Wallet.</small></>}
        {paso === 'WALLET' && <><p>Vuelve a abrir tu Wallet con tu sesión actualizada.</p>{terminosPendientes}{datosPendientes}<button type="button" className="onboarding-primary" disabled={ocupado || cargando || !aceptacionesListas} onClick={() => void activar()}>{ocupado ? 'Abriendo...' : 'Entrar a mi Wallet'}</button></>}
        {paso === 'REVISION' && (estado.puedeActualizarIdentidad === true && estado.estadoKyc === 'APROBADO'
          ? <><p>Tu verificación está aprobada. Actualiza tu identificación para continuar.</p><button type="button" className="onboarding-primary" disabled={ocupado || cargando} onClick={() => void guardar('identidad', {})}>{ocupado ? 'Actualizando...' : 'Actualizar mi identificación'}</button></>
          : <p>{estado.mensaje || 'XPAY debe revisar tu información antes de continuar. No necesitas repetir la verificación. Contacta a XPAY si necesitas ayuda.'}</p>)}
      </section>
    </>}
    {cargando && <p className="onboarding-status" role="status">Consultando tu avance...</p>}
    {error && <div className="onboarding-alert" role="alert">{error}</div>}
    {reingresar ? <button type="button" className="onboarding-primary" onClick={() => { logout(); navigate('/login', { replace: true }); }}>Volver a ingresar</button>
      : <button type="button" className="onboarding-refresh" disabled={cargando || ocupado} onClick={() => void cargar()}>Actualizar mi avance</button>}
    <details className="onboarding-config" onToggle={e => setConfiguracionAbierta(e.currentTarget.open)}>
      <summary>Mi perfil y configuración</summary>
      {configuracionAbierta && <div>
        <p>Puedes conservar tu cuenta y actualizar tu Perfil sin autorizar la biometría.</p>
        <MiInformacionSection whatsAppPendiente onActualizado={() => void cargar()} />
        <ComunicacionesSection />
        <CambiarContrasenaSection />
      </div>}
    </details>
  </div>;
}
