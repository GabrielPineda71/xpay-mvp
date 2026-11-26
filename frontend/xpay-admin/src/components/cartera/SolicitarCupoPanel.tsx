import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, get, post } from '../../api/client.ts';
import './SolicitarCupoPanel.css';

const BASE = '/api/cartera-ordinaria';
const STORAGE = 'xpay_solicitud_cupo:';
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
interface Disponibilidad { disponible: boolean; habilitado: boolean; requierePrimerApellido: boolean; estado: string; mensaje: string; }
interface Solicitud { idSolicitud: number; estadoSolicitud: string; decisionCrediticia: string; consentimientoRegistrado: boolean; }
interface Texto { version: string; texto: string; hash: string; }
interface Intencion { key: string; creacionPendiente: boolean; solicitudId: number | null; evaluacionIntentada: boolean; }

// Solo identifica el espacio local de la intención. La API determina el titular
// exclusivamente desde la sesión autenticada; ningún ID se envía en el body.
function actorSesion(): string | null {
  try {
    const usuario = JSON.parse(localStorage.getItem('xpay_user') ?? 'null');
    return usuario && Number.isSafeInteger(usuario.idUsuario) && usuario.idUsuario > 0
      && Number.isSafeInteger(usuario.idPersona) && usuario.idPersona > 0
      ? `${usuario.idUsuario}:${usuario.idPersona}` : null;
  } catch { return null; }
}

function leerIntencion(actor: string): Intencion | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(STORAGE + actor) ?? 'null');
    if (value && typeof value.key === 'string' && GUID.test(value.key)
      && value.key !== '00000000-0000-0000-0000-000000000000'
      && typeof value.creacionPendiente === 'boolean' && typeof value.evaluacionIntentada === 'boolean'
      && (value.solicitudId === null || Number.isSafeInteger(value.solicitudId) && value.solicitudId > 0)) return value;
  } catch { /* Un dato local inválido nunca se convierte en una petición. */ }
  return null;
}

function guardarIntencion(actor: string, value: Intencion): void {
  sessionStorage.setItem(STORAGE + actor, JSON.stringify(value));
}

function solicitudSegura(value: unknown): Solicitud {
  const s = value as Partial<Solicitud> | null;
  if (!s || !Number.isSafeInteger(s.idSolicitud) || s.idSolicitud! <= 0
    || typeof s.estadoSolicitud !== 'string' || typeof s.decisionCrediticia !== 'string') throw new Error('Estado no confirmado.');
  return { idSolicitud: s.idSolicitud!, estadoSolicitud: s.estadoSolicitud,
    decisionCrediticia: s.decisionCrediticia, consentimientoRegistrado: s.consentimientoRegistrado === true };
}

function textoSeguro(value: unknown): Texto {
  const t = value as Partial<Texto> | null;
  if (!t || typeof t.version !== 'string' || !t.version.trim() || t.version.length > 60
    || typeof t.texto !== 'string' || !t.texto.trim() || t.texto.length > 20000
    || typeof t.hash !== 'string' || !/^[0-9a-f]{64}$/i.test(t.hash)) throw new Error('Autorización no disponible.');
  return { version: t.version, texto: t.texto, hash: t.hash };
}

function mensajeEstado(s: Solicitud): string {
  switch (s.estadoSolicitud) {
    case 'RECIBIDA': return 'Tu solicitud está lista para que revises la autorización.';
    case 'VALIDANDO':
    case 'CONSULTANDO_RIESGO':
    case 'EN_EVALUACION':
    case 'APROBADA_PENDIENTE_CUPO': return 'Tu solicitud se está procesando. Actualiza el estado para ver el resultado.';
    case 'APROBADA': return 'Tu solicitud fue aprobada. Consulta tu cupo actualizado en esta pantalla.';
    case 'RECHAZADA': return 'Tu solicitud no fue aprobada.';
    case 'ERROR_PROVEEDOR': return 'No pudimos completar la evaluación. Tu solicitud requiere revisión de XPAY.';
    default: return 'Tu solicitud requiere revisión de XPAY.';
  }
}

