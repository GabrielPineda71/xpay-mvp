import { useEffect, useState } from 'react';
import { get, post } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

interface RetiroAdmin {
  idBrebRetiro:        number;
  tipoSujeto:          string;
  idUsuario:           number | null;
  idComercio:          number | null;
  idWallet:            number;
  valor:               number;
  moneda:              string;
  estado:              string;
  referenciaInterna:   string;
  keyValueMasked:      string;
  motivoRechazo:       string | null;
  idTransaccionLedger: number | null;
  fechaSolicitud:      string;
  fechaConfirmacion:   string | null;
  fechaLiquidacion:    string | null;
  fechaRechazo:        string | null;
}

interface ListaResp   { success: boolean; data: RetiroAdmin[]; }
interface AccionResp  { success: boolean; message?: string; }

function estadoBadge(estado: string) {
  const map: Record<string, string> = {
    CREADO:     'badge-info',
    CONFIRMADO: 'badge',
    LIQUIDADO:  'badge-ok',
    RECHAZADO:  'badge-warn',
  };
  const style: Record<string, React.CSSProperties> = {
    CONFIRMADO: { background: '#fefcbf', color: '#744210' },
  };
  return (
    <span className={`badge ${map[estado] ?? 'badge-info'}`} style={style[estado]}>
      {estado}
    </span>
  );
}

