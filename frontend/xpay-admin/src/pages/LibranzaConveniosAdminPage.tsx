import { useEffect, useState } from 'react';
import { get, post, put } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

// ── Types ──────────────────────────────────────────────────────────────────

interface Convenio {
  idConvenio:           number;
  nombreEmpresa:        string;
  nit:                  string;
  representanteLegal:   string | null;
  emailContacto:        string | null;
  telefonoContacto:     string | null;
  direccion:            string | null;
  estado:               string;
  diaPago1:             number | null;
  diaPago2:             number | null;
  periodicidadPago:     string;
  porcentajeMaximoCupo: number;
  observaciones:        string | null;
  fechaInicio:          string;
  fechaFin:             string | null;
  createdAt:            string;
  updatedAt:            string | null;
}

interface Parametros {
  idParametro:               number;
  idConvenio:                number;
  porcentajeMaximoCupo:      number;
  salarioMinimoEmpleado:     number | null;
  salarioMaximoEmpleado:     number | null;
  requiereValidacionEmpresa: boolean;
  permiteAnticipoMultiple:   boolean;
  maxAnticipacionesActivos:  number;
  ivaPorcentaje:             number;
  momentoCobroComision:      string;
  estado:                    string;
  createdAt:                 string;
  updatedAt:                 string | null;
}

interface Rango {
  idRango:    number;
  idConvenio: number;
  valorDesde: number;
  valorHasta: number;
  tipoCobro:  string;
  valorCobro: number;
  aplicaIva:  boolean;
  estado:     string;
  createdAt:  string;
  updatedAt:  string | null;
}

interface Empleado {
  idEmpleado: number;
  tipoDocumento: string;
  numeroDocumento: string;
  nombres: string;
  apellidos?: string;
  cargo?: string;
  salarioMensual: number;
  periodicidadPago: string;
  estado: string;
  cupoPreliminar: number;
  origenCarga: string;
  loteImportacion?: string;
  createdAt: string;
  updatedAt?: string;
}

interface Importacion {
  idImportacion: number;
  nombreArchivo?: string;
  loteImportacion: string;
  totalFilas: number;
  filasValidas: number;
  filasError: number;
  empleadosCreados: number;
  empleadosActualizados: number;
  estado: string;
  errores: { fila: number; campo: string; mensaje: string }[];
  createdAt: string;
}

interface UsuarioEmpresa {
  idUsuarioEmpresa: number;
  idUsuario: number;
  idConvenio: number;
  rolEmpresa: string;
  estado: string;
  createdAt: string;
}

type Vista = 'lista' | 'crear' | 'editar' | 'detalle';
type DetalleTab = 'parametros' | 'rangos' | 'empleados' | 'importaciones' | 'usuarios';

const ESTADOS_CONVENIO = ['ACTIVO', 'SUSPENDIDO', 'CANCELADO'];
const PERIODICIDADES   = ['MENSUAL', 'QUINCENAL'];
const MOMENTOS_COBRO   = ['ANTICIPADO', 'VENCIDO'];
const TIPOS_COBRO      = ['FIJO', 'PORCENTAJE'];
const ESTADOS_PARAM    = ['ACTIVO', 'INACTIVO'];

function estadoBadge(estado: string) {
  const cls: Record<string, string> = {
    ACTIVO: 'badge-ok', SUSPENDIDO: 'badge-warn', CANCELADO: 'badge-error',
    INACTIVO: 'badge-warn',
  };
  return <span className={`badge ${cls[estado] ?? 'badge'}`}>{estado}</span>;
}

// ── Formulario de convenio ─────────────────────────────────────────────────

interface FormConvenio {
  nombreEmpresa: string; nit: string; representanteLegal: string;
  emailContacto: string; telefonoContacto: string; direccion: string;
  estado: string; diaPago1: string; diaPago2: string; periodicidadPago: string;
  porcentajeMaximoCupo: string; observaciones: string; fechaInicio: string; fechaFin: string;
}

const EMPTY_FORM_CONV: FormConvenio = {
  nombreEmpresa: '', nit: '', representanteLegal: '', emailContacto: '',
  telefonoContacto: '', direccion: '', estado: 'ACTIVO', diaPago1: '',
  diaPago2: '', periodicidadPago: 'MENSUAL', porcentajeMaximoCupo: '30',
  observaciones: '', fechaInicio: new Date().toISOString().slice(0,10), fechaFin: '',
};

function convToForm(c: Convenio): FormConvenio {
  return {
    nombreEmpresa: c.nombreEmpresa, nit: c.nit,
    representanteLegal: c.representanteLegal ?? '', emailContacto: c.emailContacto ?? '',
    telefonoContacto: c.telefonoContacto ?? '', direccion: c.direccion ?? '',
    estado: c.estado, diaPago1: c.diaPago1?.toString() ?? '',
    diaPago2: c.diaPago2?.toString() ?? '', periodicidadPago: c.periodicidadPago,
    porcentajeMaximoCupo: c.porcentajeMaximoCupo.toString(),
    observaciones: c.observaciones ?? '',
    fechaInicio: c.fechaInicio.slice(0,10), fechaFin: c.fechaFin?.slice(0,10) ?? '',
  };
}

// ── Componente principal ───────────────────────────────────────────────────

