import { ReactNode, useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate, fmtMoney, fmtNum } from '../utils.ts';

// ── Types ─────────────────────────────────────────────────────────────────────

interface ResumenGeneral {
  wallets:   { total: number; saldoUsuarios: number; saldoComercios: number };
  ventasQr:  { total: number; contingencia: number; liquidadas: number };
  retiros:   { pendientes: number; pagados: number; rechazados: number };
  ledger:    { transacciones: number };
  auditoria: { eventos: number };
}

interface RetiroItem {
  idRetiro:       number;
  idComercio:     number;
  valor:          number;
  estado:         string;
  fechaSolicitud: string;
}

interface VentaQrItem {
  idVentaQr:  number;
  idComercio: number;
  valorBruto: number;
  estado:     string;
  fechaVenta: string;
}

interface LedgerTxItem {
  idTransaccionLedger: number;
  tipoTransaccion:     string;
  valorTotal:          number;
  fechaTransaccion:    string;
}

interface Paged<T> { items: T[] }

// ── Small helpers ─────────────────────────────────────────────────────────────

function Card({ label, value }: { label: string; value: string }) {
  return (
    <div className="card">
      <div className="card-label">{label}</div>
      <div className="card-value">{value}</div>
    </div>
  );
}

function QuickCard({ label, to, onNav }: { label: string; to: string; onNav: (p: string) => void }) {
  return (
    <button className="quick-card" onClick={() => onNav(to)}>{label}</button>
  );
}

function Section({
  title, loading, error, onRetry, children,
}: {
  title: string; loading: boolean; error: string;
  onRetry?: () => void; children: ReactNode;
}) {
  return (
    <div className="dashboard-section">
      <h3>{title}</h3>
      {loading && <div className="loading">Cargando...</div>}
      {!loading && error && (
        <>
          <div className="error-msg">Error: {error}</div>
          {onRetry && (
            <button className="retry-button" onClick={onRetry}>↺ Reintentar</button>
          )}
        </>
      )}
      {!loading && !error && children}
    </div>
  );
}

