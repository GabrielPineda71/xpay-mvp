import { useEffect, useRef, useState } from 'react';
import { get, post, put, API_BASE_URL } from '../api/client.ts';
import { fmtDate } from '../utils.ts';

// ── Types ─────────────────────────────────────────────────────────────────────

interface ComercioListItem {
  idComercioAliado: number;
  razonSocial: string;
  nombreComercial: string;
  nit: string;
  ciudad?: string;
  estado: string;
  fechaSolicitud: string;
  createdAt: string;
}

interface Comercio {
  idComercioAliado: number;
  idComercioExistente?: number;
  razonSocial: string;
  nombreComercial: string;
  nit: string;
  tipoPersona: string;
  actividadEconomica?: string;
  codigoCiiu?: string;
  direccionPrincipal?: string;
  ciudad?: string;
  departamento?: string;
  telefono?: string;
  correo?: string;
  sitioWeb?: string;
  estado: string;
  condicionesComerciales?: string;
  fechaSolicitud: string;
  fechaAprobacion?: string;
  fechaInicioConvenio?: string;
  fechaFinConvenio?: string;
  observaciones?: string;
  createdAt: string;
  updatedAt?: string;
}

interface Representante {
  idRepresentante: number;
  idComercioAliado: number;
  tipoDocumento: string;
  numeroDocumento: string;
  nombres: string;
  apellidos?: string;
  celular?: string;
  correo?: string;
  cargo?: string;
  fechaExpedicionDocumento?: string;
  estado: string;
  createdAt: string;
}

interface Establecimiento {
  idEstablecimiento: number;
  idComercioAliado: number;
  nombreEstablecimiento: string;
  direccion?: string;
  ciudad?: string;
  telefono?: string;
  responsable?: string;
  estado: string;
  createdAt: string;
}

interface UsuarioSolicitado {
  idUsuarioSolicitado: number;
  idComercioAliado: number;
  nombres: string;
  correo?: string;
  celular?: string;
  rolSolicitado: string;
  estado: string;
  createdAt: string;
}

interface Documento {
  idDocumento: number;
  idComercioAliado: number;
  tipoDocumento: string;
  nombreArchivoOriginal: string;
  contentType?: string;
  sizeBytes?: number;
  estado: string;
  observaciones?: string;
  uploadedAt: string;
}

interface Compleitud {
  contrato: boolean;
  camaraComercio: boolean;
  rut: boolean;
  documentoRepresentante: boolean;
  formularioSolicitud: boolean;
  totalCargados: number;
  totalRequeridos: number;
}

// ── Constants ─────────────────────────────────────────────────────────────────

const ESTADOS        = ['BORRADOR','EN_REVISION','APROBADO','RECHAZADO','ACTIVO','INACTIVO'];
const TIPOS_PERSONA  = ['NATURAL','JURIDICA'];
const TIPOS_DOC_REP  = ['CC','CE','NIT','PASAPORTE','OTRO'];
const TIPOS_DOC_ARCH = ['CONTRATO','CAMARA_COMERCIO','RUT','DOCUMENTO_REPRESENTANTE','FORMULARIO_SOLICITUD'];
const ROLES_SOL      = ['ADMIN_COMERCIO','CAJERO'];

type Vista      = 'lista' | 'crear' | 'editar' | 'detalle';
type DetalleTab = 'datos' | 'representante' | 'establecimientos' | 'usuarios' | 'documentos' | 'convenio';

// ── Pequeños helpers de estilo ────────────────────────────────────────────────

const tdStyle  = { padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0', fontSize: '0.82rem' };
const thStyle  = { textAlign: 'left' as const, padding: '0.4rem 0.5rem', color: '#718096', borderBottom: '1px solid #e2e8f0', fontWeight: 600, fontSize: '0.82rem' };
const formStyle: React.CSSProperties = { maxWidth: 560, display: 'grid', gap: '0.75rem' };
const fmtBytes = (b?: number | null) => b ? `${(b / 1024).toFixed(1)} KB` : '—';

function estadoBadge(estado: string) {
  const cls: Record<string,string> = {
    ACTIVO:'badge-ok', BORRADOR:'badge-warn', EN_REVISION:'badge-warn',
    APROBADO:'badge-ok', RECHAZADO:'badge-error', INACTIVO:'badge-error',
    PENDIENTE_CREACION:'badge-warn', CREADO:'badge-ok',
  };
  return <span className={`badge ${cls[estado] ?? 'badge'}`}>{estado}</span>;
}

function inp(label: string, val: string, set: (v: string) => void, type = 'text', required = false) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem', fontSize: '0.85rem', fontWeight: 600 }}>
      {label}{required && <span style={{ color: '#e53e3e' }}> *</span>}
      <input
        type={type} value={val} onChange={e => set(e.target.value)} required={required}
        style={{ border: '1px solid #cbd5e0', borderRadius: '0.375rem', padding: '0.4rem 0.6rem', fontSize: '0.85rem', fontWeight: 400 }}
      />
    </label>
  );
}

function sel(label: string, val: string, set: (v: string) => void, opts: string[]) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem', fontSize: '0.85rem', fontWeight: 600 }}>
      {label}
      <select value={val} onChange={e => set(e.target.value)}
        style={{ border: '1px solid #cbd5e0', borderRadius: '0.375rem', padding: '0.4rem 0.6rem', fontSize: '0.85rem', fontWeight: 400 }}>
        {opts.map(o => <option key={o} value={o}>{o}</option>)}
      </select>
    </label>
  );
}