export function BrebRetirosAdminPage() {
  const [retiros,  setRetiros]  = useState<RetiroAdmin[]>([]);
  const [loading,  setLoading]  = useState(false);
  const [error,    setError]    = useState('');
  const [msg,      setMsg]      = useState('');
  const [acting,   setActing]   = useState<number | null>(null);

  function cargar() {
    setLoading(true);
    setError('');
    get<ListaResp>('/api/breb/admin/retiros')
      .then(r => setRetiros(r.data))
      .catch(err => { setRetiros([]); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }

  useEffect(cargar, []);

  async function ejecutar(
    idBrebRetiro: number,
    endpoint: string,
    body?: Record<string, string>,
  ) {
    setActing(idBrebRetiro);
    setMsg('');
    setError('');
    try {
      const r = await post<AccionResp>(endpoint, body ?? {});
      setMsg(r.message ?? 'Acción ejecutada.');
      cargar();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setActing(null);
    }
  }

  function confirmar(r: RetiroAdmin) {
    if (!window.confirm(`¿Confirmar retiro #${r.idBrebRetiro} por ${fmtMoney(r.valor)}?\n\nEsto descontará el saldo del wallet y creará asiento DR 210101 / CR 210204.`)) return;
    ejecutar(r.idBrebRetiro, `/api/breb/admin/retiros/${r.idBrebRetiro}/confirmar`);
  }

  function liquidar(r: RetiroAdmin) {
    if (!window.confirm(`¿Liquidar retiro #${r.idBrebRetiro} por ${fmtMoney(r.valor)}?\n\nEsto creará asiento DR 210204 / CR 110102.`)) return;
    ejecutar(r.idBrebRetiro, `/api/breb/admin/retiros/${r.idBrebRetiro}/liquidar`);
  }

  function rechazar(r: RetiroAdmin) {
    const motivo = window.prompt(`Motivo de rechazo para retiro #${r.idBrebRetiro}:`, 'Rechazado por admin QA.');
    if (motivo === null) return; // cancelado
    ejecutar(r.idBrebRetiro, `/api/breb/admin/retiros/${r.idBrebRetiro}/rechazar`, { motivo });
  }

  const pendientes = retiros.filter(r => r.estado === 'CREADO' || r.estado === 'CONFIRMADO').length;

  return (
    <div className="page">
      <h2>Retiros Bre-B QA</h2>
      <p style={{ color: '#718096', marginBottom: '1.5rem', fontSize: '0.9rem' }}>
        QA · sin Passport real · movimientos contables simulados · sin producción
      </p>

      {msg && (
        <div style={{
          background: '#f0fff4', color: '#276749', padding: '0.75rem 1rem',
          borderRadius: '6px', borderLeft: '3px solid #48bb78', marginBottom: '1rem',
        }}>
          {msg}
        </div>
      )}
      {error && <div className="error-msg" style={{ marginBottom: '1rem' }}>Error: {error}</div>}

      {loading && <div className="loading">Cargando...</div>}

      {!loading && (
        retiros.length === 0 && !error ? (
          <div className="empty">No hay retiros Bre-B registrados.</div>
        ) : retiros.length > 0 ? (
          <>
            <div className="results-meta">
              {retiros.length} retiro{retiros.length !== 1 ? 's' : ''}
              {pendientes > 0 && (
                <span style={{ marginLeft: '1rem', color: '#2b6cb0', fontWeight: 600 }}>
                  · {pendientes} pendiente{pendientes !== 1 ? 's' : ''} de acción
                </span>
              )}
            </div>
            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Tipo</th>
                    <th>Usuario / Comercio</th>
                    <th>Wallet</th>
                    <th>Valor</th>
                    <th>Estado</th>
                    <th>Llave</th>
                    <th>Referencia</th>
                    <th>Ledger</th>
                    <th>Fecha solicitud</th>
                    <th>Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {retiros.map(r => (
                    <tr key={r.idBrebRetiro}>
                      <td className="mono">{r.idBrebRetiro}</td>
                      <td>{r.tipoSujeto}</td>
                      <td className="mono">
                        {r.tipoSujeto === 'USUARIO'
                          ? `usuario #${r.idUsuario}`
                          : `comercio #${r.idComercio}`}
                      </td>
                      <td className="mono">{r.idWallet}</td>
                      <td style={{ fontWeight: 600 }}>{fmtMoney(r.valor)}</td>
                      <td>{estadoBadge(r.estado)}</td>
                      <td className="mono">{r.keyValueMasked}</td>
                      <td className="mono" style={{ fontSize: '0.78rem' }}>{r.referenciaInterna}</td>
                      <td className="mono">{r.idTransaccionLedger ?? '—'}</td>
                      <td className="mono">{fmtDate(r.fechaSolicitud)}</td>
                      <td>
                        {r.estado === 'CREADO' && (
                          <div style={{ display: 'flex', gap: '0.3rem', flexWrap: 'wrap' }}>
                            <button
                              className="btn-link"
                              style={{ background: '#ebf8ff', color: '#2b6cb0', border: '1px solid #90cdf4', fontSize: '0.8rem' }}
                              disabled={acting === r.idBrebRetiro}
                              onClick={() => confirmar(r)}
                            >
                              Confirmar
                            </button>
                            <button
                              className="btn-link"
                              style={{ background: '#fff5f5', color: '#c53030', border: '1px solid #feb2b2', fontSize: '0.8rem' }}
                              disabled={acting === r.idBrebRetiro}
                              onClick={() => rechazar(r)}
                            >
                              Rechazar
                            </button>
                          </div>
                        )}
                        {r.estado === 'CONFIRMADO' && (
                          <div style={{ display: 'flex', gap: '0.3rem', flexWrap: 'wrap' }}>
                            <button
                              className="btn-link"
                              style={{ background: '#f0fff4', color: '#276749', border: '1px solid #9ae6b4', fontSize: '0.8rem' }}
                              disabled={acting === r.idBrebRetiro}
                              onClick={() => liquidar(r)}
                            >
                              Liquidar
                            </button>
                            <button
                              className="btn-link"
                              style={{ background: '#fff5f5', color: '#c53030', border: '1px solid #feb2b2', fontSize: '0.8rem' }}
                              disabled={acting === r.idBrebRetiro}
                              onClick={() => rechazar(r)}
                            >
                              Rechazar
                            </button>
                          </div>
                        )}
                        {(r.estado === 'LIQUIDADO' || r.estado === 'RECHAZADO') && (
                          <span style={{ color: '#a0aec0', fontSize: '0.82rem' }}>
                            {r.estado === 'RECHAZADO' && r.motivoRechazo
                              ? `— ${r.motivoRechazo.slice(0, 30)}${r.motivoRechazo.length > 30 ? '…' : ''}`
                              : '—'}
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        ) : null
      )}
    </div>
  );
}
