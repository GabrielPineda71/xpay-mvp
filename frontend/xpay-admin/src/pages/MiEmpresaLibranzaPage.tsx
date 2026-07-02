import { useState, useEffect, useRef } from 'react';
import { get, postForm } from '../api/client.ts';
import { API_BASE_URL } from '../api/client.ts';

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
  fechaIngreso?: string;
  estado: string;
  cupoPreliminar: number;
  origenCarga: string;
  createdAt: string;
}

interface ErrorFila {
  fila: number;
  campo: string;
  mensaje: string;
}

interface ImportResult {
  totalFilas: number;
  filasValidas: number;
  filasError: number;
  empleadosCreados: number;
  empleadosActualizados: number;
  loteImportacion: string;
  errores: ErrorFila[];
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
  const [convenio, setConvenio]           = useState<MiConvenio | null>(null);
  const [empleados, setEmpleados]         = useState<Empleado[]>([]);
  const [loadingConv, setLoadingConv]     = useState(true);
  const [loadingEmp, setLoadingEmp]       = useState(false);
  const [error, setError]                 = useState<string | null>(null);
  const [tab, setTab]                     = useState<'empleados' | 'importar'>('empleados');
  const [importResult, setImportResult]   = useState<ImportResult | null>(null);
  const [importing, setImporting]         = useState(false);
  const [importErr, setImportErr]         = useState<string | null>(null);
  const fileRef                           = useRef<HTMLInputElement>(null);

  useEffect(() => {
    loadConvenio();
  }, []);

  useEffect(() => {
    if (convenio) loadEmpleados();
  }, [convenio]);

  async function loadConvenio() {
    setLoadingConv(true);
    setError(null);
    try {
      const r = await get<ApiOk<MiConvenio>>('/api/libranza/empresa/mi-convenio');
      setConvenio(r.data);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Error cargando convenio.');
    } finally {
      setLoadingConv(false);
    }
  }

  async function loadEmpleados() {
    setLoadingEmp(true);
    try {
      const r = await get<ApiOk<Empleado[]>>('/api/libranza/empresa/empleados');
      setEmpleados(r.data);
    } catch {
      // Silent — show empty list
    } finally {
      setLoadingEmp(false);
    }
  }

  function downloadPlantilla() {
    const token = localStorage.getItem('xpay_token');
    const url   = `${API_BASE_URL}/api/libranza/empresa/empleados/plantilla`;
    const a     = document.createElement('a');
    a.href      = url;
    a.download  = 'plantilla_empleados_libranza.csv';
    if (token) {
      fetch(url, { headers: { Authorization: `Bearer ${token}` } })
        .then(r => r.blob())
        .then(blob => {
          const blobUrl = URL.createObjectURL(blob);
          a.href = blobUrl;
          a.click();
          URL.revokeObjectURL(blobUrl);
        });
    } else {
      a.click();
    }
  }

  async function handleImport(e: React.FormEvent) {
    e.preventDefault();
    const file = fileRef.current?.files?.[0];
    if (!file) { setImportErr('Selecciona un archivo CSV.'); return; }

    setImporting(true);
    setImportErr(null);
    setImportResult(null);

    try {
      const form = new FormData();
      form.append('archivo', file);
      const r = await postForm<ApiOk<ImportResult>>('/api/libranza/empresa/empleados/importar', form);
      setImportResult(r.data);
      if (fileRef.current) fileRef.current.value = '';
      await loadEmpleados();
    } catch (err: unknown) {
      setImportErr(err instanceof Error ? err.message : 'Error importando archivo.');
    } finally {
      setImporting(false);
    }
  }

  if (loadingConv) return <div className="page"><p>Cargando convenio...</p></div>;

  if (error) return (
    <div className="page">
      <h2>Mi Empresa — Libranza</h2>
      <div className="msg-error">{error}</div>
    </div>
  );

  if (!convenio) return (
    <div className="page">
      <h2>Mi Empresa — Libranza</h2>
      <p>No tienes un convenio activo asignado. Contacta al administrador XPAY.</p>
    </div>
  );