function textarea(label: string, val: string, set: (v: string) => void) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem', fontSize: '0.85rem', fontWeight: 600 }}>
      {label}
      <textarea value={val} onChange={e => set(e.target.value)} rows={3}
        style={{ border: '1px solid #cbd5e0', borderRadius: '0.375rem', padding: '0.4rem 0.6rem', fontSize: '0.85rem', fontWeight: 400, resize: 'vertical' }} />
    </label>
  );
}

function btn(label: string, onClick: () => void, variant: 'primary' | 'secondary' | 'danger' = 'primary', disabled = false) {
  const bg = variant === 'primary' ? '#3182ce' : variant === 'danger' ? '#e53e3e' : '#edf2f7';
  const color = variant === 'secondary' ? '#4a5568' : '#fff';
  return (
    <button onClick={onClick} disabled={disabled}
      style={{ padding: '0.45rem 1rem', background: disabled ? '#a0aec0' : bg, color, border: 'none', borderRadius: '0.375rem', cursor: disabled ? 'not-allowed' : 'pointer', fontSize: '0.85rem', fontWeight: 600 }}>
      {label}
    </button>
  );
}

// ── Empty form states ─────────────────────────────────────────────────────────

const emptyComercio = () => ({
  razonSocial: '', nombreComercial: '', nit: '', tipoPersona: 'JURIDICA',
  actividadEconomica: '', codigoCiiu: '', direccionPrincipal: '', ciudad: '',
  departamento: '', telefono: '', correo: '', sitioWeb: '', estado: 'BORRADOR',
  condicionesComerciales: '', fechaInicioConvenio: '', fechaFinConvenio: '', observaciones: '',
});

const emptyRep = () => ({
  tipoDocumento: 'CC', numeroDocumento: '', nombres: '', apellidos: '',
  celular: '', correo: '', cargo: '', fechaExpedicionDocumento: '',
});

const emptyEst = () => ({
  nombreEstablecimiento: '', direccion: '', ciudad: '', telefono: '', responsable: '',
});

const emptyUsr = () => ({
  nombres: '', correo: '', celular: '', rolSolicitado: 'ADMIN_COMERCIO',
});

// ── Main component ────────────────────────────────────────────────────────────

