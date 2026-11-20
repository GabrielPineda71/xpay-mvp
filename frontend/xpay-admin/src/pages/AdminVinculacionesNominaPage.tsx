import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { get, HttpUncertainError, post } from '../api/client.ts';
import { useAuth } from '../auth/AuthContext.tsx';
import { fmtMoney } from '../utils.ts';
import './AdminVinculacionesNominaPage.css';

interface ApiOk<T> { success: boolean; data: T; }
interface Convenio { idConvenio: number; nombreEmpresa: string; }
export interface CoincidenciaNomina {
  idEmpleado: number;
  idUsuario: number;
  idConvenio: number;
  empresa: string;
  empleado: string;
  usuario: string;
  documentoMasked: string;
  cupoPreliminar: number;
  cuposPorCorte?: { numeroCorte: number; diaPago: number; valorPagoProgramado: number; porcentajeCupo: number; cupoBase: number }[];
  emailVerificado: boolean;
  whatsAppVerificado: boolean;
  identidadVerificada: boolean;
  elegible: boolean;
  razones: string[];
  huella: string;
  estado: 'PENDIENTE' | 'APROBADO' | 'REVISION_REQUERIDA';
}
interface Bandeja { items: CoincidenciaNomina[]; pagina: number; total: number; totalPaginas: number; }
interface Resultado { idEmpleado: number; idUsuario: number; aprobado: boolean; mensaje: string; }
interface Aprobacion { resultados: Resultado[]; aprobados: number; rechazados: number; }
const clave = (c: Pick<CoincidenciaNomina, 'idEmpleado' | 'idUsuario'>) => `${c.idEmpleado}:${c.idUsuario}`;
const puedeSeleccionar = (c: CoincidenciaNomina) => c.elegible && c.estado !== 'APROBADO' && !!c.huella;

