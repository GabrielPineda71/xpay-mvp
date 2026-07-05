import { useState, useEffect, useRef } from 'react';
import { get, post, postForm } from '../api/client.ts';
import { API_BASE_URL } from '../api/client.ts';

interface ConvenioItem {
  idConvenio: number;
  nombreEmpresa: string;
  periodicidadPago: string;
  estado: string;
  rolEmpresa: string;
}

interface MiConvenio {
  idConvenio: number;
  nombreEmpresa: string;
  nit: string;
  representanteLegal?: string;
  emailContacto?: string;
  telefonoContacto?: string;
  estado: string;
  periodicidadPago: string;
  diaPago1?: number;
  diaPago2?: number;
  diaPago3?: number;
  permiteAnticipodiaPago?: boolean;
  ivaPorcentaje?: number;
  momentoCobroComision?: string;
  porcentajeMaximoCupo: number;
  totalEmpleados: number;
  empleadosActivos: number;
  rolEmpresa: string;
}

interface Empleado {
  idEmpleado: number;
  tipoDocumento: string;
  numeroDocumento: string;
  nombres: string;
  apellidos?: string;
  celular?: string;
  correo?: string;
  cargo?: string;
  salarioMensual: number;
  periodicidadPago: string;
  diaPago1?: number;
  diaPago2?: number;
  diaPago3?: number;
  fechaIngreso?: string;
  estado: string;
  cupoPreliminar: number;
  origenCarga: string;
  createdAt: string;
}

interface ErrorFila { fila: number; campo: string; mensaje: string; }

interface ImportResult {
  totalFilas: number;
  filasValidas: number;
  filasError: number;
  empleadosCreados: number;
  empleadosActualizados: number;
  loteImportacion: string;
  errores: ErrorFila[];
}

interface CobroItem {
  idAnticipo: number;
  idEmpleado: number;
  nombresEmpleado: string;
  numeroDocumento: string;
  tipoDocumento: string;
  diaPagoCorte: number;
  fechaPagoProgramada?: string;
  valorSolicitado: number;
  valorComision: number;
  valorIva: number;
  valorTotalACobrar: number;
  momentoCobroComision: string;
  estado: string;
}

type ApiOk<T> = { success: boolean; data: T; total?: number };