export function SolicitarCupoPanel({ actor: actorProp, cupoActivo = false, onCupoActualizado }: {
  actor?: string | null;
  cupoActivo?: boolean;
  onCupoActualizado?: () => void;
}) {
  const actor = actorProp === undefined ? actorSesion() : actorProp;
  const [disponibilidad, setDisponibilidad] = useState<Disponibilidad | null>(null);
  const [solicitud, setSolicitud] = useState<Solicitud | null>(null);
  const [texto, setTexto] = useState<Texto | null>(null);
  const [acepta, setAcepta] = useState(false);
  const [cargando, setCargando] = useState(true);
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState('');
  const [recuperacion, setRecuperacion] = useState(false);
  const [ausenciaConfirmada, setAusenciaConfirmada] = useState(false);
  const [intencion, setIntencion] = useState<Intencion | null>(null);
  const generacion = useRef(0);
  const operando = useRef(false);
  const aprobadaNotificada = useRef<number | null>(null);
  const cupoActualizado = useRef(onCupoActualizado);
  cupoActualizado.current = onCupoActualizado;

  function adoptar(s: Solicitud | null) {
    setSolicitud(s);
    if (s?.estadoSolicitud === 'APROBADA' && aprobadaNotificada.current !== s.idSolicitud) {
      aprobadaNotificada.current = s.idSolicitud;
      cupoActualizado.current?.();
    }
  }

  async function cargar(v = generacion.current) {
    if (!actor) return;
    setCargando(true); setAcepta(false); setTexto(null); setError(''); setAusenciaConfirmada(false);
    try {
      const d = await get<Disponibilidad>(`${BASE}/disponibilidad`);
      if (v !== generacion.current) return;
      if (!d || typeof d.disponible !== 'boolean' || typeof d.habilitado !== 'boolean'
        || typeof d.requierePrimerApellido !== 'boolean') throw new Error('Disponibilidad no confirmada.');
      setDisponibilidad(d);
      let s: Solicitud | null = null;
      let actualConfirmado = false;
      try {
        const actual = await get<{ solicitud: unknown }>(`${BASE}/mi-solicitud-actual`);
        if (!actual || !Object.prototype.hasOwnProperty.call(actual, 'solicitud')) throw new Error('Estado no confirmado.');
        s = actual.solicitud === null ? null : solicitudSegura(actual.solicitud);
        actualConfirmado = true;
      } catch (err) {
        if (d.disponible || d.habilitado || !(err instanceof ApiError) || ![403, 404].includes(err.status)) throw err;
      }
      if (v !== generacion.current) return;
      let local = leerIntencion(actor);
      if (s && local?.creacionPendiente && (local.solicitudId === null || d.habilitado)) {
        local = { ...local, creacionPendiente: false, solicitudId: s.idSolicitud };
        guardarIntencion(actor, local);
      }
      setIntencion(local);
      setAusenciaConfirmada(actualConfirmado && s === null);
      setRecuperacion(local?.creacionPendiente === true);
      adoptar(s);
      if (s?.estadoSolicitud === 'RECIBIDA' && d.disponible && d.habilitado
        && !(local?.evaluacionIntentada && local.solicitudId === s.idSolicitud)) {
        const vigente = textoSeguro(await get<unknown>(`${BASE}/autorizacion-consulta-riesgo/texto-vigente`));
        if (v === generacion.current) setTexto(vigente);
      }
    } catch {
      if (v === generacion.current) {
        setError('No pudimos confirmar el estado de tu solicitud. Actualiza el estado antes de continuar.');
        setRecuperacion(true);
      }
    } finally { if (v === generacion.current) setCargando(false); }
  }

  useEffect(() => {
    const v = ++generacion.current;
    operando.current = false; aprobadaNotificada.current = null;
    setOcupado(false); setAcepta(false); setTexto(null); setError(''); setCargando(true);
    setDisponibilidad(null); setSolicitud(null); setIntencion(null); setRecuperacion(false); setAusenciaConfirmada(false);
    if (actor) void cargar(v);
    return () => { generacion.current++; };
    // El actor delimita toda la recuperación. Ningún cambio visual repite POST.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [actor]);

  async function solicitar(continuar = false, reintentar = false) {
    if (!actor || operando.current || cargando || !disponibilidad?.disponible || cupoActivo) return;
    if (recuperacion && !(reintentar && ausenciaConfirmada && !solicitud && intencion?.creacionPendiente)) return;
    if (continuar) {
      if (solicitud?.estadoSolicitud !== 'RECIBIDA' || disponibilidad.habilitado
        || intencion?.evaluacionIntentada && intencion.solicitudId === solicitud.idSolicitud) return;
    } else if (solicitud) return;
    const v = generacion.current;
    operando.current = true; setOcupado(true); setError(''); setAusenciaConfirmada(false);
    try {
      const guardada = leerIntencion(actor);
      if (reintentar && (!guardada || guardada.key !== intencion?.key || !guardada.creacionPendiente))
        throw new Error('La intención no está confirmada.');
      const local = guardada ?? { key: crypto.randomUUID(), creacionPendiente: false, solicitudId: null, evaluacionIntentada: false };
      const pendiente = { ...local, creacionPendiente: true, solicitudId: continuar ? solicitud!.idSolicitud : null };
      guardarIntencion(actor, pendiente); setIntencion(pendiente);
      const creada = solicitudSegura(await post<unknown>(`${BASE}/solicitar-cupo`, undefined, { 'Idempotency-Key': local.key }));
      if (continuar && creada.idSolicitud !== solicitud!.idSolicitud) throw new Error('Estado no confirmado.');
      const confirmada = { ...pendiente, creacionPendiente: false, solicitudId: creada.idSolicitud };
      guardarIntencion(actor, confirmada);
      if (v !== generacion.current) return;
      setIntencion(confirmada); adoptar(creada);
      await cargar(v);
    } catch (err) {
      if (v === generacion.current) {
        if ((continuar || reintentar) && err instanceof ApiError && err.status === 409) {
          // El filtro puede incorporar al titular antes de que la creación
          // informe que su solicitud ya existe. Se confirma únicamente por GET.
          await cargar(v);
        } else {
          setError('No pudimos confirmar la solicitud. Actualiza el estado antes de continuar.');
          setRecuperacion(true);
        }
      }
    } finally { if (v === generacion.current) { operando.current = false; setOcupado(false); } }
  }

  async function aceptarYConsultar() {
    if (!actor || operando.current || cargando || recuperacion || !acepta || !texto
      || !disponibilidad?.disponible || !disponibilidad.habilitado || solicitud?.estadoSolicitud !== 'RECIBIDA'
      || intencion?.evaluacionIntentada && intencion.solicitudId === solicitud.idSolicitud) return;
    const v = generacion.current;
    const id = solicitud.idSolicitud;
    const mostrada = texto;
    operando.current = true; setOcupado(true); setError(''); setAcepta(false);
    try {
      const local = leerIntencion(actor) ?? { key: crypto.randomUUID(), creacionPendiente: false, solicitudId: id, evaluacionIntentada: false };
      guardarIntencion(actor, local);
      const autorizacion = await post<{ idSolicitud: number; version: string; resultado: string }>(
        `${BASE}/solicitudes/${id}/autorizar-consulta-riesgo`, { version: mostrada.version });
      if (v !== generacion.current) return;
      if (autorizacion.idSolicitud !== id || autorizacion.version !== mostrada.version
        || !['registrada', 'ya_registrada'].includes(autorizacion.resultado)) throw new Error('Autorización no confirmada.');
      const enviada = { ...local, solicitudId: id, evaluacionIntentada: true };
      guardarIntencion(actor, enviada); setIntencion(enviada);
      const resultado = solicitudSegura(await post<unknown>(`${BASE}/solicitudes/${id}/iniciar-evaluacion`, undefined,
        { 'Idempotency-Key': enviada.key }, 120000));
      if (v !== generacion.current) return;
      if (resultado.idSolicitud !== id) throw new Error('Estado no confirmado.');
      adoptar(resultado);
      await cargar(v);
    } catch {
      if (v === generacion.current) {
        setError('No pudimos confirmar el resultado. Actualiza el estado para revisar tu solicitud.');
        setRecuperacion(true);
        // La recuperación es solo lectura. Nunca repite autorización ni evaluación.
        await cargar(v);
      }
    } finally { if (v === generacion.current) { operando.current = false; setOcupado(false); } }
  }

  async function continuarEvaluacion() {
    if (!actor || operando.current || cargando || recuperacion || !disponibilidad?.disponible
      || !disponibilidad.habilitado || !solicitud
      || !['EN_EVALUACION', 'APROBADA_PENDIENTE_CUPO'].includes(solicitud.estadoSolicitud)) return;
    const v = generacion.current;
    const id = solicitud.idSolicitud;
    operando.current = true; setOcupado(true); setError('');
    try {
      // Solo se reanudan pasos posteriores a la consulta ya persistida. Una
      // lectura nueva confirma el estado antes del POST explícito.
      const actual = await get<{ solicitud: unknown }>(`${BASE}/mi-solicitud-actual`);
      if (v !== generacion.current) return;
      const confirmada = solicitudSegura(actual?.solicitud);
      if (confirmada.idSolicitud !== id || !['EN_EVALUACION', 'APROBADA_PENDIENTE_CUPO'].includes(confirmada.estadoSolicitud)) {
        await cargar(v); return;
      }
      const local = leerIntencion(actor) ?? { key: crypto.randomUUID(), creacionPendiente: false, solicitudId: id, evaluacionIntentada: false };
      const enviada = { ...local, creacionPendiente: false, solicitudId: id, evaluacionIntentada: true };
      guardarIntencion(actor, enviada); setIntencion(enviada);
      const resultado = solicitudSegura(await post<unknown>(`${BASE}/solicitudes/${id}/iniciar-evaluacion`, undefined,
        { 'Idempotency-Key': enviada.key }, 120000));
      if (v !== generacion.current) return;
      if (resultado.idSolicitud !== id) throw new Error('Estado no confirmado.');
      adoptar(resultado);
      await cargar(v);
    } catch {
      if (v === generacion.current) {
        setError('No pudimos confirmar el resultado. Actualiza el estado para revisar tu solicitud.');
        setRecuperacion(true);
        await cargar(v);
      }
    } finally { if (v === generacion.current) { operando.current = false; setOcupado(false); } }
  }

  if (!actor) return null;
  const intentoEnviado = !!solicitud && intencion?.evaluacionIntentada === true && intencion.solicitudId === solicitud.idSolicitud;
  const puedeContinuar = disponibilidad?.disponible && !disponibilidad.habilitado
    && solicitud?.estadoSolicitud === 'RECIBIDA' && !cupoActivo && !intentoEnviado && !recuperacion;
  const puedeAutorizar = disponibilidad?.disponible && disponibilidad.habilitado && solicitud?.estadoSolicitud === 'RECIBIDA'
    && !intentoEnviado && !recuperacion;
  const puedeReintentarSolicitud = !solicitud && !cupoActivo && disponibilidad?.disponible
    && ausenciaConfirmada && intencion?.creacionPendiente === true;
  const puedeReanudar = disponibilidad?.disponible && disponibilidad.habilitado && solicitud
    && ['EN_EVALUACION', 'APROBADA_PENDIENTE_CUPO'].includes(solicitud.estadoSolicitud) && !recuperacion;
  return <section className="cartera-solicitud" aria-labelledby="solicitud-cupo-titulo" aria-busy={cargando || ocupado}>
    <h3 id="solicitud-cupo-titulo">Solicitar cupo</h3>
    {cargando ? <p role="status">Consultando el estado de tu solicitud…</p> : <>
      {error && <p role="alert">{error}</p>}
      {solicitud ? <p role="status">{intentoEnviado && solicitud.estadoSolicitud === 'RECIBIDA'
        ? 'Estamos confirmando tu evaluación. Actualiza el estado para revisar el resultado.'
        : puedeContinuar ? 'Tu solicitud está guardada. Continúa para revisar la autorización.' : mensajeEstado(solicitud)}</p>
        : cupoActivo ? <p>Tu cupo está disponible en esta pantalla.</p>
          : disponibilidad?.disponible ? <p>Solicita la evaluación de tu cupo. El resultado se determina al evaluar tu solicitud.</p>
            : <p>{disponibilidad?.mensaje || 'La solicitud de cupo no está disponible en este momento.'}</p>}
      {disponibilidad?.requierePrimerApellido && <Link to="/mi-wallet/perfil">Completar mi primer apellido</Link>}
      {!disponibilidad?.disponible && !disponibilidad?.requierePrimerApellido && <Link to="/mi-wallet/perfil">Revisar mi perfil</Link>}
      {!solicitud && !cupoActivo && disponibilidad?.disponible && !recuperacion
        && <button type="button" className="btn-breb" disabled={ocupado} onClick={() => void solicitar()}>{ocupado ? 'Preparando solicitud…' : 'Solicitar mi cupo'}</button>}
      {puedeReintentarSolicitud && <button type="button" className="btn-breb" disabled={ocupado}
        onClick={() => void solicitar(false, true)}>{ocupado ? 'Recuperando solicitud…' : 'Reintentar solicitud'}</button>}
      {puedeContinuar && <button type="button" className="btn-breb" disabled={ocupado}
        onClick={() => void solicitar(true)}>{ocupado ? 'Recuperando solicitud…' : 'Continuar mi solicitud'}</button>}
      {puedeReanudar && <button type="button" className="btn-breb" disabled={ocupado}
        onClick={() => void continuarEvaluacion()}>{ocupado ? 'Continuando evaluación…' : 'Continuar evaluación'}</button>}
      {puedeAutorizar && texto && <div className="cartera-solicitud-autorizacion">
        <h4>Autorización de consulta en centrales de riesgo</h4>
        <div className="cartera-solicitud-texto">{texto.texto}</div>
        <label><input type="checkbox" checked={acepta} disabled={ocupado} onChange={e => setAcepta(e.target.checked)} /> AUTORIZO</label>
        <button type="button" className="btn-breb" disabled={!acepta || ocupado} onClick={() => void aceptarYConsultar()}>{ocupado ? 'Consultando…' : 'Aceptar y consultar'}</button>
      </div>}
      <button key="actualizar" type="button" className="wallet-send-link-btn" disabled={ocupado} onClick={() => void cargar()}>Actualizar estado</button>
    </>}
  </section>;
}
