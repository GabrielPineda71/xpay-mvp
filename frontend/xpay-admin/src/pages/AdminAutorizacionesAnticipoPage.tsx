import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ApiError, get, HttpUncertainError, post } from '../api/client.ts';
import { useAuth } from '../auth/AuthContext.tsx';
import './AdminVinculacionesNominaPage.css';
import './AdminAutorizacionesAnticipoPage.css';

interface ApiOk<T> { success: boolean; data: T; }
interface Convenio { idConvenio: number; nombreEmpresa: string; }
export interface AutorizacionAnticipo {
  idEmpleadoUsuario: number;
  idEmpleado: number;
  idUsuario: number;
  idConvenio: number;
  empresa: string;
  empleado: string;
  usuario: string;
  documentoMasked: string;
  emailVerificado: boolean;
  identidadVerificada: boolean;
  walletActiva: boolean;
  origenPublico: boolean;
  estado: 'PENDIENTE' | 'AUTORIZADO' | 'REVOCADO' | 'FLUJO_PREVIO' | 'REVISION_REQUERIDA';
  puedeAutorizar: boolean;
  puedeRevocar: boolean;
  razones: string[];
  huella: string;
  fechaAutorizacion?: string | null;
  fechaRevocacion?: string | null;
}
interface Bandeja { items: AutorizacionAnticipo[]; pagina: number; total: number; totalPaginas: number; }
type Accion = 'autorizar' | 'revocar';
interface Revision { persona: AutorizacionAnticipo; accion: Accion; }
const ETIQUETAS: Record<AutorizacionAnticipo['estado'], string> = {
  PENDIENTE: 'Pendiente de autorización', AUTORIZADO: 'Autorizado', REVOCADO: 'Revocado',
  FLUJO_PREVIO: 'Acceso previo', REVISION_REQUERIDA: 'Revisar requisitos',
};
const puedeActuar = (p: AutorizacionAnticipo, accion: Accion) => !!p.huella && Object.prototype.hasOwnProperty.call(ETIQUETAS, p.estado)
  && (accion === 'autorizar' ? p.puedeAutorizar && p.estado !== 'AUTORIZADO' : p.puedeRevocar && p.estado !== 'REVOCADO');
const referenciaValida = (s: string) => s.trim().length >= 5 && s.trim().length <= 200 && !/[\u0000-\u001f\u007f]/.test(s);
const fechaLegible = (s: string) => Number.isFinite(new Date(s).getTime())
  ? new Date(s).toLocaleString('es-CO', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'America/Bogota' }) : 'Fecha no disponible';