function fmt(n: number) {
  return n.toLocaleString('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
}

function estadoBadge(e: string) {
  const cls = e === 'ACTIVO' ? 'badge-ok' : e === 'SUSPENDIDO' ? 'badge-warn' : 'badge-error';
  return <span className={`badge ${cls}`}>{e}</span>;
}

export function MiEmpresaLibranzaPage() {
  const [convenios, setConvenios]         = useState<ConvenioItem[]>([]);
  const [selConvenio, setSelConvenio]     = useState<number | null>(null);
  const [convenio, setConvenio]           = useState<MiConvenio | null>(null);
  const [empleados, setEmpleados]         = useState<Empleado[]>([]);
  const [loadingConv, setLoadingConv]     = useState(true);
  const [loadingEmp, setLoadingEmp]       = useState(false);
  const [error, setError]                 = useState<string | null>(null);
  const [tab, setTab]                     = useState<'empleados' | 'importar' | 'cobros'>('empleados');
  const [importResult, setImportResult]   = useState<ImportResult | null>(null);
  const [importing, setImporting]         = useState(false);
  const [importErr, setImportErr]         = useState<string | null>(null);
  const fileRef                           = useRef<HTMLInputElement>(null);

  // Cobros state
  const [fechaCobro, setFechaCobro]       = useState('');
  const [cobros, setCobros]               = useState<CobroItem[]>([]);
  const [loadingCobros, setLoadingCobros] = useState(false);
  const [cobroErr, setCobroErr]           = useState<string | null>(null);
  const [refPago, setRefPago]             = useState('');
  const [aplicando, setAplicando]         = useState(false);
  const [aplicarMsg, setAplicarMsg]       = useState<string | null>(null);
  const [aplicarOk, setAplicarOk]         = useState(false);

  useEffect(() => { loadConvenios(); }, []);

  useEffect(() => {
    if (selConvenio !== null) { loadConvenio(selConvenio); }
  }, [selConvenio]);

  useEffect(() => {
    if (convenio) loadEmpleados();
  }, [convenio]);

  async function loadConvenios() {
    setLoadingConv(true);
    setError(null);
    try {
      const r = await get<ApiOk<ConvenioItem[]>>('/api/libranza/empresa/mis-convenios');
      setConvenios(r.data);
      if (r.data.length > 0) setSelConvenio(r.data[0].idConvenio);
      else setLoadingConv(false);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Error cargando convenios.');
      setLoadingConv(false);
    }
  }

  async function loadConvenio(id: number) {
    setLoadingConv(true);
    setError(null);
    try {
      const r = await get<ApiOk<MiConvenio>>(`/api/libranza/empresa/mi-convenio?idConvenio=${id}`);
      setConvenio(r.data);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Error cargando convenio.');
    } finally {
      setLoadingConv(false);
    }
  }

  async function loadEmpleados() {
    if (!selConvenio) return;
    setLoadingEmp(true);
    try {
      const r = await get<ApiOk<Empleado[]>>(`/api/libranza/empresa/empleados?idConvenio=${selConvenio}`);
      setEmpleados(r.data);
    } catch { /* show empty */ } finally { setLoadingEmp(false); }
  }

  function downloadPlantilla() {
    const token = localStorage.getItem('xpay_token');
    const url   = `${API_BASE_URL}/api/libranza/empresa/empleados/plantilla`;
    if (token) {
      fetch(url, { headers: { Authorization: `Bearer ${token}` } })
        .then(r => r.blob())
        .then(blob => {
          const a = document.createElement('a');
          a.href = URL.createObjectURL(blob);
          a.download = 'plantilla_empleados_libranza.csv';
          a.click();
          URL.revokeObjectURL(a.href);
        });
    }
  }

  async function handleImport(e: React.FormEvent) {
    e.preventDefault();
    const file = fileRef.current?.files?.[0];
    if (!file) { setImportErr('Selecciona un archivo CSV.'); return; }
    setImporting(true); setImportErr(null); setImportResult(null);
    try {
      const form = new FormData();
      form.append('archivo', file);
      const url = `/api/libranza/empresa/empleados/importar${selConvenio ? `?idConvenio=${selConvenio}` : ''}`;
      const r = await postForm<ApiOk<ImportResult>>(url, form);
      setImportResult(r.data);
      if (fileRef.current) fileRef.current.value = '';
      await loadEmpleados();
    } catch (err: unknown) {
      setImportErr(err instanceof Error ? err.message : 'Error importando archivo.');
    } finally { setImporting(false); }
  }

  async function loadCobros() {
    if (!fechaCobro || !selConvenio) { setCobroErr('Ingresa la fecha de pago.'); return; }
    setLoadingCobros(true); setCobroErr(null); setCobros([]); setAplicarMsg(null);
    try {
      const r = await get<ApiOk<CobroItem[]>>(
        `/api/libranza/empresa/cobros?fechaPago=${fechaCobro}&idConvenio=${selConvenio}`);
      setCobros(r.data);
    } catch (err: unknown) {
      setCobroErr(err instanceof Error ? err.message : 'Error cargando cobros.');
    } finally { setLoadingCobros(false); }
  }

  async function aplicarPago() {
    if (!refPago.trim()) { setCobroErr('Ingresa la referencia de pago.'); return; }
    if (!selConvenio) return;
    setAplicando(true); setCobroErr(null); setAplicarMsg(null); setAplicarOk(false);
    try {
      const r = await post<ApiOk<{ anticiopsAplicados?: number; anticiposAplicados?: number; totalCobrado: number; referenciaPago: string }>>(
        `/api/libranza/empresa/cobros/aplicar?idConvenio=${selConvenio}`,
        { fechaPago: fechaCobro, referenciaPago: refPago.trim() }
      );
      const n = r.data.anticiposAplicados ?? 0;
      setAplicarMsg(`Pago aplicado. ${n} anticipos marcados como PAGADO. Total cobrado: ${fmt(r.data.totalCobrado)}. Ref: ${r.data.referenciaPago}`);
      setAplicarOk(true);
      await loadCobros();
    } catch (err: unknown) {
      setAplicarMsg(err instanceof Error ? err.message : 'Error aplicando pago.');
      setAplicarOk(false);
    } finally { setAplicando(false); }
  }

  if (loadingConv) return <div className="page"><p>Cargando convenio...</p></div>;
  if (error) return <div className="page"><h2>Mi Empresa — Libranza</h2><div className="msg-error">{error}</div></div>;
  if (!convenio) return <div className="page"><h2>Mi Empresa — Libranza</h2><p>No tienes un convenio activo asignado.</p></div>;

  const diasPago = [convenio.diaPago1, convenio.diaPago2, convenio.diaPago3].filter(Boolean).join(' / ') || '—';
  const tabs = ['empleados', 'importar', 'cobros'] as const;

  return (
    <div className="page">
      <h2>Mi Empresa — Libranza</h2>

      {/* Multi-convenio selector */}
      {convenios.length > 1 && (
        <div style={{ marginBottom: '0.75rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <label style={{ fontSize: '0.85rem', fontWeight: 500 }}>Convenio:</label>
          <select value={selConvenio ?? ''} onChange={e => setSelConvenio(Number(e.target.value))}
            style={{ padding: '0.3rem 0.6rem', borderRadius: '0.375rem', border: '1px solid #cbd5e0', fontSize: '0.85rem' }}>
            {convenios.map(c => (
              <option key={c.idConvenio} value={c.idConvenio}>
                {c.nombreEmpresa} ({c.periodicidadPago})
              </option>
            ))}
          </select>
        </div>
      )}

      <p className="dashboard-subtitle">
        {convenio.nombreEmpresa}
        {' · '}<span className="badge badge-info">NIT {convenio.nit}</span>
        {' · '}{estadoBadge(convenio.estado)}
        {' · '}<span style={{ fontSize: '0.8rem', color: '#718096' }}>Rol: {convenio.rolEmpresa}</span>
      </p>

      <div className="info-section">
        <h3>Convenio</h3>
        <div className="info-grid">
          <div className="info-item"><span className="label">Periodicidad</span><span className="value">{convenio.periodicidadPago}</span></div>
          <div className="info-item"><span className="label">Días pago</span><span className="value">{diasPago}</span></div>
          <div className="info-item"><span className="label">% Máx. cupo</span><span className="value">{convenio.porcentajeMaximoCupo}%</span></div>
          <div className="info-item"><span className="label">IVA comisión</span><span className="value">{convenio.ivaPorcentaje ?? 0}%</span></div>
          <div className="info-item"><span className="label">Cobro comisión</span><span className="value">{convenio.momentoCobroComision ?? '—'}</span></div>
          <div className="info-item"><span className="label">Anticipo día pago</span><span className="value">{convenio.permiteAnticipodiaPago ? 'Sí' : 'No'}</span></div>
          <div className="info-item"><span className="label">Empleados activos</span><span className="value">{convenio.empleadosActivos} / {convenio.totalEmpleados}</span></div>
          {convenio.emailContacto && <div className="info-item"><span className="label">Email contacto</span><span className="value">{convenio.emailContacto}</span></div>}
          {convenio.representanteLegal && <div className="info-item"><span className="label">Representante</span><span className="value">{convenio.representanteLegal}</span></div>}
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: '0.5rem', marginTop: '1.25rem', borderBottom: '2px solid #e2e8f0', paddingBottom: '0' }}>
        {tabs.map(t => (
          <button key={t} onClick={() => setTab(t)}
            style={{
              padding: '0.5rem 1rem', border: 'none', background: 'none',
              borderBottom: tab === t ? '2px solid #3b82f6' : '2px solid transparent',
              color: tab === t ? '#3b82f6' : '#718096',
              fontWeight: tab === t ? 600 : 400,
              cursor: 'pointer', fontSize: '0.9rem', marginBottom: '-2px'
            }}>
            {t === 'empleados' ? `Empleados (${empleados.length})` : t === 'importar' ? 'Importar CSV' : 'Cobros'}
          </button>
        ))}
      </div>

      {/* Tab: Empleados */}
      {tab === 'empleados' && (
        <div className="info-section" style={{ marginTop: '1rem' }}>
          {loadingEmp
            ? <p>Cargando empleados...</p>
            : empleados.length === 0
              ? <p style={{ color: '#718096', fontSize: '0.9rem' }}>No hay empleados registrados. Usa "Importar CSV".</p>
              : (
                <div style={{ overflowX: 'auto' }}>
                  <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.82rem' }}>
                    <thead>
                      <tr>{['Doc', 'Nombres', 'Cargo', 'Salario', 'Cupo prelim.', 'Periodicidad', 'Estado', 'Origen'].map(h => (
                        <th key={h} style={{ textAlign: 'left', padding: '0.4rem 0.5rem', color: '#718096', borderBottom: '1px solid #e2e8f0', fontWeight: 600 }}>{h}</th>
                      ))}</tr>
                    </thead>
                    <tbody>
                      {empleados.map(em => (
                        <tr key={em.idEmpleado}>
                          <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{em.tipoDocumento} {em.numeroDocumento}</td>
                          <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{em.nombres}{em.apellidos ? ` ${em.apellidos}` : ''}</td>
                          <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{em.cargo || '—'}</td>
                          <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{fmt(em.salarioMensual)}</td>
                          <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0', fontWeight: 600, color: '#1e40af' }}>{fmt(em.cupoPreliminar)}</td>
                          <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{em.periodicidadPago}</td>
                          <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{estadoBadge(em.estado)}</td>
                          <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}><span className={`badge ${em.origenCarga === 'EXCEL' ? 'badge-info' : 'badge-warn'}`}>{em.origenCarga}</span></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
        </div>
      )}

      {/* Tab: Importar */}
      {tab === 'importar' && (
        <div className="info-section" style={{ marginTop: '1rem' }}>
          <h3>Importar empleados desde CSV</h3>
          <p style={{ fontSize: '0.87rem', color: '#718096', marginBottom: '1rem' }}>
            Máximo 500 filas, 2 MB. Para DECADAL incluir columnas <code>dia_pago_3</code>, <code>pago_corte_1/2/3</code>.
            Cupo = salario × {convenio.porcentajeMaximoCupo}%.
          </p>

          <button onClick={downloadPlantilla}
            style={{ marginBottom: '1rem', padding: '0.4rem 0.9rem', background: '#f0fdf4', border: '1px solid #86efac', borderRadius: '0.375rem', color: '#166534', cursor: 'pointer', fontSize: '0.85rem' }}>
            Descargar plantilla CSV
          </button>

          <form onSubmit={handleImport} style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem', maxWidth: '480px' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500, marginBottom: '0.3rem' }}>Archivo CSV</label>
              <input ref={fileRef} type="file" accept=".csv,.txt" style={{ fontSize: '0.85rem', width: '100%' }} />
            </div>
            {importErr && <div className="msg-error">{importErr}</div>}
            <button type="submit" disabled={importing}
              style={{ padding: '0.5rem 1.2rem', background: '#3b82f6', color: '#fff', border: 'none', borderRadius: '0.375rem', cursor: importing ? 'not-allowed' : 'pointer', fontWeight: 600, opacity: importing ? 0.7 : 1 }}>
              {importing ? 'Procesando...' : 'Importar'}
            </button>
          </form>

          {importResult && (
            <div style={{ marginTop: '1.25rem', padding: '1rem', background: importResult.filasError === 0 ? '#f0fdf4' : '#fffbeb', border: `1px solid ${importResult.filasError === 0 ? '#86efac' : '#fcd34d'}`, borderRadius: '0.5rem' }}>
              <h4 style={{ margin: '0 0 0.75rem', color: importResult.filasError === 0 ? '#166534' : '#92400e' }}>
                {importResult.filasError === 0 ? 'Importación completada' : 'Importación con errores'}
              </h4>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '0.5rem', fontSize: '0.85rem' }}>
                {([['Total filas', importResult.totalFilas], ['Filas válidas', importResult.filasValidas], ['Errores', importResult.filasError], ['Creados', importResult.empleadosCreados], ['Actualizados', importResult.empleadosActualizados]] as [string, number][]).map(([k, v]) => (
                  <div key={k}><div style={{ color: '#718096', fontSize: '0.75rem' }}>{k}</div><div style={{ fontWeight: 700 }}>{v}</div></div>
                ))}
              </div>
              <div style={{ marginTop: '0.5rem', fontSize: '0.78rem', color: '#718096' }}>Lote: {importResult.loteImportacion}</div>
              {importResult.errores.length > 0 && (
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.78rem', marginTop: '0.75rem' }}>
                  <thead><tr>{['Fila', 'Campo', 'Mensaje'].map(h => <th key={h} style={{ textAlign: 'left', padding: '0.3rem', background: '#fef9c3', borderBottom: '1px solid #fcd34d' }}>{h}</th>)}</tr></thead>
                  <tbody>{importResult.errores.map((er, i) => (
                    <tr key={i}><td style={{ padding: '0.3rem', borderBottom: '1px solid #fef3c7' }}>{er.fila}</td><td style={{ padding: '0.3rem', borderBottom: '1px solid #fef3c7' }}>{er.campo}</td><td style={{ padding: '0.3rem', borderBottom: '1px solid #fef3c7' }}>{er.mensaje}</td></tr>
                  ))}</tbody>
                </table>
              )}
            </div>
          )}
        </div>
      )}

      {/* Tab: Cobros */}
      {tab === 'cobros' && (
        <div className="info-section" style={{ marginTop: '1rem' }}>
          <h3>Cobros pendientes por fecha de pago</h3>
          <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end', flexWrap: 'wrap', marginBottom: '1rem' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 500, marginBottom: '0.2rem' }}>Fecha de pago (corte)</label>
              <input type="date" value={fechaCobro} onChange={e => setFechaCobro(e.target.value)}
                style={{ padding: '0.35rem 0.6rem', border: '1px solid #cbd5e0', borderRadius: '0.375rem', fontSize: '0.85rem' }} />
            </div>
            <button onClick={loadCobros} disabled={loadingCobros}
              style={{ padding: '0.4rem 0.9rem', background: '#3b82f6', color: '#fff', border: 'none', borderRadius: '0.375rem', cursor: 'pointer', fontWeight: 600, fontSize: '0.85rem' }}>
              {loadingCobros ? 'Cargando...' : 'Ver cobros'}
            </button>
          </div>

          {cobroErr && <div className="msg-error" style={{ marginBottom: '0.75rem' }}>{cobroErr}</div>}

          {cobros.length > 0 && (
            <>
              <div style={{ overflowX: 'auto', marginBottom: '1rem' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.82rem' }}>
                  <thead><tr>{['Anticipo', 'Empleado', 'Doc', 'Día corte', 'Fecha pago', 'Solicitado', 'Comisión', 'IVA', 'Total a cobrar', 'Estado'].map(h =>
                    <th key={h} style={{ textAlign: 'left', padding: '0.4rem 0.5rem', color: '#718096', borderBottom: '1px solid #e2e8f0', fontWeight: 600 }}>{h}</th>
                  )}</tr></thead>
                  <tbody>
                    {cobros.map(c => (
                      <tr key={c.idAnticipo}>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>#{c.idAnticipo}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{c.nombresEmpleado}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{c.tipoDocumento} {c.numeroDocumento}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0', textAlign: 'center' }}>{c.diaPagoCorte}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{c.fechaPagoProgramada ?? '—'}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{fmt(c.valorSolicitado)}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{fmt(c.valorComision)}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{fmt(c.valorIva)}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0', fontWeight: 700, color: '#1e40af' }}>{fmt(c.valorTotalACobrar)}</td>
                        <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}><span className="badge badge-warn">{c.estado}</span></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <div style={{ marginTop: '0.5rem', fontSize: '0.85rem', fontWeight: 600, color: '#1e40af' }}>
                  Total a cobrar: {fmt(cobros.reduce((s, c) => s + c.valorTotalACobrar, 0))} · {cobros.length} anticipo(s)
                </div>
              </div>

              <div style={{ padding: '1rem', background: '#fefce8', border: '1px solid #fde68a', borderRadius: '0.5rem' }}>
                <h4 style={{ margin: '0 0 0.75rem', color: '#92400e' }}>Aplicar pago QA (simulado)</h4>
                <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end', flexWrap: 'wrap' }}>
                  <div>
                    <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 500, marginBottom: '0.2rem' }}>Referencia de pago</label>
                    <input value={refPago} onChange={e => setRefPago(e.target.value)} placeholder="REF-2026-0710-001"
                      style={{ padding: '0.35rem 0.6rem', border: '1px solid #fcd34d', borderRadius: '0.375rem', fontSize: '0.85rem', width: '220px' }} />
                  </div>
                  <button onClick={aplicarPago} disabled={aplicando}
                    style={{ padding: '0.4rem 0.9rem', background: '#d97706', color: '#fff', border: 'none', borderRadius: '0.375rem', cursor: 'pointer', fontWeight: 600, fontSize: '0.85rem', opacity: aplicando ? 0.7 : 1 }}>
                    {aplicando ? 'Aplicando...' : 'Aplicar pago QA'}
                  </button>
                </div>
                {aplicarMsg && (
                  <div style={{ marginTop: '0.75rem', padding: '0.6rem', background: aplicarOk ? '#f0fdf4' : '#fef2f2', border: `1px solid ${aplicarOk ? '#86efac' : '#fca5a5'}`, borderRadius: '0.375rem', fontSize: '0.85rem', color: aplicarOk ? '#166534' : '#991b1b' }}>
                    {aplicarMsg}
                  </div>
                )}
              </div>
            </>
          )}

          {!loadingCobros && cobros.length === 0 && fechaCobro && !cobroErr && (
            <p style={{ color: '#718096', fontSize: '0.9rem' }}>No hay anticipos DESEMBOLSADOS para la fecha {fechaCobro}.</p>
          )}
        </div>
      )}

      <p className="user-wallet-footer">Ambiente QA/Demo · libranza activo · sin transacciones financieras reales</p>
    </div>
  );
}
