import { useEffect, useState } from 'react';
import { get, post } from '../api/client.ts';
import { fmtDate } from '../utils.ts';

interface LlaveAdmin {
  idBrebLlave:     number;
  tipoSujeto:      string;
  idUsuario:       number | null;
  idComercio:      number | null;
  idWallet:        number;
  keyType:         string;
  keyValueMasked:  string;
  estado:          string;
  fechaRegistro:   string;
  fechaValidacion: string | null;
  esActiva:        boolean;
}

interface ApiResp    { success: boolean; data: LlaveAdmin[]; }
interface ActionResp { success: boolean; message?: string; }

function estadoBadge(estado: string) {
  const map: Record<string, string> = {
    PENDIENTE_VALIDACION: 'breb-badge-pendiente-validacion',
    VALIDADA:             'breb-badge-validada',
    RECHAZADA:            'breb-badge-rechazada',
    SUSPENDIDA:           'breb-badge-suspendida',
  };
  const cls = map[estado] ?? 'breb-badge-no-registrada';
  return <span className={`breb-badge ${cls}`}>{estado.replace(/_/g, ' ')}</span>;
}

export function BrebLlavesAdminPage() {
  const [llaves,  setLlaves]  = useState<LlaveAdmin[]>([]);
  const [loading, setLoading] = useState(false);
  const [error,   setError]   = useState('');
  const [msg,     setMsg]     = useState('');
  const [acting,  setActing]  = useState<number | null>(null);

  function cargar() {
    setLoading(true);
    setError('');
    get<ApiResp>('/api/breb/admin/llaves')
      .then(r => setLlaves(r.data))
      .catch(err => { setLlaves([]); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }

  useEffect(cargar, []);

  async function accion(idBrebLlave: number, estado: 'VALIDADA' | 'RECHAZADA') {
    setActing(idBrebLlave);
    setMsg('');
    setError('');
    try {
      const r = await post<ActionResp>('/api/breb/admin/simular-validacion-llave', { idBrebLlave, estado });
      setMsg(r.message ?? `Llave ${idBrebLlave} → ${estado}`);
      cargar();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setActing(null);
    }
  }

  const pendientes = llaves.filter(l => l.estado === 'PENDIENTE_VALIDACION').length;

  return (
    <div className="page">
      <h2>Validación llaves Bre-B</h2>
      <p style={{ color: '#718096', marginBottom: '1.5rem', fontSize: '0.9rem' }}>
        QA · sin llamar Passport real · sin mover saldos · sin tocar ledger
      </p>

      {msg && (
        <div style={{
          background: '#f0fff4', color: '#276749', padding: '0.75rem 1rem',
          borderRadius: '6px', borderLeft: '3px solid #48bb78', marginBottom: '1rem'
        }}>
          {msg}
        </div>
      )}
      {error && <div className="error-msg" style={{ marginBottom: '1rem' }}>Error: {error}</div>}

      {loading && <div className="loading">Cargando...</div>}

      {!loading && (
        llaves.length === 0 && !error ? (
          <div className="empty">No hay llaves Bre-B registradas.</div>
        ) : llaves.length > 0 ? (
          <>
            <div className="results-meta">
              {llaves.length} llave{llaves.length !== 1 ? 's' : ''} registrada{llaves.length !== 1 ? 's' : ''}
              {pendientes > 0 && (
                <span style={{ marginLeft: '1rem', color: '#2b6cb0', fontWeight: 600 }}>
                  · {pendientes} pendiente{pendientes !== 1 ? 's' : ''} de validación
                </span>
              )}
            </div>
            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Tipo sujeto</th>
                    <th>Usuario / Comercio</th>
                    <th>Wallet</th>
                    <th>Tipo llave</th>
                    <th>Llave enmascarada</th>
                    <th>Estado</th>
                    <th>Activa</th>
                    <th>Fecha registro</th>
                    <th>Fecha validación</th>
                    <th>Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {llaves.map(l => (
                    <tr key={l.idBrebLlave}>
                      <td className="mono">{l.idBrebLlave}</td>
                      <td>{l.tipoSujeto}</td>
                      <td className="mono">
                        {l.tipoSujeto === 'USUARIO'
                          ? `usuario #${l.idUsuario}`
                          : `comercio #${l.idComercio}`}
                      </td>
                      <td className="mono">{l.idWallet}</td>
                      <td>
                        <span style={{
                          fontFamily: 'monospace', fontSize: '0.8rem',
                          background: '#edf2f7', padding: '0.1rem 0.4rem', borderRadius: '4px'
                        }}>
                          {l.keyType}
                        </span>
                      </td>
                      <td className="mono">{l.keyValueMasked}</td>
                      <td>{estadoBadge(l.estado)}</td>
                      <td style={{ textAlign: 'center' }}>{l.esActiva ? '✓' : '—'}</td>
                      <td className="mono">{fmtDate(l.fechaRegistro)}</td>
                      <td className="mono">{fmtDate(l.fechaValidacion)}</td>
                      <td>
                        {l.estado === 'PENDIENTE_VALIDACION' ? (
                          <div style={{ display: 'flex', gap: '0.4rem' }}>
                            <button
                              className="btn-link"
                              style={{ background: '#f0fff4', color: '#276749', border: '1px solid #9ae6b4' }}
                              disabled={acting === l.idBrebLlave}
                              onClick={() => accion(l.idBrebLlave, 'VALIDADA')}
                            >
                              Validar
                            </button>
                            <button
                              className="btn-link"
                              style={{ background: '#fff5f5', color: '#c53030', border: '1px solid #feb2b2' }}
                              disabled={acting === l.idBrebLlave}
                              onClick={() => accion(l.idBrebLlave, 'RECHAZADA')}
                            >
                              Rechazar
                            </button>
                          </div>
                        ) : (
                          <span style={{ color: '#a0aec0', fontSize: '0.85rem' }}>—</span>
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
