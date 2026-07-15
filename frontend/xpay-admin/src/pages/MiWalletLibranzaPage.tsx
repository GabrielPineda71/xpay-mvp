import { useState, useEffect } from 'react';
import { get, post } from '../api/client.ts';

interface CorteVigente {
  diaPago: number;
  periodoInicio: number;
  periodoFin: number;
  fechaPago: string;
  valorPagoProgramado: number;
  porcentajeCupo: number;
  cupoBase: number;
  cupoUsado: number;
  cupoDisponible: number;
  fechaSimulada: string;
  esDiaPago: boolean;
}

interface AnticipoResponse {
  idAnticipo: number;
  idConvenio: number;
  idEmpleado: number;
  fechaSimulada?: string;
  diaPagoCorte: number;
  fechaPagoProgramada?: string;
  valorPagoProgramado: number;
  porcentajeCupo: number;
  valorCupoBase: number;
  valorSolicitado: number;
  valorComision: number;
  valorIva: number;
  valorTotalACobrar: number;
  valorNetoDesembolsado: number;
  momentoCobroComision: string;
  estado: string;
  referenciaPago?: string;
  fechaSolicitud: string;
  updatedAt?: string;
}

interface MiCupo {
  idConvenio: number;
  nombreEmpresa: string;
  idEmpleado: number;
  nombresEmpleado: string;
  numeroDocumento: string;
  periodicidadPago: string;
  corteVigente?: CorteVigente;
  anticiposActivos: AnticipoResponse[];
  historialAnticipos: AnticipoResponse[];
}

type ApiOk<T> = { success: boolean; data: T };