export function ComerciosAliadosPage() {
  const [vista,     setVista]    = useState<Vista>('lista');
  const [comercios, setComercios] = useState<ComercioListItem[]>([]);
  const [selected,  setSelected]  = useState<Comercio | null>(null);
  const [loading,   setLoading]   = useState(false);
  const [error,     setError]     = useState('');
  const [msg,       setMsg]       = useState('');

  // Formulario comercio
  const [form, setForm] = useState(emptyComercio());
  const [saving, setSaving] = useState(false);

  // Detalle tabs
  const [tab, setTab] = useState<DetalleTab>('datos');
  const [representantes,     setRepresentantes]     = useState<Representante[]>([]);
  const [establecimientos,   setEstablecimientos]   = useState<Establecimiento[]>([]);
  const [usuariosSolicitados, setUsuariosSolicitados] = useState<UsuarioSolicitado[]>([]);
  const [documentos,         setDocumentos]         = useState<Documento[]>([]);
  const [compleitud,         setCompleitud]         = useState<Compleitud | null>(null);

  // Forms sub-entidades
  const [showFormRep,  setShowFormRep]  = useState(false);
  const [showFormEst,  setShowFormEst]  = useState(false);
  const [showFormUsr,  setShowFormUsr]  = useState(false);
  const [formRep,      setFormRep]      = useState(emptyRep());
  const [formEst,      setFormEst]      = useState(emptyEst());
  const [formUsr,      setFormUsr]      = useState(emptyUsr());
  const [editRepId,    setEditRepId]    = useState<number | null>(null);
  const [editEstId,    setEditEstId]    = useState<number | null>(null);
  const [editUsrId,    setEditUsrId]    = useState<number | null>(null);
  const [savingSub,    setSavingSub]    = useState(false);

  // Documentos upload
  const [tipoDocUpload, setTipoDocUpload] = useState(TIPOS_DOC_ARCH[0]);
  const [obsDocUpload,  setObsDocUpload]  = useState('');
  const [uploadFile,    setUploadFile]    = useState<File | null>(null);
  const [uploading,     setUploading]     = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const limpiarMsg = () => { setError(''); setMsg(''); };

  // ── Carga lista ─────────────────────────────────────────────────────────────

  const cargarLista = async () => {
    setLoading(true); limpiarMsg();
    try {
      const r = await get<{ success: boolean; data: ComercioListItem[] }>('/api/comercios-aliados/admin');
      setComercios(r.data);
    } catch (e) { setError((e as Error).message); }
    finally { setLoading(false); }
  };

  useEffect(() => { cargarLista(); }, []);

  // ── Carga detalle ───────────────────────────────────────────────────────────

  const cargarDetalle = async (c: Comercio) => {
    setSelected(c); setTab('datos'); limpiarMsg();
    setRepresentantes([]); setEstablecimientos([]);
    setUsuariosSolicitados([]); setDocumentos([]); setCompleitud(null);
    setVista('detalle');
    // Precargar datos sub-entidades
    const id = c.idComercioAliado;
    try { const r = await get<any>(`/api/comercios-aliados/admin/${id}/representantes`); setRepresentantes(r.data); } catch { /**/ }
    try { const r = await get<any>(`/api/comercios-aliados/admin/${id}/establecimientos`); setEstablecimientos(r.data); } catch { /**/ }
    try { const r = await get<any>(`/api/comercios-aliados/admin/${id}/usuarios-solicitados`); setUsuariosSolicitados(r.data); } catch { /**/ }
    try { const r = await get<any>(`/api/comercios-aliados/admin/${id}/documentos`); setDocumentos(r.data.documentos); setCompleitud(r.data.compleitud); } catch { /**/ }
  };

  // ── Guardar comercio ────────────────────────────────────────────────────────

  const guardar = async () => {
    limpiarMsg(); setSaving(true);
    const body = {
      razonSocial: form.razonSocial, nombreComercial: form.nombreComercial,
      nit: form.nit, tipoPersona: form.tipoPersona,
      actividadEconomica: form.actividadEconomica || null,
      codigoCiiu: form.codigoCiiu || null,
      direccionPrincipal: form.direccionPrincipal || null,
      ciudad: form.ciudad || null, departamento: form.departamento || null,
      telefono: form.telefono || null, correo: form.correo || null,
      sitioWeb: form.sitioWeb || null, estado: form.estado,
      condicionesComerciales: form.condicionesComerciales || null,
      fechaInicioConvenio: form.fechaInicioConvenio || null,
      fechaFinConvenio: form.fechaFinConvenio || null,
      observaciones: form.observaciones || null,
    };
    try {
      if (vista === 'crear') {
        await post('/api/comercios-aliados/admin', body);
        setMsg('Comercio aliado creado exitosamente.');
      } else if (vista === 'editar' && selected) {
        await put(`/api/comercios-aliados/admin/${selected.idComercioAliado}`, body);
        setMsg('Comercio aliado actualizado.');
      }
      await cargarLista();
      setVista('lista');
    } catch (e) { setError((e as Error).message); }
    finally { setSaving(false); }
  };

  const iniciarEditar = (c: Comercio) => {
    setSelected(c);
    setForm({
      razonSocial: c.razonSocial, nombreComercial: c.nombreComercial,
      nit: c.nit, tipoPersona: c.tipoPersona,
      actividadEconomica: c.actividadEconomica ?? '', codigoCiiu: c.codigoCiiu ?? '',
      direccionPrincipal: c.direccionPrincipal ?? '', ciudad: c.ciudad ?? '',
      departamento: c.departamento ?? '', telefono: c.telefono ?? '',
      correo: c.correo ?? '', sitioWeb: c.sitioWeb ?? '', estado: c.estado,
      condicionesComerciales: c.condicionesComerciales ?? '',
      fechaInicioConvenio: c.fechaInicioConvenio ?? '',
      fechaFinConvenio: c.fechaFinConvenio ?? '',
      observaciones: c.observaciones ?? '',
    });
    limpiarMsg(); setVista('editar');
  };

  const cambiarEstado = async (id: number, accion: 'activar' | 'inactivar') => {
    limpiarMsg();
    try {
      await post(`/api/comercios-aliados/admin/${id}/${accion}`, {});
      setMsg(`Comercio ${accion === 'activar' ? 'activado' : 'inactivado'}.`);
      await cargarLista();
    } catch (e) { setError((e as Error).message); }
  };

  // ── Sub-entidades: representantes ───────────────────────────────────────────

  const guardarRep = async () => {
    if (!selected) return;
    limpiarMsg(); setSavingSub(true);
    try {
      const body = { ...formRep };
      if (editRepId) {
        await put(`/api/comercios-aliados/admin/representantes/${editRepId}`, body);
      } else {
        await post(`/api/comercios-aliados/admin/${selected.idComercioAliado}/representantes`, body);
      }
      const r = await get<any>(`/api/comercios-aliados/admin/${selected.idComercioAliado}/representantes`);
      setRepresentantes(r.data);
      setShowFormRep(false); setFormRep(emptyRep()); setEditRepId(null);
      setMsg('Representante guardado.');
    } catch (e) { setError((e as Error).message); }
    finally { setSavingSub(false); }
  };

  // ── Sub-entidades: establecimientos ────────────────────────────────────────

  const guardarEst = async () => {
    if (!selected) return;
    limpiarMsg(); setSavingSub(true);
    try {
      const body = { ...formEst };
      if (editEstId) {
        await put(`/api/comercios-aliados/admin/establecimientos/${editEstId}`, body);
      } else {
        await post(`/api/comercios-aliados/admin/${selected.idComercioAliado}/establecimientos`, body);
      }
      const r = await get<any>(`/api/comercios-aliados/admin/${selected.idComercioAliado}/establecimientos`);
      setEstablecimientos(r.data);
      setShowFormEst(false); setFormEst(emptyEst()); setEditEstId(null);
      setMsg('Establecimiento guardado.');
    } catch (e) { setError((e as Error).message); }
    finally { setSavingSub(false); }
  };

  // ── Sub-entidades: usuarios solicitados ────────────────────────────────────

  const guardarUsr = async () => {
    if (!selected) return;
    limpiarMsg(); setSavingSub(true);
    try {
      const body = { ...formUsr };
      if (editUsrId) {
        await put(`/api/comercios-aliados/admin/usuarios-solicitados/${editUsrId}`, body);
      } else {
        await post(`/api/comercios-aliados/admin/${selected.idComercioAliado}/usuarios-solicitados`, body);
      }
      const r = await get<any>(`/api/comercios-aliados/admin/${selected.idComercioAliado}/usuarios-solicitados`);
      setUsuariosSolicitados(r.data);
      setShowFormUsr(false); setFormUsr(emptyUsr()); setEditUsrId(null);
      setMsg('Usuario solicitado guardado.');
    } catch (e) { setError((e as Error).message); }
    finally { setSavingSub(false); }
  };

  // ── Documentos ─────────────────────────────────────────────────────────────

  const subirDocumento = async () => {
    if (!selected || !uploadFile) return;
    limpiarMsg(); setUploading(true);
    try {
      const fd = new FormData();
      fd.append('archivo', uploadFile);
      fd.append('tipoDocumento', tipoDocUpload);
      if (obsDocUpload) fd.append('observaciones', obsDocUpload);

      const token = localStorage.getItem('xpay_token');
      const resp = await fetch(`${API_BASE_URL}/api/comercios-aliados/admin/${selected.idComercioAliado}/documentos`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
        body: fd,
      });
      if (!resp.ok) {
        const j = await resp.json().catch(() => ({}));
        throw new Error(j.message ?? `HTTP ${resp.status}`);
      }
      setMsg('Documento subido exitosamente.');
      setUploadFile(null); setObsDocUpload('');
      if (fileRef.current) fileRef.current.value = '';
      const r = await get<any>(`/api/comercios-aliados/admin/${selected.idComercioAliado}/documentos`);
      setDocumentos(r.data.documentos); setCompleitud(r.data.compleitud);
    } catch (e) { setError((e as Error).message); }
    finally { setUploading(false); }
  };

  const descargarDocumento = (doc: Documento) => {
    const token = localStorage.getItem('xpay_token');
    const url   = `${API_BASE_URL}/api/comercios-aliados/admin/documentos/${doc.idDocumento}/download`;
    const a     = document.createElement('a');
    fetch(url, { headers: { Authorization: `Bearer ${token}` } })
      .then(r => r.blob())
      .then(blob => {
        a.href = URL.createObjectURL(blob);
        a.download = doc.nombreArchivoOriginal;
        a.click();
        URL.revokeObjectURL(a.href);
      })
      .catch(() => setError('Error descargando documento.'));
  };

  const eliminarDocumento = async (idDocumento: number) => {
    if (!selected) return;
    limpiarMsg();
    try {
      await post(`/api/comercios-aliados/admin/documentos/${idDocumento}/eliminar`, {});
      setMsg('Documento eliminado.');
      const r = await get<any>(`/api/comercios-aliados/admin/${selected.idComercioAliado}/documentos`);
      setDocumentos(r.data.documentos); setCompleitud(r.data.compleitud);
    } catch (e) { setError((e as Error).message); }
  };

  // ── UI helpers ─────────────────────────────────────────────────────────────

  const Notif = () => (
    <>
      {error && <div className="error-banner" style={{ marginBottom: '1rem' }}>{error}</div>}
      {msg   && <div className="success-banner" style={{ marginBottom: '1rem' }}>{msg}</div>}
    </>
  );

  // ── Vista: lista ───────────────────────────────────────────────────────────

  if (vista === 'lista') return (
    <div className="page">
      <div style={{ display:'flex', alignItems:'center', justifyContent:'space-between', marginBottom:'1.5rem', flexWrap:'wrap', gap:'1rem' }}>
        <h2 style={{ margin:0 }}>Comercios Aliados</h2>
        {btn('+ Nuevo comercio aliado', () => { setForm(emptyComercio()); limpiarMsg(); setVista('crear'); })}
      </div>
      <Notif />
      {loading ? <p>Cargando...</p> : comercios.length === 0 ? (
        <p style={{ color:'#718096' }}>No hay comercios aliados registrados.</p>
      ) : (
        <div style={{ overflowX:'auto' }}>
          <table style={{ width:'100%', borderCollapse:'collapse', fontSize:'0.85rem' }}>
            <thead>
              <tr>{['ID','Razón social','Nombre comercial','NIT','Ciudad','Estado','Fecha solicitud','Acciones'].map(h =>
                <th key={h} style={thStyle}>{h}</th>
              )}</tr>
            </thead>
            <tbody>
              {comercios.map(c => (
                <tr key={c.idComercioAliado}>
                  <td style={tdStyle} className="mono">{c.idComercioAliado}</td>
                  <td style={{ ...tdStyle, fontWeight:600 }}>{c.razonSocial}</td>
                  <td style={tdStyle}>{c.nombreComercial}</td>
                  <td style={tdStyle} className="mono">{c.nit}</td>
                  <td style={tdStyle}>{c.ciudad ?? '—'}</td>
                  <td style={tdStyle}>{estadoBadge(c.estado)}</td>
                  <td style={tdStyle} className="mono">{fmtDate(c.fechaSolicitud)}</td>
                  <td style={tdStyle}>
                    <div style={{ display:'flex', gap:'0.4rem', flexWrap:'wrap' }}>
                      <button className="btn-link" style={{ fontSize:'0.8rem' }}
                        onClick={async () => {
                          const r = await get<any>(`/api/comercios-aliados/admin/${c.idComercioAliado}`);
                          cargarDetalle(r.data);
                        }}>Ver</button>
                      <button className="btn-link" style={{ fontSize:'0.8rem', color:'#3182ce' }}
                        onClick={async () => {
                          const r = await get<any>(`/api/comercios-aliados/admin/${c.idComercioAliado}`);
                          iniciarEditar(r.data);
                        }}>Editar</button>
                      {c.estado !== 'ACTIVO' && (
                        <button className="btn-link" style={{ fontSize:'0.8rem', color:'#38a169' }}
                          onClick={() => cambiarEstado(c.idComercioAliado, 'activar')}>Activar</button>
                      )}
                      {c.estado === 'ACTIVO' && (
                        <button className="btn-link" style={{ fontSize:'0.8rem', color:'#e53e3e' }}
                          onClick={() => cambiarEstado(c.idComercioAliado, 'inactivar')}>Inactivar</button>
                      )}
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

  // ── Vista: crear / editar ──────────────────────────────────────────────────

  if (vista === 'crear' || vista === 'editar') {
    const titulo = vista === 'crear' ? 'Nuevo comercio aliado' : `Editar: ${selected?.razonSocial}`;
    return (
      <div className="page">
        <h2>{titulo}</h2>
        <Notif />
        <div style={formStyle}>
          {inp('Razón social *', form.razonSocial, v => setForm(p=>({...p, razonSocial:v})), 'text', true)}
          {inp('Nombre comercial *', form.nombreComercial, v => setForm(p=>({...p, nombreComercial:v})), 'text', true)}
          {vista === 'crear' && inp('NIT *', form.nit, v => setForm(p=>({...p, nit:v})), 'text', true)}
          {vista === 'editar' && <div style={{ fontSize:'0.85rem' }}><b>NIT:</b> {selected?.nit} <span style={{color:'#a0aec0'}}>(no editable)</span></div>}
          {sel('Tipo persona', form.tipoPersona, v => setForm(p=>({...p, tipoPersona:v})), TIPOS_PERSONA)}
          {sel('Estado', form.estado, v => setForm(p=>({...p, estado:v})), ESTADOS)}
          {inp('Actividad económica', form.actividadEconomica, v => setForm(p=>({...p, actividadEconomica:v})))}
          {inp('Código CIIU', form.codigoCiiu, v => setForm(p=>({...p, codigoCiiu:v})))}
          {inp('Dirección principal', form.direccionPrincipal, v => setForm(p=>({...p, direccionPrincipal:v})))}
          {inp('Ciudad', form.ciudad, v => setForm(p=>({...p, ciudad:v})))}
          {inp('Departamento', form.departamento, v => setForm(p=>({...p, departamento:v})))}
          {inp('Teléfono', form.telefono, v => setForm(p=>({...p, telefono:v})))}
          {inp('Correo', form.correo, v => setForm(p=>({...p, correo:v})), 'email')}
          {inp('Sitio web', form.sitioWeb, v => setForm(p=>({...p, sitioWeb:v})))}
          {inp('Fecha inicio convenio', form.fechaInicioConvenio, v => setForm(p=>({...p, fechaInicioConvenio:v})), 'date')}
          {inp('Fecha fin convenio', form.fechaFinConvenio, v => setForm(p=>({...p, fechaFinConvenio:v})), 'date')}
          {textarea('Condiciones comerciales', form.condicionesComerciales, v => setForm(p=>({...p, condicionesComerciales:v})))}
          {textarea('Observaciones', form.observaciones, v => setForm(p=>({...p, observaciones:v})))}
          <div style={{ display:'flex', gap:'0.75rem', marginTop:'0.5rem' }}>
            {btn(saving ? 'Guardando...' : 'Guardar', guardar, 'primary', saving)}
            {btn('Cancelar', () => { limpiarMsg(); setVista('lista'); }, 'secondary')}
          </div>
        </div>
      </div>
    );
  }

  // ── Vista: detalle ─────────────────────────────────────────────────────────

  if (vista === 'detalle' && selected) {
    const TABS: [DetalleTab, string][] = [
      ['datos','Datos'],['representante','Representante legal'],
      ['establecimientos',`Sedes (${establecimientos.length})`],
      ['usuarios',`Usuarios (${usuariosSolicitados.length})`],
      ['documentos','Documentos'],['convenio','Convenio'],
    ];

    return (
      <div className="page">
        <div style={{ display:'flex', alignItems:'center', gap:'1rem', marginBottom:'1.5rem', flexWrap:'wrap' }}>
          <button onClick={() => { limpiarMsg(); setVista('lista'); setSelected(null); }}
            style={{ background:'none', border:'none', cursor:'pointer', color:'#3182ce', fontSize:'0.9rem', padding:0 }}>
            ← Volver
          </button>
          <h2 style={{ margin:0 }}>{selected.nombreComercial}</h2>
          {estadoBadge(selected.estado)}
          <div style={{ display:'flex', gap:'0.5rem', marginLeft:'auto' }}>
            {btn('Editar', () => iniciarEditar(selected), 'secondary')}
            {selected.estado !== 'ACTIVO' && btn('Activar', () => cambiarEstado(selected.idComercioAliado, 'activar'), 'primary')}
            {selected.estado === 'ACTIVO'  && btn('Inactivar', () => cambiarEstado(selected.idComercioAliado, 'inactivar'), 'danger')}
          </div>
        </div>
        <Notif />

        {/* Tabs */}
        <div style={{ display:'flex', gap:'0.4rem', borderBottom:'2px solid #e2e8f0', marginBottom:'1.5rem', flexWrap:'wrap' }}>
          {TABS.map(([t, label]) => (
            <button key={t} onClick={() => setTab(t)}
              style={{
                padding:'0.45rem 0.9rem', border:'none', background:'none',
                borderBottom: tab === t ? '2px solid #3182ce' : '2px solid transparent',
                color: tab === t ? '#3182ce' : '#718096',
                fontWeight: tab === t ? 700 : 400, cursor:'pointer', fontSize:'0.85rem', marginBottom:'-2px'
              }}>{label}</button>
          ))}
        </div>

        {/* Tab: Datos */}
        {tab === 'datos' && (
          <div style={{ display:'grid', gridTemplateColumns:'repeat(auto-fill,minmax(220px,1fr))', gap:'0.5rem 1.5rem', background:'#f7fafc', border:'1px solid #e2e8f0', borderRadius:'8px', padding:'1rem 1.25rem' }}>
            {[
              ['NIT', selected.nit], ['Razón social', selected.razonSocial],
              ['Tipo persona', selected.tipoPersona], ['Actividad', selected.actividadEconomica ?? '—'],
              ['CIIU', selected.codigoCiiu ?? '—'], ['Dirección', selected.direccionPrincipal ?? '—'],
              ['Ciudad', selected.ciudad ?? '—'], ['Departamento', selected.departamento ?? '—'],
              ['Teléfono', selected.telefono ?? '—'], ['Correo', selected.correo ?? '—'],
              ['Sitio web', selected.sitioWeb ?? '—'],
            ].map(([k, v]) => (
              <div key={k}><span style={{ fontSize:'0.75rem', color:'#718096' }}>{k}</span><div style={{ fontSize:'0.87rem' }}>{v}</div></div>
            ))}
            {selected.observaciones && <div style={{ gridColumn:'1/-1' }}><span style={{ fontSize:'0.75rem', color:'#718096' }}>Observaciones</span><div style={{ fontSize:'0.87rem' }}>{selected.observaciones}</div></div>}
          </div>
        )}

        {/* Tab: Representante legal */}
        {tab === 'representante' && (
          <div>
            <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom:'1rem' }}>
              <h3 style={{ margin:0 }}>Representante legal</h3>
              {btn('+ Agregar', () => { setFormRep(emptyRep()); setEditRepId(null); setShowFormRep(true); }, 'secondary')}
            </div>
            {showFormRep && (
              <div style={{ ...formStyle, marginBottom:'1.5rem', background:'#f7fafc', padding:'1rem', borderRadius:'8px', border:'1px solid #e2e8f0' }}>
                {sel('Tipo documento', formRep.tipoDocumento, v => setFormRep(p=>({...p,tipoDocumento:v})), TIPOS_DOC_REP)}
                {inp('Número documento *', formRep.numeroDocumento, v => setFormRep(p=>({...p,numeroDocumento:v})), 'text', true)}
                {inp('Nombres *', formRep.nombres, v => setFormRep(p=>({...p,nombres:v})), 'text', true)}
                {inp('Apellidos', formRep.apellidos, v => setFormRep(p=>({...p,apellidos:v})))}
                {inp('Celular', formRep.celular, v => setFormRep(p=>({...p,celular:v})))}
                {inp('Correo', formRep.correo, v => setFormRep(p=>({...p,correo:v})), 'email')}
                {inp('Cargo', formRep.cargo, v => setFormRep(p=>({...p,cargo:v})))}
                {inp('Fecha expedición doc.', formRep.fechaExpedicionDocumento, v => setFormRep(p=>({...p,fechaExpedicionDocumento:v})), 'date')}
                <div style={{ display:'flex', gap:'0.75rem' }}>
                  {btn(savingSub ? 'Guardando...' : 'Guardar', guardarRep, 'primary', savingSub)}
                  {btn('Cancelar', () => { setShowFormRep(false); setFormRep(emptyRep()); }, 'secondary')}
                </div>
              </div>
            )}
            {representantes.length === 0 ? <p style={{ color:'#718096' }}>Sin representante legal registrado.</p> : (
              <div style={{ overflowX:'auto' }}>
                <table style={{ width:'100%', borderCollapse:'collapse', fontSize:'0.82rem' }}>
                  <thead><tr>{['Tipo','Documento','Nombres','Cargo','Correo','Estado',''].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr></thead>
                  <tbody>
                    {representantes.map(r => (
                      <tr key={r.idRepresentante}>
                        <td style={tdStyle}>{r.tipoDocumento}</td>
                        <td style={tdStyle} className="mono">{r.numeroDocumento}</td>
                        <td style={tdStyle}>{r.nombres}{r.apellidos ? ` ${r.apellidos}` : ''}</td>
                        <td style={tdStyle}>{r.cargo ?? '—'}</td>
                        <td style={tdStyle}>{r.correo ?? '—'}</td>
                        <td style={tdStyle}>{estadoBadge(r.estado)}</td>
                        <td style={tdStyle}>
                          <button className="btn-link" style={{ fontSize:'0.8rem' }} onClick={() => {
                            setFormRep({ tipoDocumento: r.tipoDocumento, numeroDocumento: r.numeroDocumento, nombres: r.nombres, apellidos: r.apellidos ?? '', celular: r.celular ?? '', correo: r.correo ?? '', cargo: r.cargo ?? '', fechaExpedicionDocumento: r.fechaExpedicionDocumento ?? '' });
                            setEditRepId(r.idRepresentante); setShowFormRep(true);
                          }}>Editar</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {/* Tab: Establecimientos */}
        {tab === 'establecimientos' && (
          <div>
            <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom:'1rem' }}>
              <h3 style={{ margin:0 }}>Sedes / Establecimientos</h3>
              {btn('+ Agregar', () => { setFormEst(emptyEst()); setEditEstId(null); setShowFormEst(true); }, 'secondary')}
            </div>
            {showFormEst && (
              <div style={{ ...formStyle, marginBottom:'1.5rem', background:'#f7fafc', padding:'1rem', borderRadius:'8px', border:'1px solid #e2e8f0' }}>
                {inp('Nombre establecimiento *', formEst.nombreEstablecimiento, v => setFormEst(p=>({...p,nombreEstablecimiento:v})), 'text', true)}
                {inp('Dirección', formEst.direccion, v => setFormEst(p=>({...p,direccion:v})))}
                {inp('Ciudad', formEst.ciudad, v => setFormEst(p=>({...p,ciudad:v})))}
                {inp('Teléfono', formEst.telefono, v => setFormEst(p=>({...p,telefono:v})))}
                {inp('Responsable', formEst.responsable, v => setFormEst(p=>({...p,responsable:v})))}
                <div style={{ display:'flex', gap:'0.75rem' }}>
                  {btn(savingSub ? 'Guardando...' : 'Guardar', guardarEst, 'primary', savingSub)}
                  {btn('Cancelar', () => { setShowFormEst(false); setFormEst(emptyEst()); }, 'secondary')}
                </div>
              </div>
            )}
            {establecimientos.length === 0 ? <p style={{ color:'#718096' }}>Sin establecimientos registrados.</p> : (
              <div style={{ overflowX:'auto' }}>
                <table style={{ width:'100%', borderCollapse:'collapse', fontSize:'0.82rem' }}>
                  <thead><tr>{['Nombre','Dirección','Ciudad','Responsable','Estado',''].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr></thead>
                  <tbody>
                    {establecimientos.map(e => (
                      <tr key={e.idEstablecimiento}>
                        <td style={{ ...tdStyle, fontWeight:600 }}>{e.nombreEstablecimiento}</td>
                        <td style={tdStyle}>{e.direccion ?? '—'}</td>
                        <td style={tdStyle}>{e.ciudad ?? '—'}</td>
                        <td style={tdStyle}>{e.responsable ?? '—'}</td>
                        <td style={tdStyle}>{estadoBadge(e.estado)}</td>
                        <td style={tdStyle}>
                          <button className="btn-link" style={{ fontSize:'0.8rem' }} onClick={() => {
                            setFormEst({ nombreEstablecimiento: e.nombreEstablecimiento, direccion: e.direccion ?? '', ciudad: e.ciudad ?? '', telefono: e.telefono ?? '', responsable: e.responsable ?? '' });
                            setEditEstId(e.idEstablecimiento); setShowFormEst(true);
                          }}>Editar</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {/* Tab: Usuarios solicitados */}
        {tab === 'usuarios' && (
          <div>
            <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom:'1rem' }}>
              <h3 style={{ margin:0 }}>Usuarios solicitados</h3>
              {btn('+ Agregar', () => { setFormUsr(emptyUsr()); setEditUsrId(null); setShowFormUsr(true); }, 'secondary')}
            </div>
            <p style={{ fontSize:'0.82rem', color:'#718096', marginTop:0 }}>
              Solo registro. No se crean contraseñas ni credenciales en esta fase.
            </p>
            {showFormUsr && (
              <div style={{ ...formStyle, marginBottom:'1.5rem', background:'#f7fafc', padding:'1rem', borderRadius:'8px', border:'1px solid #e2e8f0' }}>
                {inp('Nombres *', formUsr.nombres, v => setFormUsr(p=>({...p,nombres:v})), 'text', true)}
                {inp('Correo', formUsr.correo, v => setFormUsr(p=>({...p,correo:v})), 'email')}
                {inp('Celular', formUsr.celular, v => setFormUsr(p=>({...p,celular:v})))}
                {sel('Rol solicitado', formUsr.rolSolicitado, v => setFormUsr(p=>({...p,rolSolicitado:v})), ROLES_SOL)}
                <div style={{ display:'flex', gap:'0.75rem' }}>
                  {btn(savingSub ? 'Guardando...' : 'Guardar', guardarUsr, 'primary', savingSub)}
                  {btn('Cancelar', () => { setShowFormUsr(false); setFormUsr(emptyUsr()); }, 'secondary')}
                </div>
              </div>
            )}
            {usuariosSolicitados.length === 0 ? <p style={{ color:'#718096' }}>Sin usuarios solicitados.</p> : (
              <div style={{ overflowX:'auto' }}>
                <table style={{ width:'100%', borderCollapse:'collapse', fontSize:'0.82rem' }}>
                  <thead><tr>{['Nombres','Correo','Rol','Estado',''].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr></thead>
                  <tbody>
                    {usuariosSolicitados.map(u => (
                      <tr key={u.idUsuarioSolicitado}>
                        <td style={tdStyle}>{u.nombres}</td>
                        <td style={tdStyle}>{u.correo ?? '—'}</td>
                        <td style={tdStyle}><span className="badge badge-info">{u.rolSolicitado}</span></td>
                        <td style={tdStyle}>{estadoBadge(u.estado)}</td>
                        <td style={tdStyle}>
                          <button className="btn-link" style={{ fontSize:'0.8rem' }} onClick={() => {
                            setFormUsr({ nombres: u.nombres, correo: u.correo ?? '', celular: u.celular ?? '', rolSolicitado: u.rolSolicitado });
                            setEditUsrId(u.idUsuarioSolicitado); setShowFormUsr(true);
                          }}>Editar</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {/* Tab: Documentos */}
        {tab === 'documentos' && (
          <div>
            <h3 style={{ margin:'0 0 1rem' }}>Documentos</h3>

            {/* Completitud */}
            {compleitud && (
              <div style={{ background:'#f7fafc', border:'1px solid #e2e8f0', borderRadius:'8px', padding:'1rem 1.25rem', marginBottom:'1.5rem' }}>
                <div style={{ fontWeight:600, marginBottom:'0.5rem', fontSize:'0.87rem' }}>
                  Completitud documental: {compleitud.totalCargados}/{compleitud.totalRequeridos}
                </div>
                <div style={{ display:'grid', gridTemplateColumns:'repeat(auto-fill,minmax(200px,1fr))', gap:'0.4rem' }}>
                  {([
                    ['Contrato', compleitud.contrato],
                    ['Cámara de Comercio', compleitud.camaraComercio],
                    ['RUT', compleitud.rut],
                    ['Doc. Representante', compleitud.documentoRepresentante],
                    ['Formulario solicitud', compleitud.formularioSolicitud],
                  ] as [string, boolean][]).map(([label, ok]) => (
                    <div key={label} style={{ fontSize:'0.82rem', display:'flex', alignItems:'center', gap:'0.4rem' }}>
                      <span style={{ color: ok ? '#38a169' : '#e53e3e', fontWeight:700 }}>{ok ? '✓' : '✗'}</span>
                      {label}: <span className={`badge ${ok ? 'badge-ok' : 'badge-error'}`}>{ok ? 'Cargado' : 'Pendiente'}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Upload form */}
            <div style={{ background:'#f7fafc', border:'1px solid #e2e8f0', borderRadius:'8px', padding:'1rem 1.25rem', marginBottom:'1.5rem' }}>
              <h4 style={{ margin:'0 0 0.75rem' }}>Subir documento</h4>
              <div style={{ display:'grid', gap:'0.75rem', maxWidth:460 }}>
                {sel('Tipo de documento', tipoDocUpload, setTipoDocUpload, TIPOS_DOC_ARCH)}
                <label style={{ display:'flex', flexDirection:'column', gap:'0.25rem', fontSize:'0.85rem', fontWeight:600 }}>
                  Archivo (PDF, JPG, PNG — máx. 5 MB)
                  <input ref={fileRef} type="file" accept=".pdf,.jpg,.jpeg,.png"
                    onChange={e => setUploadFile(e.target.files?.[0] ?? null)}
                    style={{ border:'1px solid #cbd5e0', borderRadius:'0.375rem', padding:'0.3rem 0.6rem', fontSize:'0.85rem', fontWeight:400 }} />
                </label>
                {inp('Observaciones (opcional)', obsDocUpload, setObsDocUpload)}
                {btn(uploading ? 'Subiendo...' : 'Subir documento', subirDocumento, 'primary', uploading || !uploadFile)}
              </div>
            </div>

            {/* Lista documentos */}
            {documentos.length === 0 ? <p style={{ color:'#718096' }}>Sin documentos cargados.</p> : (
              <div style={{ overflowX:'auto' }}>
                <table style={{ width:'100%', borderCollapse:'collapse', fontSize:'0.82rem' }}>
                  <thead><tr>{['Tipo','Archivo','Tamaño','Fecha','Estado','Acciones'].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr></thead>
                  <tbody>
                    {documentos.map(d => (
                      <tr key={d.idDocumento}>
                        <td style={tdStyle}><span className="badge badge-info">{d.tipoDocumento}</span></td>
                        <td style={tdStyle}>{d.nombreArchivoOriginal}</td>
                        <td style={tdStyle} className="mono">{fmtBytes(d.sizeBytes)}</td>
                        <td style={tdStyle} className="mono">{fmtDate(d.uploadedAt)}</td>
                        <td style={tdStyle}>{estadoBadge(d.estado)}</td>
                        <td style={tdStyle}>
                          <div style={{ display:'flex', gap:'0.4rem' }}>
                            <button className="btn-link" style={{ fontSize:'0.8rem' }} onClick={() => descargarDocumento(d)}>Descargar</button>
                            <button className="btn-link" style={{ fontSize:'0.8rem', color:'#e53e3e' }}
                              onClick={() => { if (confirm('¿Eliminar documento?')) eliminarDocumento(d.idDocumento); }}>
                              Eliminar
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {/* Tab: Convenio */}
        {tab === 'convenio' && (
          <div style={{ display:'grid', gridTemplateColumns:'repeat(auto-fill,minmax(220px,1fr))', gap:'0.5rem 1.5rem', background:'#f7fafc', border:'1px solid #e2e8f0', borderRadius:'8px', padding:'1rem 1.25rem' }}>
            {[
              ['Fecha solicitud', selected.fechaSolicitud ? fmtDate(selected.fechaSolicitud) : '—'],
              ['Fecha aprobación', selected.fechaAprobacion ? fmtDate(selected.fechaAprobacion) : '—'],
              ['Inicio convenio', selected.fechaInicioConvenio ?? '—'],
              ['Fin convenio', selected.fechaFinConvenio ?? '—'],
            ].map(([k, v]) => (
              <div key={k}><span style={{ fontSize:'0.75rem', color:'#718096' }}>{k}</span><div className="mono" style={{ fontSize:'0.87rem' }}>{v}</div></div>
            ))}
            {selected.condicionesComerciales && (
              <div style={{ gridColumn:'1/-1' }}>
                <span style={{ fontSize:'0.75rem', color:'#718096' }}>Condiciones comerciales</span>
                <div style={{ fontSize:'0.87rem', whiteSpace:'pre-line' }}>{selected.condicionesComerciales}</div>
              </div>
            )}
          </div>
        )}
      </div>
    );
  }

  return null;
}