// La vista solo selecciona filas visibles y la confirmación conserva exactamente
// sus huellas. El servidor revalida identidad, empleo, verificaciones y capacidad.
export function AdminVinculacionesNominaPage() {
  const { user } = useAuth();
  const autorizado = user?.roles.some(r => r === 'ADMIN_XPAY' || r === 'SUPERUSUARIO') === true;
  const [params, setParams] = useSearchParams();
  const convenioInicial = params.get('idConvenio');
  const idConvenio = convenioInicial && /^\d+$/.test(convenioInicial) && Number(convenioInicial) > 0 ? Number(convenioInicial) : null;
  const [convenios, setConvenios] = useState<Convenio[]>([]);
  const [pagina, setPagina] = useState(1);
  const [bandeja, setBandeja] = useState<Bandeja | null>(null);
  const [seleccion, setSeleccion] = useState<Set<string>>(new Set());
  const [revision, setRevision] = useState<CoincidenciaNomina[] | null>(null);
  const [confirma, setConfirma] = useState(false);
  const [referencia, setReferencia] = useState('');
  const [cargando, setCargando] = useState(true);
  const [aprobando, setAprobando] = useState(false);
  const [error, setError] = useState('');
  const [resultado, setResultado] = useState<Aprobacion | null>(null);
  const [nombresResultado, setNombresResultado] = useState<Record<string, string>>({});
  const secuencia = useRef(0);
  const confirmacionRef = useRef<HTMLHeadingElement>(null);

  const cargar = useCallback(async () => {
    if (!autorizado) { setCargando(false); return; }
    const intento = ++secuencia.current;
    setCargando(true); setError(''); setSeleccion(new Set());
    try {
      const consulta = new URLSearchParams({ pagina: String(pagina), tamanio: '50' });
      if (idConvenio) consulta.set('idConvenio', String(idConvenio));
      const r = await get<ApiOk<Bandeja>>(`/api/libranza/admin/vinculaciones?${consulta}`);
      if (intento !== secuencia.current) return;
      if (!r.success || !Array.isArray(r.data?.items)) throw new Error('Respuesta no reconocida.');
      setBandeja(r.data);
    } catch (err) {
      if (intento !== secuencia.current) return;
      setBandeja(null);
      setError(err instanceof Error ? err.message : 'No se pudieron consultar las coincidencias.');
    } finally { if (intento === secuencia.current) setCargando(false); }
  }, [autorizado, pagina, idConvenio]);

  useEffect(() => { void cargar(); return () => { secuencia.current++; }; }, [cargar]);
  useEffect(() => {
    if (!autorizado) return;
    let vigente = true;
    void get<ApiOk<Convenio[]>>('/api/libranza/admin/convenios').then(r => {
      if (vigente && r.success && Array.isArray(r.data)) setConvenios(r.data);
    }).catch(() => { /* La bandeja conserva su consulta aunque no cargue el filtro. */ });
    return () => { vigente = false; };
  }, [autorizado]);

  useEffect(() => { if (revision) confirmacionRef.current?.focus(); }, [revision]);

  function abrirRevision(filas: CoincidenciaNomina[]) {
    if (!filas.length || filas.length > 50 || filas.some(c => !puedeSeleccionar(c)) || aprobando || cargando) return;
    setRevision(filas.map(c => ({ ...c }))); setConfirma(false); setReferencia(''); setResultado(null); setNombresResultado({}); setError('');
  }

  async function aprobar() {
    if (!revision || !confirma || referencia.trim().length < 5 || referencia.trim().length > 200 || aprobando) return;
    setAprobando(true); setError('');
    try {
      const r = await post<ApiOk<Aprobacion>>('/api/libranza/admin/vinculaciones/aprobar', {
        seleccion: revision.map(c => ({ idEmpleado: c.idEmpleado, idUsuario: c.idUsuario, huella: c.huella })),
        confirmaPertenenciaLaboral: true,
        referenciaRevision: referencia.trim(),
      }, undefined, 60_000);
      if (!r.success || !Array.isArray(r.data?.resultados)) throw new Error('No pudimos confirmar el resultado.');
      setNombresResultado(Object.fromEntries(revision.map(c => [clave(c), `${c.usuario} · ${c.empresa}`])));
      setResultado(r.data); setRevision(null); setConfirma(false); setReferencia('');
      await cargar();
    } catch (err) {
      const mensaje = err instanceof HttpUncertainError
        ? 'No sabemos todavía cuáles aprobaciones se completaron. Se actualizará la bandeja; revisa su estado antes de intentarlo otra vez.'
        : err instanceof Error ? err.message : 'No se pudo completar la revisión.';
      // Un resultado incierto obliga a consultar de nuevo, nunca repetir el POST
      // desde la misma confirmación con una selección que podría estar aplicada.
      setRevision(null); setConfirma(false);
      await cargar();
      setError(mensaje);
    } finally { setAprobando(false); }
  }

  if (!autorizado) return <div className="page"><h2>Coincidencias de nómina</h2><p role="alert">Esta revisión está disponible para administradores de XPAY.</p></div>;
  const filas = bandeja?.items ?? [];
  const elegidas = filas.filter(c => seleccion.has(clave(c)) && puedeSeleccionar(c));
  const elegiblesPagina = filas.filter(puedeSeleccionar).slice(0, 50);
  const etiquetas: Record<CoincidenciaNomina['estado'], string> = { PENDIENTE: 'Por aprobar', APROBADO: 'Aprobada', REVISION_REQUERIDA: 'Revisar de nuevo' };

  return <div className="page nomina-vinculaciones">
    <Link to="/admin/libranza-convenios">← Convenios de nómina</Link>
    <h2>Coincidencias de nómina</h2>
    <p>Revisa las personas cuyo documento verificado coincide con la base de empleados de una empresa. La carga de la base no habilita cupos automáticamente.</p>
    <p>Confirma la pertenencia laboral y la vigencia de la información antes de aprobar. Los casos ambiguos o con requisitos pendientes quedan bloqueados.</p>
    <div className="nomina-filtros">
      <label>Empresa<select aria-label="Empresa" value={idConvenio ?? ''} disabled={aprobando || !!revision} onChange={e => { setPagina(1); setResultado(null); setRevision(null); setParams(e.target.value ? { idConvenio: e.target.value } : {}); }}>
        <option value="">Todas las empresas</option>
        {idConvenio && !convenios.some(c => c.idConvenio === idConvenio) && <option value={idConvenio}>Empresa seleccionada</option>}
        {convenios.map(c => <option key={c.idConvenio} value={c.idConvenio}>{c.nombreEmpresa}</option>)}
      </select></label>
      <button type="button" className="btn-search secondary" disabled={cargando || aprobando || !!revision} onClick={() => void cargar()}>Actualizar coincidencias</button>
    </div>
    {error && <p className="msg-error" role="alert">{error}</p>}
    {resultado && <section className="nomina-resultados" aria-label="Resultado de la revisión" role="status">
      <h3>Resultado de la revisión</h3>
      <p>{resultado.aprobados} aprobada(s). {resultado.rechazados} sin aprobar.</p>
      <ul>{resultado.resultados.map(r => <li key={clave(r)}><strong>{nombresResultado[clave(r)] ?? 'Persona revisada'}</strong> · {r.aprobado ? 'Aprobada' : 'Sin aprobar'}: <span>{r.mensaje}</span></li>)}</ul>
    </section>}
    {cargando && <p role="status">Consultando coincidencias...</p>}
    {!cargando && bandeja && <>
      <div className="nomina-toolbar"><p>{bandeja.total} coincidencia(s). Seleccionadas: {elegidas.length} de un máximo de 50.</p>
        <button type="button" className="btn-search secondary" disabled={!elegiblesPagina.length || aprobando || !!revision} onClick={() => setSeleccion(new Set(elegiblesPagina.map(clave)))}>Seleccionar elegibles de esta página ({elegiblesPagina.length})</button>
        <button type="button" className="btn-search secondary" disabled={!seleccion.size || aprobando || !!revision} onClick={() => setSeleccion(new Set())}>Limpiar selección</button>
        <button type="button" className="btn-search" disabled={!elegidas.length || aprobando || !!revision} onClick={() => abrirRevision(elegidas)}>Revisar seleccionadas ({elegidas.length})</button>
      </div>
      {!filas.length && <p>No hay coincidencias para esta consulta. Las personas deben completar su identidad y la empresa debe cargar su base de empleados.</p>}
      <div className="nomina-candidatos">
        {filas.map(c => <article className="nomina-candidato" key={clave(c)}>
          <div className="nomina-candidato-cabecera">
            <label className="nomina-seleccionar"><input type="checkbox" aria-label={`Seleccionar ${c.usuario} en ${c.empresa}`} checked={seleccion.has(clave(c))} disabled={!puedeSeleccionar(c) || aprobando || !!revision || (seleccion.size >= 50 && !seleccion.has(clave(c)))} onChange={e => { const checked = e.target.checked; setSeleccion(prev => { const siguiente = new Set(prev); if (checked && siguiente.size < 50) siguiente.add(clave(c)); else if (!checked) siguiente.delete(clave(c)); return siguiente; }); }} /><span>{c.empleado}</span></label>
            <span className={`badge ${c.estado === 'APROBADO' ? 'badge-ok' : c.elegible ? 'badge-info' : 'badge-warn'}`}>{etiquetas[c.estado]}</span>
          </div>
          <dl><div><dt>Empresa</dt><dd>{c.empresa}</dd></div><div><dt>Cuenta XPAY</dt><dd>{c.usuario}</dd></div><div><dt>Documento</dt><dd>{c.documentoMasked}</dd></div><div><dt>Cupo preliminar</dt><dd>{fmtMoney(c.cupoPreliminar)}</dd></div></dl>
          {!!c.cuposPorCorte?.length && <div className="nomina-cupos-corte"><strong>Cupos por corte</strong><ul aria-label={`Cupos por corte de ${c.usuario}`}>{c.cuposPorCorte.map(corte => <li key={corte.numeroCorte}>Día {corte.diaPago}: pago de nómina {fmtMoney(corte.valorPagoProgramado)} · cupo {fmtMoney(corte.cupoBase)} ({corte.porcentajeCupo} %).</li>)}</ul></div>}
          <ul className="nomina-verificaciones" aria-label={`Verificaciones de ${c.usuario}`}><li>Correo: {c.emailVerificado ? 'verificado' : 'pendiente'}</li><li>WhatsApp: {c.whatsAppVerificado ? 'verificado' : 'pendiente'}</li><li>Identidad: {c.identidadVerificada ? 'verificada' : 'pendiente'}</li></ul>
          {c.razones.length > 0 && <ul className="nomina-bloqueos">{c.razones.map((razon, i) => <li key={`${i}-${razon}`}>{razon}</li>)}</ul>}
          {puedeSeleccionar(c) && <button type="button" className="btn-search secondary" disabled={aprobando || !!revision} onClick={() => abrirRevision([c])}>Revisar a {c.usuario}</button>}
        </article>)}
      </div>
      {bandeja.totalPaginas > 1 && <nav className="nomina-paginacion" aria-label="Páginas de coincidencias">
        <button type="button" disabled={pagina <= 1 || aprobando || !!revision} onClick={() => setPagina(p => p - 1)}>Anterior</button>
        <span>Página {bandeja.pagina} de {bandeja.totalPaginas}</span>
        <button type="button" disabled={pagina >= bandeja.totalPaginas || aprobando || !!revision} onClick={() => setPagina(p => p + 1)}>Siguiente</button>
      </nav>}
    </>}
    {revision && <section className="nomina-confirmacion" aria-label="Confirmar aprobación de nómina">
      <h3 ref={confirmacionRef} tabIndex={-1}>Revisa antes de aprobar</h3>
      <p>Se revisarán únicamente estas {revision.length} persona(s). XPAY comprobará nuevamente sus requisitos y que la información siga vigente.</p>
      <ul>{revision.map(c => <li key={clave(c)}><strong>{c.usuario}</strong> · {c.empresa} · {c.documentoMasked} · cupo preliminar {fmtMoney(c.cupoPreliminar)}{c.cuposPorCorte?.map(corte => <span className="nomina-cupo-confirmado" key={corte.numeroCorte}>Día {corte.diaPago}: cupo {fmtMoney(corte.cupoBase)} sobre pago de nómina {fmtMoney(corte.valorPagoProgramado)}.</span>)}</li>)}</ul>
      <label className="nomina-confirmar"><input type="checkbox" checked={confirma} disabled={aprobando} onChange={e => setConfirma(e.target.checked)} /><span>Confirmo que revisé la base vigente y que las personas seleccionadas pertenecen a las empresas indicadas.</span></label>
      <label>Referencia de la revisión<input value={referencia} minLength={5} maxLength={200} disabled={aprobando} onChange={e => setReferencia(e.target.value)} aria-describedby="referencia-revision-ayuda" /></label>
      <p id="referencia-revision-ayuda">Indica una referencia interna de al menos 5 caracteres del soporte revisado. No incluyas documentos, teléfonos ni datos bancarios.</p>
      <div className="nomina-confirmacion-acciones"><button type="button" className="btn-search" disabled={!confirma || referencia.trim().length < 5 || aprobando} onClick={() => void aprobar()}>{aprobando ? 'Aprobando...' : `Aprobar ${revision.length} persona(s)`}</button><button type="button" className="btn-search secondary" disabled={aprobando} onClick={() => setRevision(null)}>Cancelar revisión</button></div>
    </section>}
  </div>;
}