export function LibranzaConveniosAdminPage() {
  const [vista,      setVista]      = useState<Vista>('lista');
  const [convenios,  setConvenios]  = useState<Convenio[]>([]);
  const [selected,   setSelected]   = useState<Convenio | null>(null);
  const [parametros, setParametros] = useState<Parametros[]>([]);
  const [rangos,     setRangos]     = useState<Rango[]>([]);
  const [loading,    setLoading]    = useState(false);
  const [error,      setError]      = useState('');
  const [msg,        setMsg]        = useState('');

  // Formulario convenio
  const [formConv, setFormConv] = useState<FormConvenio>(EMPTY_FORM_CONV);
  const [savingConv, setSavingConv] = useState(false);

  // Formulario parámetros (inline edit)
  const [editParam, setEditParam] = useState<Parametros | null>(null);
  const [formParam, setFormParam] = useState<Partial<Parametros>>({});
  const [savingParam, setSavingParam] = useState(false);
  const [showNewParam, setShowNewParam] = useState(false);
  const [newParam, setNewParam] = useState({
    porcentajeMaximoCupo: '30', salarioMinimoEmpleado: '1000000',
    salarioMaximoEmpleado: '', requiereValidacionEmpresa: true,
    permiteAnticipoMultiple: false, maxAnticipacionesActivos: 1,
    ivaPorcentaje: '19', momentoCobroComision: 'VENCIDO', estado: 'ACTIVO',
  });

  // Empleados / importaciones / usuarios empresa
  const [detalleTab, setDetalleTab]         = useState<DetalleTab>('parametros');
  const [empleados, setEmpleados]           = useState<Empleado[]>([]);
  const [importaciones, setImportaciones]   = useState<Importacion[]>([]);
  const [usuariosEmp, setUsuariosEmp]       = useState<UsuarioEmpresa[]>([]);
  const [loadingEmp, setLoadingEmp]         = useState(false);
  const [newUsuario, setNewUsuario]         = useState({ idUsuario: '', rolEmpresa: 'ADMIN_EMPRESA' });
  const [savingUsuario, setSavingUsuario]   = useState(false);
  const [expandImport, setExpandImport]     = useState<number | null>(null);

  // Formulario rango
  const [showNewRango, setShowNewRango] = useState(false);
  const [newRango, setNewRango] = useState({
    valorDesde: '', valorHasta: '', tipoCobro: 'FIJO',
    valorCobro: '', aplicaIva: true, estado: 'ACTIVO',
  });
  const [editRango, setEditRango] = useState<Rango | null>(null);
  const [formRango, setFormRango] = useState<Partial<Rango>>({});
  const [savingRango, setSavingRango] = useState(false);

  function limpiarMsg() { setMsg(''); setError(''); }

  function cargarConvenios() {
    setLoading(true); limpiarMsg();
    get<{ success: boolean; data: Convenio[] }>('/api/libranza/admin/convenios')
      .then(r => setConvenios(r.data))
      .catch(e => setError((e as Error).message))
      .finally(() => setLoading(false));
  }

  function cargarDetalle(conv: Convenio) {
    setSelected(conv);
    setVista('detalle');
    setDetalleTab('parametros');
    limpiarMsg();
    Promise.all([
      get<{ success: boolean; data: Parametros[] }>(`/api/libranza/admin/convenios/${conv.idConvenio}/parametros`),
      get<{ success: boolean; data: Rango[] }>(`/api/libranza/admin/convenios/${conv.idConvenio}/rangos`),
    ]).then(([p, r]) => { setParametros(p.data); setRangos(r.data); })
      .catch(e => setError((e as Error).message));
  }

  async function cargarEmpleados(id: number) {
    setLoadingEmp(true);
    try {
      const [e, i, u] = await Promise.all([
        get<{ success: boolean; data: Empleado[] }>(`/api/libranza/admin/convenios/${id}/empleados`),
        get<{ success: boolean; data: Importacion[] }>(`/api/libranza/admin/convenios/${id}/importaciones`),
        get<{ success: boolean; data: UsuarioEmpresa[] }>(`/api/libranza/admin/convenios/${id}/usuarios-empresa`),
      ]);
      setEmpleados(e.data);
      setImportaciones(i.data);
      setUsuariosEmp(u.data);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoadingEmp(false);
    }
  }

  async function asociarUsuario() {
    if (!selected) return;
    if (!newUsuario.idUsuario || isNaN(Number(newUsuario.idUsuario))) {
      setError('Ingresa un ID de usuario válido.'); return;
    }
    setSavingUsuario(true); limpiarMsg();
    try {
      await post(`/api/libranza/admin/convenios/${selected.idConvenio}/usuarios-empresa`, {
        idUsuario: Number(newUsuario.idUsuario),
        rolEmpresa: newUsuario.rolEmpresa,
      });
      setMsg('Usuario asociado exitosamente.');
      setNewUsuario({ idUsuario: '', rolEmpresa: 'ADMIN_EMPRESA' });
      await cargarEmpleados(selected.idConvenio);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSavingUsuario(false);
    }
  }

  useEffect(cargarConvenios, []);

  // ── Guardar convenio ────────────────────────────────────────────────────

  async function guardarConvenio() {
    limpiarMsg(); setSavingConv(true);
    const body = {
      nombreEmpresa:        formConv.nombreEmpresa,
      nit:                  formConv.nit,
      representanteLegal:   formConv.representanteLegal || null,
      emailContacto:        formConv.emailContacto || null,
      telefonoContacto:     formConv.telefonoContacto || null,
      direccion:            formConv.direccion || null,
      estado:               formConv.estado,
      diaPago1:             formConv.diaPago1 ? Number(formConv.diaPago1) : null,
      diaPago2:             formConv.diaPago2 ? Number(formConv.diaPago2) : null,
      periodicidadPago:     formConv.periodicidadPago,
      porcentajeMaximoCupo: Number(formConv.porcentajeMaximoCupo),
      observaciones:        formConv.observaciones || null,
      fechaInicio:          formConv.fechaInicio || new Date().toISOString(),
      fechaFin:             formConv.fechaFin || null,
    };
    try {
      if (vista === 'crear') {
        await post<{ success: boolean; data: Convenio }>('/api/libranza/admin/convenios', body);
        setMsg('Convenio creado exitosamente.');
      } else if (vista === 'editar' && selected) {
        await put<{ success: boolean; data: Convenio }>(`/api/libranza/admin/convenios/${selected.idConvenio}`, body);
        setMsg('Convenio actualizado.');
      }
      cargarConvenios();
      setVista('lista');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSavingConv(false);
    }
  }

  // ── Guardar parámetros ──────────────────────────────────────────────────

  async function crearParametros() {
    if (!selected) return;
    limpiarMsg(); setSavingParam(true);
    try {
      await post(`/api/libranza/admin/convenios/${selected.idConvenio}/parametros`, {
        porcentajeMaximoCupo:      Number(newParam.porcentajeMaximoCupo),
        salarioMinimoEmpleado:     newParam.salarioMinimoEmpleado ? Number(newParam.salarioMinimoEmpleado) : null,
        salarioMaximoEmpleado:     newParam.salarioMaximoEmpleado ? Number(newParam.salarioMaximoEmpleado) : null,
        requiereValidacionEmpresa: newParam.requiereValidacionEmpresa,
        permiteAnticipoMultiple:   newParam.permiteAnticipoMultiple,
        maxAnticipacionesActivos:  Number(newParam.maxAnticipacionesActivos),
        ivaPorcentaje:             Number(newParam.ivaPorcentaje),
        momentoCobroComision:      newParam.momentoCobroComision,
        estado:                    newParam.estado,
      });
      setMsg('Parámetros creados.');
      setShowNewParam(false);
      const r = await get<{ success: boolean; data: Parametros[] }>(`/api/libranza/admin/convenios/${selected.idConvenio}/parametros`);
      setParametros(r.data);
    } catch (e) { setError((e as Error).message); }
    finally { setSavingParam(false); }
  }

  async function actualizarParametros(idParametro: number) {
    limpiarMsg(); setSavingParam(true);
    try {
      await put(`/api/libranza/admin/parametros/${idParametro}`, {
        porcentajeMaximoCupo:      formParam.porcentajeMaximoCupo,
        salarioMinimoEmpleado:     formParam.salarioMinimoEmpleado,
        salarioMaximoEmpleado:     formParam.salarioMaximoEmpleado,
        requiereValidacionEmpresa: formParam.requiereValidacionEmpresa,
        permiteAnticipoMultiple:   formParam.permiteAnticipoMultiple,
        maxAnticipacionesActivos:  formParam.maxAnticipacionesActivos,
        ivaPorcentaje:             formParam.ivaPorcentaje,
        momentoCobroComision:      formParam.momentoCobroComision,
        estado:                    formParam.estado,
      });
      setMsg('Parámetros actualizados.');
      setEditParam(null);
      if (selected) {
        const r = await get<{ success: boolean; data: Parametros[] }>(`/api/libranza/admin/convenios/${selected.idConvenio}/parametros`);
        setParametros(r.data);
      }
    } catch (e) { setError((e as Error).message); }
    finally { setSavingParam(false); }
  }

  // ── Guardar rango ───────────────────────────────────────────────────────

  async function crearRango() {
    if (!selected) return;
    limpiarMsg(); setSavingRango(true);
    try {
      await post(`/api/libranza/admin/convenios/${selected.idConvenio}/rangos`, {
        valorDesde: Number(newRango.valorDesde),
        valorHasta: Number(newRango.valorHasta),
        tipoCobro:  newRango.tipoCobro,
        valorCobro: Number(newRango.valorCobro),
        aplicaIva:  newRango.aplicaIva,
        estado:     newRango.estado,
      });
      setMsg('Rango creado.');
      setShowNewRango(false);
      const r = await get<{ success: boolean; data: Rango[] }>(`/api/libranza/admin/convenios/${selected.idConvenio}/rangos`);
      setRangos(r.data);
    } catch (e) { setError((e as Error).message); }
    finally { setSavingRango(false); }
  }

  async function actualizarRango(idRango: number) {
    limpiarMsg(); setSavingRango(true);
    try {
      await put(`/api/libranza/admin/rangos/${idRango}`, {
        valorDesde: formRango.valorDesde,
        valorHasta: formRango.valorHasta,
        tipoCobro:  formRango.tipoCobro,
        valorCobro: formRango.valorCobro,
        aplicaIva:  formRango.aplicaIva,
        estado:     formRango.estado,
      });
      setMsg('Rango actualizado.');
      setEditRango(null);
      if (selected) {
        const r = await get<{ success: boolean; data: Rango[] }>(`/api/libranza/admin/convenios/${selected.idConvenio}/rangos`);
        setRangos(r.data);
      }
    } catch (e) { setError((e as Error).message); }
    finally { setSavingRango(false); }
  }

  // ── Renders ─────────────────────────────────────────────────────────────

  const Notif = () => (
    <>
      {msg   && <div style={{ background:'#f0fff4',color:'#276749',padding:'0.75rem 1rem',borderRadius:'6px',borderLeft:'3px solid #48bb78',marginBottom:'1rem' }}>{msg}</div>}
      {error && <div className="error-msg" style={{ marginBottom:'1rem' }}>Error: {error}</div>}
    </>
  );

  // ── Input helpers ────────────────────────────────────────────────────────

  const inp = (label: string, val: string, onChange: (v:string)=>void, type='text', required=false) => (
    <label style={{ display:'block', marginBottom:'0.75rem' }}>
      <span style={{ fontSize:'0.85rem', fontWeight:600, display:'block', marginBottom:'0.2rem' }}>{label}{required && ' *'}</span>
      <input type={type} value={val} onChange={e => onChange(e.target.value)}
        style={{ width:'100%', padding:'0.45rem 0.6rem', border:'1px solid #cbd5e0', borderRadius:'4px', fontSize:'0.9rem', boxSizing:'border-box' }} />
    </label>
  );

  const sel = (label: string, val: string, onChange: (v:string)=>void, opts: string[]) => (
    <label style={{ display:'block', marginBottom:'0.75rem' }}>
      <span style={{ fontSize:'0.85rem', fontWeight:600, display:'block', marginBottom:'0.2rem' }}>{label}</span>
      <select value={val} onChange={e => onChange(e.target.value)}
        style={{ width:'100%', padding:'0.45rem 0.6rem', border:'1px solid #cbd5e0', borderRadius:'4px', fontSize:'0.9rem', boxSizing:'border-box' }}>
        {opts.map(o => <option key={o} value={o}>{o}</option>)}
      </select>
    </label>
  );

  const chk = (label: string, val: boolean, onChange: (v:boolean)=>void) => (
    <label style={{ display:'flex', alignItems:'center', gap:'0.5rem', marginBottom:'0.75rem', cursor:'pointer' }}>
      <input type="checkbox" checked={val} onChange={e => onChange(e.target.checked)} />
      <span style={{ fontSize:'0.85rem', fontWeight:600 }}>{label}</span>
    </label>
  );

  const btn = (label: string, onClick: ()=>void, variant: 'primary'|'secondary'|'danger' = 'primary', disabled=false) => {
    const styles: Record<string, React.CSSProperties> = {
      primary:   { background:'#3182ce', color:'#fff', border:'none', padding:'0.45rem 1rem', borderRadius:'4px', cursor:'pointer', fontSize:'0.9rem' },
      secondary: { background:'#edf2f7', color:'#2d3748', border:'1px solid #cbd5e0', padding:'0.45rem 1rem', borderRadius:'4px', cursor:'pointer', fontSize:'0.9rem' },
      danger:    { background:'#e53e3e', color:'#fff', border:'none', padding:'0.45rem 1rem', borderRadius:'4px', cursor:'pointer', fontSize:'0.9rem' },
    };
    return <button onClick={onClick} disabled={disabled} style={{ ...styles[variant], opacity: disabled ? 0.6 : 1 }}>{label}</button>;
  };

  const formStyle: React.CSSProperties = {
    background:'#fff', border:'1px solid #e2e8f0', borderRadius:'8px',
    padding:'1.5rem', maxWidth:'600px', marginBottom:'1.5rem',
  };

  // ── Vista: lista ─────────────────────────────────────────────────────────

  if (vista === 'lista') return (
    <div className="page">
      <h2>Convenios Libranza</h2>
      <p style={{ color:'#718096', fontSize:'0.9rem', marginBottom:'1.5rem' }}>
        QA · sin dinero real · sin empleados todavía · solo parametrización
      </p>
      <Notif />
      <div style={{ display:'flex', gap:'0.75rem', marginBottom:'1.25rem', flexWrap:'wrap' }}>
        {btn('+ Nuevo convenio', () => { setFormConv(EMPTY_FORM_CONV); limpiarMsg(); setVista('crear'); })}
        {btn('↺ Refrescar', () => { limpiarMsg(); cargarConvenios(); }, 'secondary')}
      </div>
      {loading && <div className="loading">Cargando...</div>}
      {!loading && convenios.length === 0 && !error && <div className="empty">No hay convenios.</div>}
      {!loading && convenios.length > 0 && (
        <div className="table-wrapper">
          <table>
            <thead><tr>
              <th>ID</th><th>Empresa</th><th>NIT</th><th>Estado</th>
              <th>Periodicidad</th><th>Día pago</th><th>Cupo %</th>
              <th>Fecha inicio</th><th>Acciones</th>
            </tr></thead>
            <tbody>
              {convenios.map(c => (
                <tr key={c.idConvenio}>
                  <td className="mono">{c.idConvenio}</td>
                  <td style={{ fontWeight:600 }}>{c.nombreEmpresa}</td>
                  <td className="mono">{c.nit}</td>
                  <td>{estadoBadge(c.estado)}</td>
                  <td>{c.periodicidadPago}</td>
                  <td className="mono">{c.diaPago1}{c.diaPago2 ? ` / ${c.diaPago2}` : ''}</td>
                  <td className="mono">{c.porcentajeMaximoCupo}%</td>
                  <td className="mono">{fmtDate(c.fechaInicio)}</td>
                  <td>
                    <div style={{ display:'flex', gap:'0.4rem', flexWrap:'wrap' }}>
                      <button className="btn-link" style={{ fontSize:'0.8rem' }}
                        onClick={() => cargarDetalle(c)}>Ver detalle</button>
                      <button className="btn-link" style={{ fontSize:'0.8rem', color:'#3182ce' }}
                        onClick={() => { setSelected(c); setFormConv(convToForm(c)); limpiarMsg(); setVista('editar'); }}>Editar</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );

  // ── Vista: crear / editar convenio ───────────────────────────────────────

  if (vista === 'crear' || vista === 'editar') {
    const titulo = vista === 'crear' ? 'Nuevo convenio' : `Editar: ${selected?.nombreEmpresa}`;
    return (
      <div className="page">
        <h2>{titulo}</h2>
        <Notif />
        <div style={formStyle}>
          {inp('Nombre empresa *', formConv.nombreEmpresa, v => setFormConv(p=>({...p,nombreEmpresa:v})), 'text', true)}
          {vista === 'crear' && inp('NIT *', formConv.nit, v => setFormConv(p=>({...p,nit:v})), 'text', true)}
          {vista === 'editar' && <div style={{ marginBottom:'0.75rem', fontSize:'0.85rem' }}><b>NIT:</b> {selected?.nit} <span style={{color:'#a0aec0'}}>(no editable)</span></div>}
          {inp('Representante legal', formConv.representanteLegal, v => setFormConv(p=>({...p,representanteLegal:v})))}
          {inp('Email contacto', formConv.emailContacto, v => setFormConv(p=>({...p,emailContacto:v})), 'email')}
          {inp('Teléfono', formConv.telefonoContacto, v => setFormConv(p=>({...p,telefonoContacto:v})))}
          {inp('Dirección', formConv.direccion, v => setFormConv(p=>({...p,direccion:v})))}
          {sel('Estado', formConv.estado, v => setFormConv(p=>({...p,estado:v})), ESTADOS_CONVENIO)}
          {sel('Periodicidad de pago', formConv.periodicidadPago, v => setFormConv(p=>({...p,periodicidadPago:v})), PERIODICIDADES)}
          {inp('Día de pago 1 (1-31)', formConv.diaPago1, v => setFormConv(p=>({...p,diaPago1:v})), 'number')}
          {inp('Día de pago 2 (opcional)', formConv.diaPago2, v => setFormConv(p=>({...p,diaPago2:v})), 'number')}
          {inp('% máximo cupo (1-100) *', formConv.porcentajeMaximoCupo, v => setFormConv(p=>({...p,porcentajeMaximoCupo:v})), 'number', true)}
          {inp('Fecha inicio *', formConv.fechaInicio, v => setFormConv(p=>({...p,fechaInicio:v})), 'date', true)}
          {inp('Fecha fin (opcional)', formConv.fechaFin, v => setFormConv(p=>({...p,fechaFin:v})), 'date')}
          {inp('Observaciones', formConv.observaciones, v => setFormConv(p=>({...p,observaciones:v})))}
          <div style={{ display:'flex', gap:'0.75rem', marginTop:'1rem' }}>
            {btn(savingConv ? 'Guardando...' : 'Guardar', guardarConvenio, 'primary', savingConv)}
            {btn('Cancelar', () => { limpiarMsg(); setVista('lista'); }, 'secondary')}
          </div>
        </div>
      </div>
    );
  }

  // ── Vista: detalle ───────────────────────────────────────────────────────

  if (vista === 'detalle' && selected) return (
    <div className="page">
      <div style={{ display:'flex', alignItems:'center', gap:'1rem', marginBottom:'1.5rem', flexWrap:'wrap' }}>
        <button onClick={() => { limpiarMsg(); setVista('lista'); setSelected(null); }}
          style={{ background:'none', border:'none', cursor:'pointer', color:'#3182ce', fontSize:'0.9rem', padding:0 }}>
          ← Volver
        </button>
        <h2 style={{ margin:0 }}>{selected.nombreEmpresa}</h2>
        {estadoBadge(selected.estado)}
      </div>
      <Notif />

      {/* Info rápida del convenio */}
      <div style={{ background:'#f7fafc', border:'1px solid #e2e8f0', borderRadius:'8px', padding:'1rem 1.25rem', marginBottom:'1.5rem', display:'grid', gridTemplateColumns:'repeat(auto-fill,minmax(200px,1fr))', gap:'0.5rem 1.5rem' }}>
        <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>NIT</span><div className="mono">{selected.nit}</div></div>
        <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Representante</span><div>{selected.representanteLegal ?? '—'}</div></div>
        <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Email</span><div>{selected.emailContacto ?? '—'}</div></div>
        <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Teléfono</span><div className="mono">{selected.telefonoContacto ?? '—'}</div></div>
        <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Periodicidad</span><div>{selected.periodicidadPago}</div></div>
        <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Días de pago</span><div className="mono">{selected.diaPago1 ?? '—'}{selected.diaPago2 ? ` / ${selected.diaPago2}` : ''}</div></div>
        <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>% máx cupo</span><div className="mono">{selected.porcentajeMaximoCupo}%</div></div>
        <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Inicio</span><div className="mono">{fmtDate(selected.fechaInicio)}</div></div>
        {selected.observaciones && <div style={{ gridColumn:'1/-1' }}><span style={{ fontSize:'0.78rem', color:'#718096' }}>Observaciones</span><div>{selected.observaciones}</div></div>}
      </div>

      {/* ── Tabs ── */}
      <div style={{ display:'flex', gap:'0.4rem', borderBottom:'2px solid #e2e8f0', marginBottom:'1.5rem', flexWrap:'wrap' }}>
        {([
          ['parametros', 'Parámetros'],
          ['rangos', 'Rangos de cobro'],
          ['empleados', `Empleados (${empleados.length})`],
          ['importaciones', `Importaciones (${importaciones.length})`],
          ['usuarios', `Usuarios empresa (${usuariosEmp.length})`],
        ] as [DetalleTab, string][]).map(([t, label]) => (
          <button key={t} onClick={() => {
            setDetalleTab(t);
            if ((t === 'empleados' || t === 'importaciones' || t === 'usuarios') && selected && empleados.length === 0 && importaciones.length === 0) {
              cargarEmpleados(selected.idConvenio);
            }
          }}
            style={{
              padding:'0.45rem 0.9rem', border:'none', background:'none',
              borderBottom: detalleTab === t ? '2px solid #3182ce' : '2px solid transparent',
              color: detalleTab === t ? '#3182ce' : '#718096',
              fontWeight: detalleTab === t ? 700 : 400,
              cursor:'pointer', fontSize:'0.85rem', marginBottom:'-2px'
            }}>{label}</button>
        ))}
      </div>

      {/* ── Parámetros ── */}
      {detalleTab === 'parametros' && <div style={{ marginBottom:'2rem' }}>
        <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:'0.75rem' }}>
          <h3 style={{ margin:0 }}>Parámetros del convenio</h3>
          {btn('+ Agregar parámetros', () => { setShowNewParam(true); setEditParam(null); limpiarMsg(); }, 'secondary')}
        </div>

        {showNewParam && (
          <div style={{ ...formStyle, background:'#f7fafc' }}>
            <h4 style={{ margin:'0 0 1rem' }}>Nuevos parámetros</h4>
            {inp('% máximo cupo', newParam.porcentajeMaximoCupo, v => setNewParam(p=>({...p,porcentajeMaximoCupo:v})), 'number')}
            {inp('Salario mínimo empleado', newParam.salarioMinimoEmpleado, v => setNewParam(p=>({...p,salarioMinimoEmpleado:v})), 'number')}
            {inp('Salario máximo empleado', newParam.salarioMaximoEmpleado, v => setNewParam(p=>({...p,salarioMaximoEmpleado:v})), 'number')}
            {inp('IVA %', newParam.ivaPorcentaje, v => setNewParam(p=>({...p,ivaPorcentaje:v})), 'number')}
            <label style={{ display:'block', marginBottom:'0.75rem' }}>
              <span style={{ fontSize:'0.85rem', fontWeight:600, display:'block', marginBottom:'0.2rem' }}>Cobro de comisión</span>
              <select value={newParam.momentoCobroComision} onChange={e => setNewParam(p=>({...p,momentoCobroComision:e.target.value}))}
                style={{ width:'100%', padding:'0.45rem 0.6rem', border:'1px solid #cbd5e0', borderRadius:'4px', fontSize:'0.9rem' }}>
                {MOMENTOS_COBRO.map(o => <option key={o} value={o}>{o === 'ANTICIPADO' ? 'Anticipado' : 'Vencido'}</option>)}
              </select>
              <span style={{ fontSize:'0.78rem', color:'#718096', display:'block', marginTop:'0.2rem' }}>
                Anticipado descuenta comisión e IVA al desembolso. Vencido cobra comisión e IVA en el pago de nómina.
              </span>
            </label>
            {inp('Max anticipos activos', String(newParam.maxAnticipacionesActivos), v => setNewParam(p=>({...p,maxAnticipacionesActivos:Number(v)})), 'number')}
            {chk('Requiere validación empresa', newParam.requiereValidacionEmpresa, v => setNewParam(p=>({...p,requiereValidacionEmpresa:v})))}
            {chk('Permite anticipo múltiple', newParam.permiteAnticipoMultiple, v => setNewParam(p=>({...p,permiteAnticipoMultiple:v})))}
            {sel('Estado', newParam.estado, v => setNewParam(p=>({...p,estado:v})), ESTADOS_PARAM)}
            <div style={{ display:'flex', gap:'0.75rem', marginTop:'1rem' }}>
              {btn(savingParam ? 'Guardando...' : 'Guardar', crearParametros, 'primary', savingParam)}
              {btn('Cancelar', () => setShowNewParam(false), 'secondary')}
            </div>
          </div>
        )}

        {parametros.length === 0 && !showNewParam && <div className="empty">Sin parámetros. Agrega uno.</div>}

        {parametros.map(p => (
          editParam?.idParametro === p.idParametro ? (
            <div key={p.idParametro} style={{ ...formStyle, background:'#f7fafc' }}>
              <h4 style={{ margin:'0 0 1rem' }}>Editar parámetros #{p.idParametro}</h4>
              {inp('% máximo cupo', String(formParam.porcentajeMaximoCupo ?? p.porcentajeMaximoCupo), v => setFormParam(x=>({...x,porcentajeMaximoCupo:Number(v)})), 'number')}
              {inp('Salario mínimo', String(formParam.salarioMinimoEmpleado ?? p.salarioMinimoEmpleado ?? ''), v => setFormParam(x=>({...x,salarioMinimoEmpleado:v?Number(v):null})), 'number')}
              {inp('Salario máximo', String(formParam.salarioMaximoEmpleado ?? p.salarioMaximoEmpleado ?? ''), v => setFormParam(x=>({...x,salarioMaximoEmpleado:v?Number(v):null})), 'number')}
              {inp('IVA %', String(formParam.ivaPorcentaje ?? p.ivaPorcentaje), v => setFormParam(x=>({...x,ivaPorcentaje:Number(v)})), 'number')}
              <label style={{ display:'block', marginBottom:'0.75rem' }}>
                <span style={{ fontSize:'0.85rem', fontWeight:600, display:'block', marginBottom:'0.2rem' }}>Cobro de comisión</span>
                <select value={formParam.momentoCobroComision ?? p.momentoCobroComision}
                  onChange={e => setFormParam(x=>({...x,momentoCobroComision:e.target.value}))}
                  style={{ width:'100%', padding:'0.45rem 0.6rem', border:'1px solid #cbd5e0', borderRadius:'4px', fontSize:'0.9rem' }}>
                  {MOMENTOS_COBRO.map(o => <option key={o} value={o}>{o === 'ANTICIPADO' ? 'Anticipado' : 'Vencido'}</option>)}
                </select>
                <span style={{ fontSize:'0.78rem', color:'#718096', display:'block', marginTop:'0.2rem' }}>
                  Anticipado descuenta comisión e IVA al desembolso. Vencido cobra comisión e IVA en el pago de nómina.
                </span>
              </label>
              {inp('Max anticipos activos', String(formParam.maxAnticipacionesActivos ?? p.maxAnticipacionesActivos), v => setFormParam(x=>({...x,maxAnticipacionesActivos:Number(v)})), 'number')}
              {chk('Requiere validación empresa', formParam.requiereValidacionEmpresa ?? p.requiereValidacionEmpresa, v => setFormParam(x=>({...x,requiereValidacionEmpresa:v})))}
              {chk('Permite anticipo múltiple', formParam.permiteAnticipoMultiple ?? p.permiteAnticipoMultiple, v => setFormParam(x=>({...x,permiteAnticipoMultiple:v})))}
              <label style={{ display:'block', marginBottom:'0.75rem' }}>
                <span style={{ fontSize:'0.85rem', fontWeight:600, display:'block', marginBottom:'0.2rem' }}>Estado</span>
                <select value={formParam.estado ?? p.estado} onChange={e => setFormParam(x=>({...x,estado:e.target.value}))}
                  style={{ width:'100%', padding:'0.45rem 0.6rem', border:'1px solid #cbd5e0', borderRadius:'4px', fontSize:'0.9rem' }}>
                  {ESTADOS_PARAM.map(o => <option key={o} value={o}>{o}</option>)}
                </select>
              </label>
              <div style={{ display:'flex', gap:'0.75rem', marginTop:'1rem' }}>
                {btn(savingParam ? 'Guardando...' : 'Guardar', () => actualizarParametros(p.idParametro), 'primary', savingParam)}
                {btn('Cancelar', () => setEditParam(null), 'secondary')}
              </div>
            </div>
          ) : (
            <div key={p.idParametro} style={{ background:'#fff', border:'1px solid #e2e8f0', borderRadius:'8px', padding:'1rem 1.25rem', marginBottom:'1rem', display:'grid', gridTemplateColumns:'repeat(auto-fill,minmax(200px,1fr))', gap:'0.5rem 1.5rem' }}>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>% máx cupo</span><div className="mono">{p.porcentajeMaximoCupo}%</div></div>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Salario mín</span><div className="mono">{p.salarioMinimoEmpleado ? fmtMoney(p.salarioMinimoEmpleado) : '—'}</div></div>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Salario máx</span><div className="mono">{p.salarioMaximoEmpleado ? fmtMoney(p.salarioMaximoEmpleado) : '—'}</div></div>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>IVA</span><div className="mono">{p.ivaPorcentaje}%</div></div>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Cobro comisión</span>
                <div style={{ fontWeight:700, color: p.momentoCobroComision === 'ANTICIPADO' ? '#c05621' : '#276749' }}>
                  {p.momentoCobroComision}
                </div>
              </div>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Max anticipos</span><div className="mono">{p.maxAnticipacionesActivos}</div></div>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Validación empresa</span><div>{p.requiereValidacionEmpresa ? 'Sí' : 'No'}</div></div>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Múltiple</span><div>{p.permiteAnticipoMultiple ? 'Sí' : 'No'}</div></div>
              <div><span style={{ fontSize:'0.78rem', color:'#718096' }}>Estado</span><div>{estadoBadge(p.estado)}</div></div>
              <div style={{ display:'flex', alignItems:'flex-end' }}>
                <button className="btn-link" style={{ fontSize:'0.8rem', color:'#3182ce' }}
                  onClick={() => { setEditParam(p); setFormParam({}); limpiarMsg(); }}>Editar</button>
              </div>
            </div>
          )
        ))}
      </div>}

      {/* ── Rangos ── */}
      {detalleTab === 'rangos' && <div>
        <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:'0.75rem' }}>
          <h3 style={{ margin:0 }}>Rangos de cobro</h3>
          {btn('+ Agregar rango', () => { setShowNewRango(true); setEditRango(null); limpiarMsg(); }, 'secondary')}
        </div>

        {showNewRango && (
          <div style={{ ...formStyle, background:'#f7fafc' }}>
            <h4 style={{ margin:'0 0 1rem' }}>Nuevo rango</h4>
            {inp('Valor desde', newRango.valorDesde, v => setNewRango(p=>({...p,valorDesde:v})), 'number')}
            {inp('Valor hasta', newRango.valorHasta, v => setNewRango(p=>({...p,valorHasta:v})), 'number')}
            {sel('Tipo cobro', newRango.tipoCobro, v => setNewRango(p=>({...p,tipoCobro:v})), TIPOS_COBRO)}
            {inp('Valor cobro', newRango.valorCobro, v => setNewRango(p=>({...p,valorCobro:v})), 'number')}
            {chk('Aplica IVA', newRango.aplicaIva, v => setNewRango(p=>({...p,aplicaIva:v})))}
            {sel('Estado', newRango.estado, v => setNewRango(p=>({...p,estado:v})), ESTADOS_PARAM)}
            <div style={{ display:'flex', gap:'0.75rem', marginTop:'1rem' }}>
              {btn(savingRango ? 'Guardando...' : 'Guardar', crearRango, 'primary', savingRango)}
              {btn('Cancelar', () => setShowNewRango(false), 'secondary')}
            </div>
          </div>
        )}

        {rangos.length === 0 && !showNewRango && <div className="empty">Sin rangos de cobro. Agrega uno.</div>}
        {rangos.length > 0 && (

          <div className="table-wrapper">
            <table>
              <thead><tr>
                <th>ID</th><th>Desde</th><th>Hasta</th><th>Tipo</th>
                <th>Comisión</th><th>IVA</th><th>Estado</th><th>Acciones</th>
              </tr></thead>
              <tbody>
                {rangos.map(r => (
                  editRango?.idRango === r.idRango ? (
                    <tr key={r.idRango}>
                      <td colSpan={8}>
                        <div style={{ padding:'0.75rem 0' }}>
                          <div style={{ display:'grid', gridTemplateColumns:'repeat(auto-fill,minmax(160px,1fr))', gap:'0.5rem 1rem' }}>
                            {inp('Desde', String(formRango.valorDesde ?? r.valorDesde), v => setFormRango(x=>({...x,valorDesde:Number(v)})), 'number')}
                            {inp('Hasta', String(formRango.valorHasta ?? r.valorHasta), v => setFormRango(x=>({...x,valorHasta:Number(v)})), 'number')}
                            {sel('Tipo', (formRango.tipoCobro ?? r.tipoCobro) as string, v => setFormRango(x=>({...x,tipoCobro:v})), TIPOS_COBRO)}
                            {inp('Comisión', String(formRango.valorCobro ?? r.valorCobro), v => setFormRango(x=>({...x,valorCobro:Number(v)})), 'number')}
                          </div>
                          <div style={{ display:'flex', gap:'1rem', alignItems:'center', flexWrap:'wrap' }}>
                            {chk('Aplica IVA', formRango.aplicaIva ?? r.aplicaIva, v => setFormRango(x=>({...x,aplicaIva:v})))}
                            <label style={{ display:'flex', alignItems:'center', gap:'0.5rem', fontSize:'0.85rem', fontWeight:600 }}>
                              Estado:
                              <select value={(formRango.estado ?? r.estado) as string} onChange={e => setFormRango(x=>({...x,estado:e.target.value}))}
                                style={{ padding:'0.3rem 0.5rem', border:'1px solid #cbd5e0', borderRadius:'4px' }}>
                                {ESTADOS_PARAM.map(o => <option key={o} value={o}>{o}</option>)}
                              </select>
                            </label>
                            {btn(savingRango ? 'Guardando...' : 'Guardar', () => actualizarRango(r.idRango), 'primary', savingRango)}
                            {btn('Cancelar', () => setEditRango(null), 'secondary')}
                          </div>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    <tr key={r.idRango}>
                      <td className="mono">{r.idRango}</td>
                      <td className="mono">{fmtMoney(r.valorDesde)}</td>
                      <td className="mono">{fmtMoney(r.valorHasta)}</td>
                      <td>{r.tipoCobro}</td>
                      <td className="mono" style={{ fontWeight:600 }}>{fmtMoney(r.valorCobro)}</td>
                      <td>{r.aplicaIva ? 'Sí' : 'No'}</td>
                      <td>{estadoBadge(r.estado)}</td>
                      <td>
                        <button className="btn-link" style={{ fontSize:'0.8rem', color:'#3182ce' }}
                          onClick={() => { setEditRango(r); setFormRango({}); limpiarMsg(); }}>Editar</button>
                      </td>
                    </tr>
                  )
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>}

      {/* ── Empleados (admin) ── */}
      {detalleTab === 'empleados' && (
        <div>
          <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:'0.75rem' }}>
            <h3 style={{ margin:0 }}>Empleados del convenio</h3>
            {btn('↺ Refrescar', () => selected && cargarEmpleados(selected.idConvenio), 'secondary')}
          </div>
          {loadingEmp && <p>Cargando...</p>}
          {!loadingEmp && empleados.length === 0 && <div className="empty">Sin empleados registrados.</div>}
          {empleados.length > 0 && (
            <div style={{ overflowX:'auto' }}>
              <table>
                <thead><tr>
                  <th>Doc</th><th>Nombres</th><th>Cargo</th><th>Salario</th>
                  <th>Cupo prelim.</th><th>Período</th><th>Estado</th><th>Origen</th><th>Lote</th>
                </tr></thead>
                <tbody>
                  {empleados.map(e => (
                    <tr key={e.idEmpleado}>
                      <td className="mono">{e.tipoDocumento} {e.numeroDocumento}</td>
                      <td style={{ fontWeight:600 }}>{e.nombres}{e.apellidos ? ` ${e.apellidos}` : ''}</td>
                      <td>{e.cargo ?? '—'}</td>
                      <td className="mono">{fmtMoney(e.salarioMensual)}</td>
                      <td className="mono" style={{ fontWeight:700, color:'#1e40af' }}>{fmtMoney(e.cupoPreliminar)}</td>
                      <td>{e.periodicidadPago}</td>
                      <td>{estadoBadge(e.estado)}</td>
                      <td><span className={`badge ${e.origenCarga === 'EXCEL' ? 'badge-info' : 'badge-warn'}`}>{e.origenCarga}</span></td>
                      <td className="mono" style={{ fontSize:'0.75rem', color:'#718096' }}>{e.loteImportacion ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* ── Importaciones ── */}
      {detalleTab === 'importaciones' && (
        <div>
          <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:'0.75rem' }}>
            <h3 style={{ margin:0 }}>Historial de importaciones</h3>
            {btn('↺ Refrescar', () => selected && cargarEmpleados(selected.idConvenio), 'secondary')}
          </div>
          {loadingEmp && <p>Cargando...</p>}
          {!loadingEmp && importaciones.length === 0 && <div className="empty">Sin importaciones.</div>}
          {importaciones.length > 0 && (
            <div>
              {importaciones.map(i => (
                <div key={i.idImportacion} style={{ background:'#fff', border:'1px solid #e2e8f0', borderRadius:'8px', padding:'1rem 1.25rem', marginBottom:'0.75rem' }}>
                  <div style={{ display:'grid', gridTemplateColumns:'repeat(auto-fill,minmax(160px,1fr))', gap:'0.4rem 1.5rem', marginBottom:'0.5rem' }}>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Lote</span><div className="mono" style={{ fontSize:'0.8rem' }}>{i.loteImportacion}</div></div>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Archivo</span><div style={{ fontSize:'0.8rem' }}>{i.nombreArchivo ?? '—'}</div></div>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Total filas</span><div style={{ fontWeight:700 }}>{i.totalFilas}</div></div>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Válidas</span><div style={{ fontWeight:700, color:'#166534' }}>{i.filasValidas}</div></div>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Errores</span><div style={{ fontWeight:700, color: i.filasError > 0 ? '#c05621' : '#718096' }}>{i.filasError}</div></div>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Creados</span><div style={{ fontWeight:700 }}>{i.empleadosCreados}</div></div>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Actualizados</span><div style={{ fontWeight:700 }}>{i.empleadosActualizados}</div></div>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Estado</span><div>{estadoBadge(i.estado)}</div></div>
                    <div><span style={{ fontSize:'0.75rem', color:'#718096' }}>Fecha</span><div style={{ fontSize:'0.8rem' }}>{fmtDate(i.createdAt)}</div></div>
                  </div>
                  {i.errores.length > 0 && (
                    <div>
                      <button className="btn-link" style={{ fontSize:'0.8rem' }}
                        onClick={() => setExpandImport(expandImport === i.idImportacion ? null : i.idImportacion)}>
                        {expandImport === i.idImportacion ? 'Ocultar errores' : `Ver ${i.errores.length} errores`}
                      </button>
                      {expandImport === i.idImportacion && (
                        <table style={{ marginTop:'0.5rem', width:'100%', borderCollapse:'collapse', fontSize:'0.78rem' }}>
                          <thead><tr>
                            {['Fila','Campo','Mensaje'].map(h => <th key={h} style={{ textAlign:'left', padding:'0.3rem', background:'#fef9c3', borderBottom:'1px solid #fcd34d' }}>{h}</th>)}
                          </tr></thead>
                          <tbody>
                            {i.errores.map((er, idx) => (
                              <tr key={idx}>
                                <td style={{ padding:'0.3rem', borderBottom:'1px solid #fef3c7' }}>{er.fila}</td>
                                <td style={{ padding:'0.3rem', borderBottom:'1px solid #fef3c7' }}>{er.campo}</td>
                                <td style={{ padding:'0.3rem', borderBottom:'1px solid #fef3c7' }}>{er.mensaje}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      )}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* ── Usuarios empresa ── */}
      {detalleTab === 'usuarios' && (
        <div>
          <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:'0.75rem' }}>
            <h3 style={{ margin:0 }}>Usuarios empresa</h3>
            {btn('↺ Refrescar', () => selected && cargarEmpleados(selected.idConvenio), 'secondary')}
          </div>
          {loadingEmp && <p>Cargando...</p>}

          {/* Form asociar usuario */}
          <div style={{ background:'#f7fafc', border:'1px solid #e2e8f0', borderRadius:'8px', padding:'1rem 1.25rem', marginBottom:'1.25rem' }}>
            <h4 style={{ margin:'0 0 0.75rem' }}>Asociar usuario al convenio</h4>
            <div style={{ display:'grid', gridTemplateColumns:'1fr 1fr auto', gap:'0.75rem', alignItems:'flex-end', flexWrap:'wrap' }}>
              <label style={{ display:'block' }}>
                <span style={{ fontSize:'0.83rem', fontWeight:600, display:'block', marginBottom:'0.2rem' }}>ID usuario *</span>
                <input type="number" value={newUsuario.idUsuario}
                  onChange={e => setNewUsuario(p=>({...p, idUsuario: e.target.value}))}
                  placeholder="ej: 12"
                  style={{ width:'100%', padding:'0.4rem 0.6rem', border:'1px solid #cbd5e0', borderRadius:'4px', fontSize:'0.9rem' }} />
              </label>
              <label style={{ display:'block' }}>
                <span style={{ fontSize:'0.83rem', fontWeight:600, display:'block', marginBottom:'0.2rem' }}>Rol empresa</span>
                <select value={newUsuario.rolEmpresa}
                  onChange={e => setNewUsuario(p=>({...p, rolEmpresa: e.target.value}))}
                  style={{ width:'100%', padding:'0.4rem 0.6rem', border:'1px solid #cbd5e0', borderRadius:'4px', fontSize:'0.9rem' }}>
                  {['ADMIN_EMPRESA','OPERADOR_EMPRESA','CONSULTA_EMPRESA'].map(r => <option key={r} value={r}>{r}</option>)}
                </select>
              </label>
              {btn(savingUsuario ? 'Guardando...' : 'Asociar', asociarUsuario, 'primary', savingUsuario)}
            </div>
          </div>

          {usuariosEmp.length === 0 && !loadingEmp && <div className="empty">Sin usuarios asociados.</div>}
          {usuariosEmp.length > 0 && (
            <table>
              <thead><tr>
                <th>ID asoc.</th><th>ID usuario</th><th>Rol</th><th>Estado</th><th>Desde</th>
              </tr></thead>
              <tbody>
                {usuariosEmp.map(u => (
                  <tr key={u.idUsuarioEmpresa}>
                    <td className="mono">{u.idUsuarioEmpresa}</td>
                    <td className="mono">{u.idUsuario}</td>
                    <td>{u.rolEmpresa}</td>
                    <td>{estadoBadge(u.estado)}</td>
                    <td className="mono">{fmtDate(u.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </div>
  );

  return null;
}