  return (
    <div className="page">
      <h2>Mi Empresa — Libranza</h2>
      <p className="dashboard-subtitle">
        {convenio.nombreEmpresa}
        {' · '}<span className="badge badge-info">NIT {convenio.nit}</span>
        {' · '}{estadoBadge(convenio.estado)}
        {' · '}<span style={{ fontSize: '0.8rem', color: '#718096' }}>Rol: {convenio.rolEmpresa}</span>
      </p>

      {/* Convenio info */}
      <div className="info-section">
        <h3>Convenio</h3>
        <div className="info-grid">
          <div className="info-item"><span className="label">Periodicidad</span><span className="value">{convenio.periodicidadPago}</span></div>
          <div className="info-item"><span className="label">Días pago</span><span className="value">{[convenio.diaPago1, convenio.diaPago2].filter(Boolean).join(' / ') || '—'}</span></div>
          <div className="info-item"><span className="label">% Máx. cupo</span><span className="value">{convenio.porcentajeMaximoCupo}%</span></div>
          <div className="info-item"><span className="label">Empleados activos</span><span className="value">{convenio.empleadosActivos} / {convenio.totalEmpleados}</span></div>
          {convenio.emailContacto && <div className="info-item"><span className="label">Email contacto</span><span className="value">{convenio.emailContacto}</span></div>}
          {convenio.representanteLegal && <div className="info-item"><span className="label">Representante</span><span className="value">{convenio.representanteLegal}</span></div>}
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: '0.5rem', marginTop: '1.25rem', borderBottom: '2px solid #e2e8f0', paddingBottom: '0' }}>
        {(['empleados', 'importar'] as const).map(t => (
          <button key={t} onClick={() => setTab(t)}
            style={{
              padding: '0.5rem 1rem', border: 'none', background: 'none',
              borderBottom: tab === t ? '2px solid #3b82f6' : '2px solid transparent',
              color: tab === t ? '#3b82f6' : '#718096',
              fontWeight: tab === t ? 600 : 400,
              cursor: 'pointer', fontSize: '0.9rem', marginBottom: '-2px'
            }}>
            {t === 'empleados' ? `Empleados (${empleados.length})` : 'Importar CSV'}
          </button>
        ))}
      </div>

      {/* Tab: Empleados */}
      {tab === 'empleados' && (
        <div className="info-section" style={{ marginTop: '1rem' }}>
          {loadingEmp
            ? <p>Cargando empleados...</p>
            : empleados.length === 0
              ? <p style={{ color: '#718096', fontSize: '0.9rem' }}>No hay empleados registrados. Usa la pestaña "Importar CSV" para cargar el padrón.</p>
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
            El archivo debe seguir el formato de la plantilla. Máximo 500 filas, 2 MB.
            Si un empleado ya existe (por tipo+número de documento) sus datos serán actualizados.
            El cupo preliminar se calcula automáticamente como <strong>salario × {convenio.porcentajeMaximoCupo}%</strong>.
          </p>

          <button onClick={downloadPlantilla}
            style={{ marginBottom: '1rem', padding: '0.4rem 0.9rem', background: '#f0fdf4', border: '1px solid #86efac', borderRadius: '0.375rem', color: '#166534', cursor: 'pointer', fontSize: '0.85rem' }}>
            Descargar plantilla CSV
          </button>

          <form onSubmit={handleImport} style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem', maxWidth: '480px' }}>
            <div>
              <label style={{ display: 'block', fontSize: '0.85rem', fontWeight: 500, marginBottom: '0.3rem' }}>
                Archivo CSV
              </label>
              <input ref={fileRef} type="file" accept=".csv,.txt"
                style={{ fontSize: '0.85rem', width: '100%' }} />
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
                {importResult.filasError === 0 ? 'Importación completada' : 'Importación completada con errores'}
              </h4>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '0.5rem', fontSize: '0.85rem' }}>
                {[
                  ['Total filas', importResult.totalFilas],
                  ['Filas válidas', importResult.filasValidas],
                  ['Filas con error', importResult.filasError],
                  ['Empleados creados', importResult.empleadosCreados],
                  ['Actualizados', importResult.empleadosActualizados],
                ].map(([k, v]) => (
                  <div key={String(k)}>
                    <div style={{ color: '#718096', fontSize: '0.75rem' }}>{k}</div>
                    <div style={{ fontWeight: 700, fontSize: '1rem' }}>{v}</div>
                  </div>
                ))}
              </div>
              <div style={{ marginTop: '0.5rem', fontSize: '0.78rem', color: '#718096' }}>Lote: {importResult.loteImportacion}</div>
              {importResult.errores.length > 0 && (
                <div style={{ marginTop: '0.75rem' }}>
                  <strong style={{ fontSize: '0.85rem', color: '#92400e' }}>Errores:</strong>
                  <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.78rem', marginTop: '0.4rem' }}>
                    <thead>
                      <tr>{['Fila', 'Campo', 'Mensaje'].map(h => (
                        <th key={h} style={{ textAlign: 'left', padding: '0.3rem 0.4rem', background: '#fef9c3', borderBottom: '1px solid #fcd34d' }}>{h}</th>
                      ))}</tr>
                    </thead>
                    <tbody>
                      {importResult.errores.map((er, i) => (
                        <tr key={i}>
                          <td style={{ padding: '0.3rem 0.4rem', borderBottom: '1px solid #fef3c7' }}>{er.fila}</td>
                          <td style={{ padding: '0.3rem 0.4rem', borderBottom: '1px solid #fef3c7' }}>{er.campo}</td>
                          <td style={{ padding: '0.3rem 0.4rem', borderBottom: '1px solid #fef3c7' }}>{er.mensaje}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      <p className="user-wallet-footer">Ambiente QA/Demo · módulo libranza · sin transacciones financieras · sin producción</p>
    </div>
  );
}