function estadoBadge(estado: string) {
  const cls =
    estado === 'PAGADO'    || estado === 'LIQUIDADA'    ? 'badge-ok'   :
    estado === 'PENDIENTE' || estado === 'CONTINGENCIA' ? 'badge-info' : 'badge-warn';
  return <span className={`badge ${cls}`}>{estado}</span>;
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

export function DashboardPage() {
  const navigate = useNavigate();

  const [resumen,     setResumen]     = useState<ResumenGeneral | null>(null);
  const [resumenLoad, setResumenLoad] = useState(true);
  const [resumenErr,  setResumenErr]  = useState('');

  const [retiros,     setRetiros]     = useState<RetiroItem[]>([]);
  const [retirosLoad, setRetirosLoad] = useState(true);
  const [retirosErr,  setRetirosErr]  = useState('');

  const [ventas,      setVentas]      = useState<VentaQrItem[]>([]);
  const [ventasLoad,  setVentasLoad]  = useState(true);
  const [ventasErr,   setVentasErr]   = useState('');

  const [ledger,      setLedger]      = useState<LedgerTxItem[]>([]);
  const [ledgerLoad,  setLedgerLoad]  = useState(true);
  const [ledgerErr,   setLedgerErr]   = useState('');

  // Incrementing this counter triggers a full reload of all sections
  const [retryCount, setRetryCount] = useState(0);
  const retry = useCallback(() => setRetryCount(c => c + 1), []);

  useEffect(() => {
    setResumenLoad(true); setResumenErr('');
    setRetirosLoad(true); setRetirosErr('');
    setVentasLoad(true);  setVentasErr('');
    setLedgerLoad(true);  setLedgerErr('');

    get<{ success: boolean; data: ResumenGeneral }>('/api/reportes/operaciones/resumen-general')
      .then(r => setResumen(r.data))
      .catch(err => setResumenErr((err as Error).message))
      .finally(() => setResumenLoad(false));

    get<{ success: boolean; data: Paged<RetiroItem> }>('/api/comercios/retiros?page=1&pageSize=5')
      .then(r => setRetiros(r.data.items))
      .catch(err => setRetirosErr((err as Error).message))
      .finally(() => setRetirosLoad(false));

    get<{ success: boolean; data: Paged<VentaQrItem> }>('/api/admin/ventas-qr?page=1&pageSize=5')
      .then(r => setVentas(r.data.items))
      .catch(err => setVentasErr((err as Error).message))
      .finally(() => setVentasLoad(false));

    get<{ success: boolean; data: Paged<LedgerTxItem> }>('/api/admin/ledger-transacciones?page=1&pageSize=5')
      .then(r => setLedger(r.data.items))
      .catch(err => setLedgerErr((err as Error).message))
      .finally(() => setLedgerLoad(false));
  }, [retryCount]);

  return (
    <div className="page">
      <h2>Dashboard operativo XPAY</h2>
      <p className="dashboard-subtitle">Resumen de operación y accesos rápidos</p>

      {/* ── Accesos rápidos ── */}
      <div className="quick-actions">
        <QuickCard label="Wallets"   to="/wallets/listado"   onNav={navigate} />
        <QuickCard label="Comercios" to="/comercios/listado" onNav={navigate} />
        <QuickCard label="Retiros"   to="/retiros/listado"   onNav={navigate} />
        <QuickCard label="Ventas QR" to="/ventas-qr/listado" onNav={navigate} />
        <QuickCard label="Ledger"    to="/ledger/listado"    onNav={navigate} />
      </div>

      {/* ── Métricas ── */}
      {resumenLoad && <div className="loading">Cargando métricas...</div>}
      {!resumenLoad && resumenErr && (
        <div className="error-msg" style={{ marginBottom: '1.5rem' }}>
          Error cargando métricas: {resumenErr}
          <button className="retry-button" style={{ marginLeft: '1rem' }} onClick={retry}>
            ↺ Reintentar
          </button>
        </div>
      )}
      {!resumenLoad && !resumenErr && resumen && (
        <div className="cards">
          <Card label="Total Wallets"      value={fmtNum(resumen.wallets.total)} />
          <Card label="Saldo Usuarios"     value={fmtMoney(resumen.wallets.saldoUsuarios)} />
          <Card label="Saldo Comercios"    value={fmtMoney(resumen.wallets.saldoComercios)} />
          <Card label="Ventas QR"          value={fmtNum(resumen.ventasQr.total)} />
          <Card label="QR Liquidadas"      value={fmtNum(resumen.ventasQr.liquidadas)} />
          <Card label="QR Contingencia"    value={fmtNum(resumen.ventasQr.contingencia)} />
          <Card label="Retiros Pagados"    value={fmtNum(resumen.retiros.pagados)} />
          <Card label="Retiros Pendientes" value={fmtNum(resumen.retiros.pendientes)} />
          <Card label="Retiros Rechazados" value={fmtNum(resumen.retiros.rechazados)} />
          <Card label="Txs Ledger"         value={fmtNum(resumen.ledger.transacciones)} />
          <Card label="Auditoría Eventos"  value={fmtNum(resumen.auditoria.eventos)} />
        </div>
      )}

      {/* ── Últimos retiros ── */}
      <Section title="Últimos retiros" loading={retirosLoad} error={retirosErr} onRetry={retry}>
        {retiros.length === 0 ? (
          <div className="empty">Sin registros.</div>
        ) : (
          <div className="table-wrapper">
            <table className="compact-table">
              <thead>
                <tr>
                  <th>ID</th><th>Comercio</th><th>Valor</th>
                  <th>Estado</th><th>Fecha solicitud</th><th></th>
                </tr>
              </thead>
              <tbody>
                {retiros.map(r => (
                  <tr key={r.idRetiro}>
                    <td className="mono">{r.idRetiro}</td>
                    <td className="mono">{r.idComercio}</td>
                    <td>{fmtMoney(r.valor)}</td>
                    <td>{estadoBadge(r.estado)}</td>
                    <td className="mono">{fmtDate(r.fechaSolicitud)}</td>
                    <td>
                      <button className="btn-link" onClick={() => navigate(`/retiros/${r.idRetiro}`)}>
                        Ver
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Section>

      {/* ── Últimas ventas QR ── */}
      <Section title="Últimas ventas QR" loading={ventasLoad} error={ventasErr} onRetry={retry}>
        {ventas.length === 0 ? (
          <div className="empty">Sin registros.</div>
        ) : (
          <div className="table-wrapper">
            <table className="compact-table">
              <thead>
                <tr>
                  <th>ID</th><th>Comercio</th><th>Valor bruto</th>
                  <th>Estado</th><th>Fecha venta</th><th></th>
                </tr>
              </thead>
              <tbody>
                {ventas.map(v => (
                  <tr key={v.idVentaQr}>
                    <td className="mono">{v.idVentaQr}</td>
                    <td className="mono">{v.idComercio}</td>
                    <td className="credit">{fmtMoney(v.valorBruto)}</td>
                    <td>{estadoBadge(v.estado)}</td>
                    <td className="mono">{fmtDate(v.fechaVenta)}</td>
                    <td>
                      <button className="btn-link" onClick={() => navigate(`/comercios/${v.idComercio}`)}>
                        Ver comercio
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Section>

      {/* ── Últimas transacciones ledger ── */}
      <Section title="Últimas transacciones ledger" loading={ledgerLoad} error={ledgerErr} onRetry={retry}>
        {ledger.length === 0 ? (
          <div className="empty">Sin registros.</div>
        ) : (
          <div className="table-wrapper">
            <table className="compact-table">
              <thead>
                <tr>
                  <th>ID</th><th>Tipo</th><th>Valor total</th><th>Fecha</th><th></th>
                </tr>
              </thead>
              <tbody>
                {ledger.map(t => (
                  <tr key={t.idTransaccionLedger}>
                    <td className="mono">{t.idTransaccionLedger}</td>
                    <td>{t.tipoTransaccion}</td>
                    <td className="credit">{fmtMoney(t.valorTotal)}</td>
                    <td className="mono">{fmtDate(t.fechaTransaccion)}</td>
                    <td>
                      <button className="btn-link" onClick={() => navigate(`/ledger/${t.idTransaccionLedger}`)}>
                        Ver detalle
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Section>
    </div>
  );
}
