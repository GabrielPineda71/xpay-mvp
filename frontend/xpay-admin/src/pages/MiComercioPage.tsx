import { FormEvent, useCallback, useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext.tsx';
import { get, post } from '../api/client.ts';
import { fmtMoney, fmtDate } from '../utils.ts';

// QA/Demo mapping: username → comercio data
// Documented in docs/QA_DEMO_BUSINESS_USERS.md
const DEMO_COMERCIO_MAP: Record<string, { idComercio: number; idWalletComercio: number }> = {
  'qa.comercio1': { idComercio: 2, idWalletComercio: 4 },
};

interface ResumenComercio {
  idComercio:        number;
  nombreComercial:   string;
  idWalletComercio:  number;
  saldoDisponible:   number;
  ventasQr: {
    total:        number;
    contingencia: number;
    liquidadas:   number;
    valorTotal:   number;
  };
  liquidaciones: { total: number; valorTotal: number };
  retiros: {
    total:           number;
    pendientes:      number;
    pagados:         number;
    rechazados:      number;
    valorPendiente:  number;
    valorPagado:     number;
    valorRechazado:  number;
  };
}

interface VentaQr {
  idVentaQr:    number;
  valorBruto:   number;
  estado:       string;
  fechaVenta:   string;
  codigoQr?:    string;
}

interface RetiroComercio {
  idRetiro:     number;
  valor:        number;
  estado:       string;
  medioRetiro?: string;
  fechaCreacion:string;
}

type Msg = { ok: boolean; text: string };

export function MiComercioPage() {
  const { user } = useAuth();
  const demoInfo = user ? DEMO_COMERCIO_MAP[user.usuario] : undefined;

  const [resumen,  setResumen]  = useState<ResumenComercio | null>(null);
  const [ventas,   setVentas]   = useState<VentaQr[]>([]);
  const [retiros,  setRetiros]  = useState<RetiroComercio[]>([]);
  const [loading,  setLoading]  = useState(true);
  const [dataErr,  setDataErr]  = useState<string | null>(null);

  const [retValor, setRetValor] = useState('');
  const [retObs,   setRetObs]   = useState('Solicitud retiro demo QA desde UI');
  const [retBusy,  setRetBusy]  = useState(false);
  const [retMsg,   setRetMsg]   = useState<Msg | null>(null);

  const loadData = useCallback(async () => {
    if (!demoInfo) return;
    setLoading(true);
    setDataErr(null);
    try {
      const [resumenResp, ventasResp, retirosResp] = await Promise.all([
        get<{ success: boolean; data: ResumenComercio }>(`/api/reportes/comercios/${demoInfo.idComercio}/resumen`),
        get<{ success: boolean; data: { items?: VentaQr[] } | VentaQr[] }>(`/api/admin/ventas-qr?idComercio=${demoInfo.idComercio}&pageSize=10`),
        get<{ success: boolean; data: { items?: RetiroComercio[] } | RetiroComercio[] }>(`/api/comercios/retiros?idComercio=${demoInfo.idComercio}&pageSize=10`),
      ]);
      setResumen(resumenResp.data);

      const ventasData = ventasResp.data;
      setVentas(Array.isArray(ventasData) ? ventasData : (ventasData.items ?? []));

      const retirosData = retirosResp.data;
      setRetiros(Array.isArray(retirosData) ? retirosData : (retirosData.items ?? []));
    } catch (e) {
      setDataErr((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [demoInfo]);

  useEffect(() => { void loadData(); }, [loadData]);

  if (!user || !demoInfo) {
    return (
      <div className="page">
        <h2>Mi Comercio</h2>
        <div className="error-msg">Comercio no reconocido en el mapa demo QA. Contacta al administrador.</div>
      </div>
    );
  }

  async function handleSolicitarRetiro(e: FormEvent) {
    e.preventDefault();
    if (!resumen) return;
    setRetBusy(true);
    setRetMsg(null);
    try {
      const r = await post<{ success: boolean; message?: string }>('/api/comercios/solicitar-retiro', {
        idComercio: demoInfo!.idComercio,
        valor: Number(retValor),
        medioRetiro: 'TRANSFERENCIA_BANCARIA',
        observacion: retObs,
      });
      setRetMsg({ ok: r.success, text: r.message ?? (r.success ? 'Solicitud de retiro enviada.' : 'Error al solicitar retiro.') });
      if (r.success) { setRetValor(''); await loadData(); }
    } catch (e) {
      setRetMsg({ ok: false, text: (e as Error).message });
    } finally {
      setRetBusy(false);
    }
  }

  return (
    <div className="page">
      <h2>Mi Comercio</h2>
      <p className="dashboard-subtitle">
        {resumen?.nombreComercial ?? 'Cargando...'}
        {' · '}idComercio #{demoInfo.idComercio}
        {' · '}<span className="badge badge-info">QA / Demo</span>
      </p>

      {loading ? (
        <div className="loading">Cargando información del comercio...</div>
      ) : dataErr ? (
        <div className="error-msg">
          {dataErr}{' '}
          <button className="retry-button" onClick={() => void loadData()}>↺ Reintentar</button>
        </div>
      ) : resumen ? (
        <>
          {/* Saldo y resumen */}
          <div className="cards" style={{ marginBottom: '1.75rem' }}>
            <div className="card">
              <div className="card-label">Saldo disponible</div>
              <div className="card-value" style={{ color: '#276749' }}>{fmtMoney(resumen.saldoDisponible)}</div>
            </div>
            <div className="card">
              <div className="card-label">Ventas QR totales</div>
              <div className="card-value">{resumen.ventasQr.total}</div>
            </div>
            <div className="card">
              <div className="card-label">Valor ventas QR</div>
              <div className="card-value" style={{ fontSize: '1.1rem' }}>{fmtMoney(resumen.ventasQr.valorTotal)}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#f6ad55' }}>
              <div className="card-label">En contingencia</div>
              <div className="card-value">{resumen.ventasQr.contingencia}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#68d391' }}>
              <div className="card-label">Liquidadas</div>
              <div className="card-value">{resumen.ventasQr.liquidadas}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#a0aec0' }}>
              <div className="card-label">Retiros pendientes</div>
              <div className="card-value">{resumen.retiros.pendientes}</div>
            </div>
          </div>

          {/* Solicitar retiro */}
          <div className="action-row" style={{ marginBottom: '1.5rem' }}>
            <div className="action-section">
              <h3>Solicitar retiro</h3>
              <form className="action-form" onSubmit={e => void handleSolicitarRetiro(e)}>
                <label>
                  Valor a retirar (COP ficticio)
                  <input
                    type="number"
                    value={retValor}
                    onChange={e => setRetValor(e.target.value)}
                    required
                    min={1}
                    max={resumen.saldoDisponible || undefined}
                    placeholder="Ingresa monto"
                  />
                </label>
                <label>
                  Observación
                  <input
                    type="text"
                    value={retObs}
                    onChange={e => setRetObs(e.target.value)}
                    maxLength={200}
                  />
                </label>
                <button
                  className="btn-confirm"
                  type="submit"
                  disabled={retBusy || resumen.saldoDisponible <= 0}
                >
                  {retBusy ? 'Procesando...' : 'Solicitar retiro'}
                </button>
                {resumen.saldoDisponible <= 0 && (
                  <p style={{ fontSize: '0.82rem', color: '#a0aec0', marginTop: '0.25rem' }}>
                    Saldo $0 — requiere ventas liquidadas para habilitar retiro.
                  </p>
                )}
              </form>
              {retMsg && (
                <div className={retMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginTop: '0.75rem' }}>
                  {retMsg.text}
                </div>
              )}
            </div>
            <div className="action-section" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
              <h3>Flujo de liquidación QA</h3>
              <ol style={{ paddingLeft: '1.25rem', fontSize: '0.9rem', color: '#4a5568', lineHeight: '1.8' }}>
                <li>Usuario paga con QR → venta en <strong>CONTINGENCIA</strong></li>
                <li>Admin liquida venta QR → estado <strong>LIQUIDADA</strong></li>
                <li>Saldo del comercio aumenta</li>
                <li>Comercio solicita retiro → estado <strong>PENDIENTE</strong></li>
                <li>Admin confirma pago → estado <strong>PAGADO</strong></li>
              </ol>
              <p style={{ fontSize: '0.78rem', color: '#a0aec0', marginTop: '0.75rem' }}>
                Datos ficticios · QA/Demo · sin dinero real
              </p>
            </div>
          </div>

          {/* Ventas QR */}
          <div className="table-wrapper">
            <div className="table-title">Ventas QR del comercio ({ventas.length})</div>
            {ventas.length === 0 ? (
              <div className="empty">Sin ventas QR registradas.</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>#Venta</th>
                    <th>Estado</th>
                    <th>Valor bruto</th>
                    <th>Fecha</th>
                  </tr>
                </thead>
                <tbody>
                  {ventas.map(v => (
                    <tr key={v.idVentaQr}>
                      <td className="mono">{v.idVentaQr}</td>
                      <td>
                        <span className={`badge ${v.estado === 'LIQUIDADA' ? 'badge-ok' : v.estado === 'CONTINGENCIA' ? 'badge-warn' : 'badge-info'}`}>
                          {v.estado}
                        </span>
                      </td>
                      <td className="credit">+{fmtMoney(v.valorBruto)}</td>
                      <td className="mono">{fmtDate(v.fechaVenta)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>

          {/* Retiros */}
          <div className="table-wrapper" style={{ marginTop: '1.25rem' }}>
            <div className="table-title">Retiros del comercio ({retiros.length})</div>
            {retiros.length === 0 ? (
              <div className="empty">Sin retiros registrados.</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>#Retiro</th>
                    <th>Estado</th>
                    <th>Valor</th>
                    <th>Medio</th>
                    <th>Fecha</th>
                  </tr>
                </thead>
                <tbody>
                  {retiros.map(r => (
                    <tr key={r.idRetiro}>
                      <td className="mono">{r.idRetiro}</td>
                      <td>
                        <span className={`badge ${r.estado === 'PAGADO' ? 'badge-ok' : r.estado === 'RECHAZADO' ? 'badge-warn' : 'badge-info'}`}>
                          {r.estado}
                        </span>
                      </td>
                      <td>{fmtMoney(r.valor)}</td>
                      <td>{r.medioRetiro ?? '—'}</td>
                      <td className="mono">{fmtDate(r.fechaCreacion)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      ) : null}

      <p className="user-wallet-footer">
        Ambiente QA/Demo · datos ficticios · sin dinero real · sin producción
      </p>
    </div>
  );
}
