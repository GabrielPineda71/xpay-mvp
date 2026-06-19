import { FormEvent, useCallback, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext.tsx';
import { get, post } from '../api/client.ts';
import { fmtMoney, fmtDate } from '../utils.ts';

// QA/Demo mapping — temporary rule: username → wallet/user IDs
// Documented in docs/QA_DEMO_TRANSACTIONAL_USERS.md
const DEMO_MAP: Record<string, { idWallet: number; idUsuario: number; destinoDefault: number }> = {
  'qa.usuario1': { idWallet: 2, idUsuario: 3, destinoDefault: 3 },
  'qa.usuario2': { idWallet: 3, idUsuario: 4, destinoDefault: 2 },
};

interface Movimiento {
  idMovimiento:   number;
  fecha:          string;
  tipoMovimiento: string;
  naturaleza:     string;
  valor:          number;
  saldoDespues:   number;
  descripcion:    string | null;
}

interface EstadoCuenta {
  idWallet:        number;
  nombreWallet:    string;
  estado:          string;
  saldoDisponible: number;
  movimientos:     Movimiento[];
}

interface ApiResp { success: boolean; data: EstadoCuenta; }

type Msg = { ok: boolean; text: string };

export function UserWalletPage() {
  const { user } = useAuth();
  const demoInfo = user ? DEMO_MAP[user.usuario] : undefined;

  const [cuenta, setCuenta]     = useState<EstadoCuenta | null>(null);
  const [loading, setLoading]   = useState(true);
  const [dataErr, setDataErr]   = useState<string | null>(null);

  const [txDest,    setTxDest]    = useState(String(demoInfo?.destinoDefault ?? ''));
  const [txValor,   setTxValor]   = useState('5000');
  const [txDesc,    setTxDesc]    = useState('Transferencia demo QA desde UI');
  const [txBusy,    setTxBusy]    = useState(false);
  const [txMsg,     setTxMsg]     = useState<Msg | null>(null);

  const [qrCodigo,  setQrCodigo]  = useState('QR-DEMO-XPAY-QA-001');
  const [qrValor,   setQrValor]   = useState('5000');
  const [qrDesc,    setQrDesc]    = useState('Pago QR demo QA desde UI');
  const [qrBusy,    setQrBusy]    = useState(false);
  const [qrMsg,     setQrMsg]     = useState<Msg | null>(null);

  const loadCuenta = useCallback(async () => {
    if (!demoInfo) return;
    setLoading(true);
    setDataErr(null);
    try {
      const r = await get<ApiResp>(`/api/reportes/wallet/${demoInfo.idWallet}/estado-cuenta`);
      setCuenta(r.data);
    } catch (e) {
      setDataErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [demoInfo]);

  useEffect(() => { void loadCuenta(); }, [loadCuenta]);

  if (!user || !demoInfo) {
    return (
      <div className="page">
        <h2>Mi Wallet</h2>
        <div className="error-msg">
          Usuario no reconocido en el mapa demo QA. Contacta al administrador.
        </div>
      </div>
    );
  }

  async function handleTransfer(e: FormEvent) {
    e.preventDefault();
    setTxBusy(true);
    setTxMsg(null);
    try {
      const r = await post<{ success: boolean; message?: string }>('/api/wallets/transferencia', {
        idWalletOrigen:  demoInfo!.idWallet,
        idWalletDestino: Number(txDest),
        valor:           Number(txValor),
        descripcion:     txDesc,
        creadoPor:       demoInfo!.idUsuario,
      });
      setTxMsg({ ok: r.success, text: r.message ?? (r.success ? 'Transferencia realizada.' : 'Error al transferir.') });
      if (r.success) await loadCuenta();
    } catch (e) {
      setTxMsg({ ok: false, text: (e as Error).message });
    } finally {
      setTxBusy(false);
    }
  }

  async function handleQr(e: FormEvent) {
    e.preventDefault();
    setQrBusy(true);
    setQrMsg(null);
    try {
      const r = await post<{ success: boolean; message?: string }>('/api/qr/pagar', {
        codigoQr:        qrCodigo,
        idWalletUsuario: demoInfo!.idWallet,
        valor:           Number(qrValor),
        descripcion:     qrDesc,
        creadoPor:       demoInfo!.idUsuario,
      });
      setQrMsg({ ok: r.success, text: r.message ?? (r.success ? 'Pago QR realizado.' : 'Error al pagar QR.') });
      if (r.success) await loadCuenta();
    } catch (e) {
      setQrMsg({ ok: false, text: (e as Error).message });
    } finally {
      setQrBusy(false);
    }
  }

  return (
    <div className="page">
      <h2>Mi Wallet</h2>
      <p className="dashboard-subtitle">
        Usuario: <strong>{user.usuario}</strong>
        {' · '}Wallet #{demoInfo.idWallet}
        {' · '}<span className="badge badge-info">QA / Demo</span>
      </p>

      {/* Saldo */}
      {loading ? (
        <div className="loading">Cargando saldo...</div>
      ) : dataErr ? (
        <div className="error-msg">
          {dataErr}{' '}
          <button className="retry-button" onClick={() => void loadCuenta()}>↺ Reintentar</button>
        </div>
      ) : cuenta ? (
        <div className="cards" style={{ marginBottom: '1.75rem' }}>
          <div className="card">
            <div className="card-label">Saldo disponible</div>
            <div className="card-value" style={{ color: '#276749' }}>{fmtMoney(cuenta.saldoDisponible)}</div>
          </div>
          <div className="card" style={{ borderLeftColor: '#a0aec0' }}>
            <div className="card-label">Wallet</div>
            <div className="card-value" style={{ fontSize: '0.9rem', color: '#4a5568' }}>{cuenta.nombreWallet}</div>
          </div>
          <div className="card" style={{ borderLeftColor: cuenta.estado === 'ACTIVA' ? '#68d391' : '#fc8181' }}>
            <div className="card-label">Estado</div>
            <div className="card-value">
              <span className={`badge ${cuenta.estado === 'ACTIVA' ? 'badge-ok' : 'badge-warn'}`}>{cuenta.estado}</span>
            </div>
          </div>
        </div>
      ) : null}

      {/* Formularios */}
      <div className="action-row">
        {/* Transferencia */}
        <div className="action-section">
          <h3>Transferir</h3>
          <form className="action-form" onSubmit={e => void handleTransfer(e)}>
            <label>
              Wallet destino (ID)
              <input
                type="number"
                value={txDest}
                onChange={e => setTxDest(e.target.value)}
                required
                min={1}
                placeholder="Ej. 3"
              />
            </label>
            <label>
              Valor (COP ficticio)
              <input
                type="number"
                value={txValor}
                onChange={e => setTxValor(e.target.value)}
                required
                min={1}
              />
            </label>
            <label>
              Descripción
              <input
                type="text"
                value={txDesc}
                onChange={e => setTxDesc(e.target.value)}
                maxLength={200}
              />
            </label>
            <button className="btn-confirm" type="submit" disabled={txBusy}>
              {txBusy ? 'Procesando...' : 'Transferir'}
            </button>
          </form>
          {txMsg && (
            <div className={txMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginTop: '0.75rem' }}>
              {txMsg.text}
            </div>
          )}
        </div>

        {/* Pago QR */}
        <div className="action-section">
          <h3>Pagar con QR</h3>
          <form className="action-form" onSubmit={e => void handleQr(e)}>
            <label>
              Código QR
              <input
                type="text"
                value={qrCodigo}
                onChange={e => setQrCodigo(e.target.value)}
                required
                placeholder="QR-DEMO-XPAY-QA-001"
              />
            </label>
            <label>
              Valor (COP ficticio)
              <input
                type="number"
                value={qrValor}
                onChange={e => setQrValor(e.target.value)}
                required
                min={1}
              />
            </label>
            <label>
              Descripción
              <input
                type="text"
                value={qrDesc}
                onChange={e => setQrDesc(e.target.value)}
                maxLength={200}
              />
            </label>
            <button className="btn-confirm" type="submit" disabled={qrBusy}>
              {qrBusy ? 'Procesando...' : 'Pagar QR'}
            </button>
          </form>
          {qrMsg && (
            <div className={qrMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginTop: '0.75rem' }}>
              {qrMsg.text}
            </div>
          )}
        </div>
      </div>

      {/* Movimientos */}
      {!loading && cuenta && (
        <div className="table-wrapper" style={{ marginTop: '1.5rem' }}>
          <div className="table-title">Movimientos recientes ({cuenta.movimientos.length})</div>
          {cuenta.movimientos.length === 0 ? (
            <div className="empty">Sin movimientos registrados.</div>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Tipo</th>
                  <th>Valor</th>
                  <th>Saldo después</th>
                  <th>Descripción</th>
                  <th>Fecha</th>
                </tr>
              </thead>
              <tbody>
                {cuenta.movimientos.map(m => (
                  <tr key={m.idMovimiento}>
                    <td>
                      <span className={`badge ${m.naturaleza === 'C' ? 'badge-ok' : 'badge-warn'}`}>
                        {m.tipoMovimiento}
                      </span>
                    </td>
                    <td className={m.naturaleza === 'C' ? 'credit' : 'debit'}>
                      {m.naturaleza === 'C' ? '+' : '−'}{fmtMoney(m.valor)}
                    </td>
                    <td>{fmtMoney(m.saldoDespues)}</td>
                    <td>{m.descripcion ?? '—'}</td>
                    <td className="mono">{fmtDate(m.fecha)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      <p className="user-wallet-footer">
        Ambiente QA/Demo · saldos y transacciones ficticios · sin dinero real · sin producción
      </p>
    </div>
  );
}