function fmt(n: number) {
  return n.toLocaleString('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
}

function estadoBadge(e: string) {
  const cls = e === 'DESEMBOLSADO' ? 'badge-warn'
            : e === 'PAGADO'       ? 'badge-ok'
            : e === 'ANULADO' || e === 'RECHAZADO' ? 'badge-error'
            : 'badge-info';
  return <span className={`badge ${cls}`}>{e}</span>;
}

export function MiWalletLibranzaPage() {
  const [fecha, setFecha]             = useState('');
  const [cupo, setCupo]               = useState<MiCupo | null>(null);
  const [loading, setLoading]         = useState(false);
  const [error, setError]             = useState<string | null>(null);
  const [valor, setValor]             = useState('');
  const [solicitando, setSolicitando] = useState(false);
  const [solicErr, setSolicErr]       = useState<string | null>(null);
  const [solicOk, setSolicOk]         = useState<AnticipoResponse | null>(null);
  const [tab, setTab]                 = useState<'cupo' | 'historial'>('cupo');

  useEffect(() => { loadCupo(); }, []);

  async function loadCupo(f?: string) {
    setLoading(true); setError(null); setSolicOk(null);
    try {
      const url = `/api/libranza/cliente/mi-cupo${f ? `?fecha=${f}` : ''}`;
      const r = await get<ApiOk<MiCupo>>(url);
      setCupo(r.data);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Error cargando cupo.');
    } finally { setLoading(false); }
  }

  async function solicitarAnticipo(e: React.FormEvent) {
    e.preventDefault();
    const v = Number(valor);
    if (!Number.isFinite(v) || v <= 0) { setSolicErr('Ingresa un valor numérico.'); return; }
    if (cupo?.corteVigente && v > cupo.corteVigente.cupoDisponible) {
      setSolicErr(`El valor supera tu cupo disponible (${fmt(cupo.corteVigente.cupoDisponible)}).`);
      return;
    }
    setSolicitando(true); setSolicErr(null); setSolicOk(null);
    try {
      const r = await post<ApiOk<AnticipoResponse>>('/api/libranza/cliente/anticipos', {
        valorSolicitado: v,
        ...(fecha ? { fechaSimulada: fecha } : {}),
      });
      setSolicOk(r.data);
      setValor('');
      await loadCupo(fecha || undefined);
    } catch (err: unknown) {
      setSolicErr(err instanceof Error ? err.message : 'Error solicitando anticipo.');
    } finally { setSolicitando(false); }
  }

  return (
    <div className="page">
      <h2>Anticipo de Nómina</h2>

      {/* Simulated date selector */}
      <div style={{ marginBottom: '1rem', padding: '0.75rem', background: '#fefce8', border: '1px solid #fde68a', borderRadius: '0.5rem', display: 'flex', gap: '0.75rem', alignItems: 'center', flexWrap: 'wrap' }}>
        <span style={{ fontSize: '0.8rem', fontWeight: 600, color: '#92400e' }}>Fecha simulada QA:</span>
        <input type="date" value={fecha} onChange={e => setFecha(e.target.value)}
          style={{ padding: '0.3rem 0.6rem', border: '1px solid #fcd34d', borderRadius: '0.375rem', fontSize: '0.85rem' }} />
        <button onClick={() => loadCupo(fecha || undefined)}
          style={{ padding: '0.3rem 0.75rem', background: '#d97706', color: '#fff', border: 'none', borderRadius: '0.375rem', cursor: 'pointer', fontSize: '0.82rem', fontWeight: 600 }}>
          Cargar
        </button>
        {fecha && <span style={{ fontSize: '0.78rem', color: '#92400e' }}>Simulando: {fecha}</span>}
      </div>

      {loading && <p>Cargando...</p>}
      {error && <div className="msg-error">{error}</div>}

      {cupo && (
        <>
          {/* Tabs */}
          <div style={{ display: 'flex', gap: '0.5rem', borderBottom: '2px solid #e2e8f0', marginBottom: '0' }}>
            {(['cupo', 'historial'] as const).map(t => (
              <button key={t} onClick={() => setTab(t)}
                style={{
                  padding: '0.5rem 1rem', border: 'none', background: 'none',
                  borderBottom: tab === t ? '2px solid #3b82f6' : '2px solid transparent',
                  color: tab === t ? '#3b82f6' : '#718096',
                  fontWeight: tab === t ? 600 : 400,
                  cursor: 'pointer', fontSize: '0.9rem', marginBottom: '-2px'
                }}>
                {t === 'cupo' ? 'Mi cupo' : `Historial (${cupo.historialAnticipos.length + cupo.anticiposActivos.length})`}
              </button>
            ))}
          </div>

          {tab === 'cupo' && (
            <div style={{ marginTop: '1rem' }}>
              {/* Profile */}
              <div className="info-section">
                <h3>Perfil</h3>
                <div className="info-grid">
                  <div className="info-item"><span className="label">Empresa</span><span className="value">{cupo.nombreEmpresa}</span></div>
                  <div className="info-item"><span className="label">Empleado</span><span className="value">{cupo.nombresEmpleado}</span></div>
                  <div className="info-item"><span className="label">Documento</span><span className="value">{cupo.numeroDocumento}</span></div>
                  <div className="info-item"><span className="label">Periodicidad</span><span className="value">{cupo.periodicidadPago}</span></div>
                </div>
              </div>

              {/* Corte vigente */}
              {cupo.corteVigente ? (
                <div className="info-section" style={{ marginTop: '1rem', borderLeft: '3px solid #3b82f6', background: '#eff6ff' }}>
                  <h3 style={{ color: '#1e40af' }}>Corte vigente — Día {cupo.corteVigente.diaPago}</h3>
                  <div className="info-grid">
                    <div className="info-item">
                      <span className="label">Período</span>
                      <span className="value">Días {cupo.corteVigente.periodoInicio}–{cupo.corteVigente.periodoFin}</span>
                    </div>
                    <div className="info-item">
                      <span className="label">Fecha de pago</span>
                      <span className="value">{cupo.corteVigente.fechaPago}</span>
                    </div>
                    <div className="info-item">
                      <span className="label">Pago programado</span>
                      <span className="value">{fmt(cupo.corteVigente.valorPagoProgramado)}</span>
                    </div>
                    <div className="info-item">
                      <span className="label">% Cupo</span>
                      <span className="value">{cupo.corteVigente.porcentajeCupo}%</span>
                    </div>
                    <div className="info-item">
                      <span className="label">Cupo base</span>
                      <span className="value" style={{ fontWeight: 600 }}>{fmt(cupo.corteVigente.cupoBase)}</span>
                    </div>
                    <div className="info-item">
                      <span className="label">Cupo usado</span>
                      <span className="value" style={{ color: '#dc2626' }}>{fmt(cupo.corteVigente.cupoUsado)}</span>
                    </div>
                    <div className="info-item">
                      <span className="label">Cupo disponible</span>
                      <span className="value" style={{ fontWeight: 700, fontSize: '1.05rem', color: '#166534' }}>{fmt(cupo.corteVigente.cupoDisponible)}</span>
                    </div>
                    {cupo.corteVigente.esDiaPago && (
                      <div className="info-item" style={{ gridColumn: 'span 2' }}>
                        <span className="badge badge-warn">Hoy es día de pago</span>
                      </div>
                    )}
                  </div>

                  {/* Solicitar anticipo */}
                  {cupo.corteVigente.cupoDisponible > 0 && (
                    <div style={{ marginTop: '1rem', padding: '0.75rem', background: '#fff', borderRadius: '0.5rem', border: '1px solid #bfdbfe' }}>
                      <h4 style={{ margin: '0 0 0.75rem', color: '#1e40af' }}>Solicitar anticipo</h4>
                      <form onSubmit={solicitarAnticipo} style={{ display: 'flex', gap: '0.5rem', alignItems: 'flex-end', flexWrap: 'wrap' }}>
                        <div>
                          <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 500, marginBottom: '0.2rem' }}>
                            Valor (máx. {fmt(cupo.corteVigente.cupoDisponible)})
                          </label>
                          <input
                            type="number" inputMode="numeric" min="1000" step="1000"
                            value={valor}
                            onChange={e => setValor(e.target.value)}
                            placeholder="100000"
                            style={{ padding: '0.35rem 0.6rem', border: '1px solid #93c5fd', borderRadius: '0.375rem', fontSize: '0.9rem', width: '180px' }}
                          />
                        </div>
                        <button type="submit" disabled={solicitando}
                          style={{ padding: '0.4rem 1rem', background: '#1e40af', color: '#fff', border: 'none', borderRadius: '0.375rem', cursor: 'pointer', fontWeight: 600, opacity: solicitando ? 0.7 : 1 }}>
                          {solicitando ? 'Solicitando...' : 'Solicitar anticipo'}
                        </button>
                      </form>
                      {solicErr && <div className="msg-error" style={{ marginTop: '0.5rem' }}>{solicErr}</div>}
                      {solicOk && (
                        <div style={{ marginTop: '0.75rem', padding: '0.75rem', background: '#f0fdf4', border: '1px solid #86efac', borderRadius: '0.5rem', fontSize: '0.85rem', color: '#166534' }}>
                          <strong>Anticipo #{solicOk.idAnticipo} desembolsado</strong><br />
                          Valor neto en wallet: {fmt(solicOk.valorNetoDesembolsado)}<br />
                          Comisión: {fmt(solicOk.valorComision)} + IVA: {fmt(solicOk.valorIva)}<br />
                          Total a cobrar el {solicOk.fechaPagoProgramada}: <strong>{fmt(solicOk.valorTotalACobrar)}</strong>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              ) : (
                <div className="info-section" style={{ marginTop: '1rem', borderLeft: '3px solid #f59e0b', background: '#fffbeb' }}>
                  <p style={{ color: '#92400e', fontSize: '0.9rem' }}>
                    No hay corte de pago vigente para la fecha simulada. Los días 30–31 en periodicidad DECADAL [10/20/30]
                    quedan fuera del período habilitado. Cambia la fecha simulada.
                  </p>
                </div>
              )}

              {/* Anticipos del período */}
              <div className="info-section" style={{ marginTop: '1rem' }}>
                <h3>Anticipos del período vigente</h3>
                {cupo.anticiposActivos.length === 0 ? (
                  <p style={{ color: '#718096', fontSize: '0.9rem' }}>
                    No hay anticipos activos para este período.
                    {cupo.corteVigente && cupo.corteVigente.cupoDisponible > 0 &&
                      ' Puedes solicitar hasta ' + fmt(cupo.corteVigente.cupoDisponible) + '.'}
                  </p>
                ) : (
                  <>
                    <p style={{ fontSize: '0.82rem', color: '#4a5568', marginBottom: '0.5rem' }}>
                      Múltiples anticipos permitidos — el único límite es el cupo disponible del período.
                    </p>
                    <AnticipasTable rows={cupo.anticiposActivos} />
                  </>
                )}
                {cupo.corteVigente && cupo.corteVigente.cupoDisponible <= 0 && (
                  <div style={{ marginTop: '0.5rem', padding: '0.5rem 0.75rem', background: '#fff5f5',
                    border: '1px solid #fc8181', borderRadius: '0.5rem', fontSize: '0.85rem', color: '#c53030', fontWeight: 500 }}>
                    Ya usaste el cupo disponible para este período.
                  </div>
                )}
              </div>
            </div>
          )}

          {tab === 'historial' && (
            <div className="info-section" style={{ marginTop: '1rem' }}>
              <h3>Historial de anticipos</h3>
              {cupo.anticiposActivos.length === 0 && cupo.historialAnticipos.length === 0
                ? <p style={{ color: '#718096', fontSize: '0.9rem' }}>No tiene anticipos registrados.</p>
                : <AnticipasTable rows={[...cupo.anticiposActivos, ...cupo.historialAnticipos]} />}
            </div>
          )}
        </>
      )}

      <p className="user-wallet-footer">Ambiente QA/Demo · anticipo nómina · sin transacciones reales en producción</p>
    </div>
  );
}

function AnticipasTable({ rows }: { rows: AnticipoResponse[] }) {
  if (rows.length === 0) return <p style={{ color: '#718096', fontSize: '0.9rem' }}>Sin registros.</p>;

  function fmt(n: number) {
    return n.toLocaleString('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
  }

  function estadoBadge(e: string) {
    const cls = e === 'DESEMBOLSADO' ? 'badge-warn'
              : e === 'PAGADO'       ? 'badge-ok'
              : e === 'ANULADO' || e === 'RECHAZADO' ? 'badge-error'
              : 'badge-info';
    return <span className={`badge ${cls}`}>{e}</span>;
  }

  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.82rem' }}>
        <thead><tr>{['#', 'Solicitud', 'Fecha sim.', 'Día corte', 'Fecha pago', 'Solicitado', 'Neto desemb.', 'Comisión', 'IVA', 'Total cobro', 'Estado'].map(h =>
          <th key={h} style={{ textAlign: 'left', padding: '0.4rem 0.5rem', color: '#718096', borderBottom: '1px solid #e2e8f0', fontWeight: 600 }}>{h}</th>
        )}</tr></thead>
        <tbody>
          {rows.map(a => (
            <tr key={a.idAnticipo}>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>#{a.idAnticipo}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{new Date(a.fechaSolicitud).toLocaleDateString('es-CO')}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{a.fechaSimulada ?? '—'}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0', textAlign: 'center' }}>{a.diaPagoCorte}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{a.fechaPagoProgramada ?? '—'}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{fmt(a.valorSolicitado)}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0', color: '#166534' }}>{fmt(a.valorNetoDesembolsado)}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{fmt(a.valorComision)}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{fmt(a.valorIva)}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0', fontWeight: 700, color: '#1e40af' }}>{fmt(a.valorTotalACobrar)}</td>
              <td style={{ padding: '0.4rem 0.5rem', borderBottom: '1px solid #f0f0f0' }}>{estadoBadge(a.estado)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