// La confirmación guarda una sola persona y su huella. Las respuestas de otras
// empresas/sesiones se descartan y ningún fallo provoca repetir una autorización.
export function AdminAutorizacionesAnticipoPage() {
  const { user } = useAuth();
  const autorizado = user?.roles.some(r => r === 'ADMIN_XPAY' || r === 'SUPERUSUARIO') === true;
  const [params, setParams] = useSearchParams();
  const convenioParam = params.get('idConvenio');
  const idConvenio = convenioParam && /^\d+$/.test(convenioParam) && Number.isSafeInteger(Number(convenioParam)) && Number(convenioParam) > 0 ? Number(convenioParam) : null;
  const filtroInvalido = convenioParam !== null && idConvenio === null;
  const [convenios, setConvenios] = useState<Convenio[]>([]);
  const [pagina, setPagina] = useState(1);
  const [bandeja, setBandeja] = useState<Bandeja | null>(null);
  const [revision, setRevision] = useState<Revision | null>(null);
  const [confirma, setConfirma] = useState(false);
  const [referencia, setReferencia] = useState('');
  const [cargando, setCargando] = useState(true);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState('');
  const [mensaje, setMensaje] = useState('');
  const secuencia = useRef(0);
  const montado = useRef(false);
  const envioActivo = useRef(false);
  const contexto = `${user?.idUsuario ?? ''}:${autorizado}:${convenioParam ?? ''}:${pagina}`;
  const contextoActual = useRef(contexto);
  contextoActual.current = contexto;
  const contextoRevision = useRef('');
  const dialogoRef = useRef<HTMLDivElement>(null);
  const tituloRef = useRef<HTMLHeadingElement>(null);
  const origenFoco = useRef<HTMLElement | null>(null);

  useEffect(() => { montado.current = true; return () => { montado.current = false; secuencia.current++; }; }, []);
  const cargar = useCallback(async (): Promise<boolean> => {
    if (!autorizado || filtroInvalido) {
      setBandeja(null); setCargando(false);
      if (filtroInvalido) setError('Selecciona una empresa válida.');
      return false;
    }
    const intento = ++secuencia.current;
    setCargando(true); setError(''); setBandeja(null);
    try {
      const consulta = new URLSearchParams({ pagina: String(pagina), tamanio: '50' });
      if (idConvenio) consulta.set('idConvenio', String(idConvenio));
      const r = await get<ApiOk<Bandeja>>(`/api/libranza/admin/autorizaciones-anticipo?${consulta}`);
      if (!montado.current || intento !== secuencia.current || contextoActual.current !== contexto) return false;
      if (!r.success || !Array.isArray(r.data?.items)) throw new Error('Respuesta no reconocida.');
      setBandeja(r.data); return true;
    } catch (err) {
      if (montado.current && intento === secuencia.current && contextoActual.current === contexto)
        setError(err instanceof Error ? err.message : 'No se pudieron consultar las autorizaciones.');
      return false;
    } finally { if (montado.current && intento === secuencia.current && contextoActual.current === contexto) setCargando(false); }
  }, [autorizado, filtroInvalido, idConvenio, pagina, contexto]);

  useEffect(() => {
    setRevision(null); setConfirma(false); setReferencia(''); setMensaje('');
    void cargar();
    return () => { secuencia.current++; };
  }, [cargar]);
  useEffect(() => {
    if (!autorizado) return;
    let vigente = true;
    void get<ApiOk<Convenio[]>>('/api/libranza/admin/convenios').then(r => {
      if (vigente && r.success && Array.isArray(r.data)) setConvenios(r.data);
    }).catch(() => { /* La consulta de cuentas sigue disponible sin el filtro. */ });
    return () => { vigente = false; };
  }, [autorizado, user?.idUsuario]);
  useEffect(() => {
    if (!revision) return;
    tituloRef.current?.focus();
    return () => { if (origenFoco.current?.isConnected) origenFoco.current.focus(); };
  }, [revision]);

  function abrir(persona: AutorizacionAnticipo, accion: Accion) {
    if (!autorizado || cargando || guardando || envioActivo.current || !puedeActuar(persona, accion)) return;
    origenFoco.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    contextoRevision.current = contexto;
    setRevision({ persona: { ...persona, razones: [...persona.razones] }, accion });
    setConfirma(false); setReferencia(''); setMensaje(''); setError('');
  }
  function cerrar() { if (!guardando) { setRevision(null); setConfirma(false); setReferencia(''); } }
  function manejarTeclado(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') { event.preventDefault(); cerrar(); return; }
    if (event.key !== 'Tab') return;
    const controles = [...(dialogoRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),a[href],select:not(:disabled),[tabindex="0"]') ?? [])];
    if (!controles.length) { event.preventDefault(); return; }
    const indice = controles.indexOf(document.activeElement as HTMLElement);
    if (indice < 0 || (!event.shiftKey && indice === controles.length - 1) || (event.shiftKey && indice === 0)) {
      event.preventDefault(); controles[event.shiftKey ? controles.length - 1 : 0].focus();
    }
  }
  async function confirmar() {
    if (!autorizado || !revision || contextoRevision.current !== contexto || !confirma || !referenciaValida(referencia) || envioActivo.current || !puedeActuar(revision.persona, revision.accion)) return;
    envioActivo.current = true;
    const contextoEnvio = contexto;
    const sigueEnVista = () => montado.current && contextoActual.current === contextoEnvio;
    setGuardando(true); setError(''); setMensaje('');
    try {
      const r = await post<ApiOk<AutorizacionAnticipo>>(`/api/libranza/admin/autorizaciones-anticipo/${revision.accion}`, {
        idEmpleadoUsuario: revision.persona.idEmpleadoUsuario, huella: revision.persona.huella,
        referenciaRevision: referencia.trim(), confirmaAutorizacion: true,
      }, undefined, 60_000);
      if (!sigueEnVista()) return;
      if (!r.success || r.data?.idEmpleadoUsuario !== revision.persona.idEmpleadoUsuario) throw new HttpUncertainError('Resultado no confirmado.');
      const texto = revision.accion === 'autorizar' ? `Autorización registrada para ${revision.persona.usuario}.` : `Acceso revocado para ${revision.persona.usuario}.`;
      setRevision(null); setConfirma(false); setReferencia('');
      await cargar();
      if (sigueEnVista()) setMensaje(texto);
    } catch (err) {
      if (!sigueEnVista()) return;
      setRevision(null); setConfirma(false); setReferencia('');
      const actualizada = await cargar();
      if (!sigueEnVista()) return;
      if (err instanceof ApiError && err.status === 409)
        setError(`La información cambió. ${actualizada ? 'La lista se actualizó; revisa nuevamente a la persona.' : 'Actualiza la lista antes de volver a revisar.'}`);
      else if (err instanceof HttpUncertainError || !(err instanceof ApiError))
        setError(`No pudimos confirmar el resultado. ${actualizada ? 'Revisa el estado actualizado antes de intentarlo otra vez.' : 'Actualiza la lista para comprobar el estado antes de intentarlo otra vez.'}`);
      else setError(err.message);
    } finally { envioActivo.current = false; if (montado.current) setGuardando(false); }
  }

  if (!autorizado) return <div className="page"><h2>Autorizaciones de Anticipo de Nómina</h2><p role="alert">Esta opción está disponible para administradores de XPAY.</p></div>;
  const filas = bandeja?.items ?? [];
  return <div className="page nomina-vinculaciones anticipo-autorizaciones">
    <div className="anticipo-enlaces"><Link to={`/admin/nomina-vinculaciones${idConvenio ? `?idConvenio=${idConvenio}` : ''}`}>← Coincidencias de nómina</Link></div>
    <h2>Autorizaciones de Anticipo de Nómina</h2>
    <p>Autoriza individualmente el uso del producto para las cuentas ya vinculadas. Los límites y controles del convenio siguen vigentes.</p>
    <div className="nomina-filtros">
      <label>Empresa<select aria-label="Empresa" value={idConvenio ?? ''} disabled={guardando || !!revision} onChange={e => { setPagina(1); setParams(e.target.value ? { idConvenio: e.target.value } : {}); }}>
        <option value="">Todas las empresas</option>
        {idConvenio && !convenios.some(c => c.idConvenio === idConvenio) && <option value={idConvenio}>Empresa seleccionada</option>}
        {convenios.map(c => <option key={c.idConvenio} value={c.idConvenio}>{c.nombreEmpresa}</option>)}
      </select></label>
      <button type="button" className="secondary" disabled={cargando || guardando || !!revision} onClick={() => { setMensaje(''); void cargar(); }}>Actualizar autorizaciones</button>
    </div>
    {error && <p className="msg-error" role="alert">{error}</p>}
    {mensaje && <p className="nomina-resultados" role="status">{mensaje}</p>}
    {cargando && <p role="status">Consultando autorizaciones...</p>}
    {!cargando && bandeja && <>
      <p>{bandeja.total} cuenta(s) vinculada(s).</p>
      {!filas.length && <p>No hay cuentas vinculadas para esta consulta. Revisa primero las coincidencias de nómina.</p>}
      <div className="nomina-candidatos">
        {filas.map(p => <article className="nomina-candidato" key={p.idEmpleadoUsuario}>
          <div className="nomina-candidato-cabecera"><h3>{p.empleado}</h3><span className={`badge ${p.estado === 'AUTORIZADO' || p.estado === 'FLUJO_PREVIO' ? 'badge-ok' : 'badge-warn'}`}>{ETIQUETAS[p.estado] ?? 'Revisar estado'}</span></div>
          <dl><div><dt>Empresa</dt><dd>{p.empresa}</dd></div><div><dt>Cuenta XPAY</dt><dd>{p.usuario}</dd></div><div><dt>Documento</dt><dd>{p.documentoMasked}</dd></div></dl>
          <ul className="nomina-verificaciones" aria-label={`Requisitos de ${p.usuario}`}><li>Correo: {p.emailVerificado ? 'verificado' : 'pendiente'}</li><li>Identidad: {p.identidadVerificada ? 'verificada' : 'pendiente'}</li><li>Wallet: {p.walletActiva ? 'activa' : 'pendiente'}</li></ul>
          {p.razones.length > 0 && <ul className="nomina-bloqueos">{p.razones.map((razon, i) => <li key={`${i}-${razon}`}>{razon}</li>)}</ul>}
          {p.fechaAutorizacion && <p className="anticipo-fecha">Última autorización: {fechaLegible(p.fechaAutorizacion)}</p>}
          {p.fechaRevocacion && <p className="anticipo-fecha">Última revocación: {fechaLegible(p.fechaRevocacion)}</p>}
          <div className="anticipo-acciones">
            {puedeActuar(p, 'autorizar') && <button type="button" disabled={guardando || !!revision} onClick={() => abrir(p, 'autorizar')}>Autorizar a {p.usuario}</button>}
            {puedeActuar(p, 'revocar') && <button type="button" className="secondary anticipo-revocar" disabled={guardando || !!revision} onClick={() => abrir(p, 'revocar')}>Revocar a {p.usuario}</button>}
          </div>
        </article>)}
      </div>
      {bandeja.totalPaginas > 1 && <nav className="nomina-paginacion" aria-label="Páginas de autorizaciones">
        <button type="button" disabled={pagina <= 1 || guardando || !!revision} onClick={() => setPagina(p => p - 1)}>Anterior</button>
        <span>Página {bandeja.pagina} de {bandeja.totalPaginas}</span>
        <button type="button" disabled={pagina >= bandeja.totalPaginas || guardando || !!revision} onClick={() => setPagina(p => p + 1)}>Siguiente</button>
      </nav>}
    </>}
    {revision && <div className="anticipo-dialogo-fondo" role="dialog" aria-modal="true" aria-label={revision.accion === 'autorizar' ? 'Confirmar autorización de anticipo' : 'Confirmar revocación de anticipo'} onKeyDown={manejarTeclado}>
      <div className="nomina-confirmacion anticipo-dialogo" ref={dialogoRef}>
        <h3 ref={tituloRef} tabIndex={-1}>{revision.accion === 'autorizar' ? 'Autorizar Anticipo de Nómina' : 'Revocar Anticipo de Nómina'}</h3>
        <dl className="anticipo-persona"><div><dt>Persona</dt><dd>{revision.persona.empleado}</dd></div><div><dt>Cuenta XPAY</dt><dd>{revision.persona.usuario}</dd></div><div><dt>Empresa</dt><dd>{revision.persona.empresa}</dd></div><div><dt>Documento</dt><dd>{revision.persona.documentoMasked}</dd></div></dl>
        <p>{revision.accion === 'autorizar' ? 'Esta autorización permite usar el producto. Cada solicitud conserva los límites y controles del convenio.' : 'Esta acción bloquea el acceso al producto. Los anticipos y las obligaciones existentes se conservan.'}</p>
        <label className="nomina-confirmar"><input type="checkbox" checked={confirma} disabled={guardando} onChange={e => setConfirma(e.target.checked)} /><span>{revision.accion === 'autorizar' ? 'Confirmo que XPAY autoriza a esta persona a usar Anticipo de Nómina bajo este convenio.' : 'Confirmo que XPAY revoca el acceso de esta persona a Anticipo de Nómina bajo este convenio.'}</span></label>
        <label>Referencia de la decisión<input value={referencia} minLength={5} maxLength={200} disabled={guardando} onChange={e => setReferencia(e.target.value)} aria-describedby="anticipo-referencia-ayuda" /></label>
        <p id="anticipo-referencia-ayuda">Indica el soporte de la decisión, sin incluir datos personales.</p>
        <div className="nomina-confirmacion-acciones"><button type="button" disabled={!confirma || !referenciaValida(referencia) || guardando} onClick={() => void confirmar()}>{guardando ? 'Guardando...' : revision.accion === 'autorizar' ? 'Confirmar autorización' : 'Confirmar revocación'}</button><button type="button" className="secondary" disabled={guardando} onClick={cerrar}>Cancelar</button></div>
      </div>
    </div>}
  </div>;
}
