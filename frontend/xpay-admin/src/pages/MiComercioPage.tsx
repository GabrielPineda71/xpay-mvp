import { FormEvent, useCallback, useEffect, useState } from 'react';
import QRCode from 'qrcode';
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

interface BrebLlave {
  idBrebLlave:     number;
  tipoSujeto:      string;
  keyType:         string;
  keyValueMasked:  string;
  estado:          string;
  fechaRegistro?:  string;
  fechaValidacion?: string;
}

interface BrebRetiro {
  idBrebRetiro:      number;
  tipoSujeto:        string;
  valor:             number;
  moneda:            string;
  estado:            string;
  referenciaInterna: string;
  keyValueMasked:    string;
  fechaSolicitud:    string;
  motivoRechazo?:    string;
}

interface ResumenDisponibilidad {
  totalNoDisponibleBruto:          number;
  totalNoDisponibleNetoProgramado: number;
  totalDisponibleBruto:            number;
  totalLiquidado:                  number;
  cantidadNoDisponible:            number;
  proximaFechaDisponibilidad?:     string;
  valorEstimadoProximaLiberacion:  number;
}

interface VentaNoDisponible {
  idDisponibilidad:         number;
  idVentaQr:                number;
  valorBruto:               number;
  valorDescuento:           number;
  valorNetoProgramado:      number;
  diasDisponibilidad:       number;
  fechaDisponibleProgramada:string;
  diasFaltantes:            number;
  tasaAnticipada:           number;
  valorDescuentoAnticipado: number;
  valorNetoSiLiquidaAhora:  number;
  estado:                   string;
}

interface ComercioScope {
  idUsuario:               number;
  rolComercio:             string;
  idComercioAliado:        number;
  idComercioExistente?:    number;
  idEstablecimiento?:      number;
  puedeVerTodoComercio:    boolean;
  puedeDisponerRecursos:   boolean;
  puedeLiquidarAnticipado: boolean;
  puedeEnviarBreb:         boolean;
  puedeAnularVentasDiaActual: boolean;
  puedeGenerarQr:          boolean;
}

export function MiComercioPage() {
  const { user } = useAuth();
  const demoInfo = user ? DEMO_COMERCIO_MAP[user.usuario] : undefined;

  const [resumen,  setResumen]  = useState<ResumenComercio | null>(null);
  const [ventas,   setVentas]   = useState<VentaQr[]>([]);
  const [retiros,  setRetiros]  = useState<RetiroComercio[]>([]);
  const [loading,  setLoading]  = useState(true);
  const [dataErr,  setDataErr]  = useState<string | null>(null);

  // ── Filtros de fecha ─────────────────────────────────────────────────────
  const defaultDesde = (() => {
    const d = new Date(); d.setDate(d.getDate() - 30); return d.toISOString().slice(0, 10);
  })();
  const defaultHasta = new Date().toISOString().slice(0, 10);
  const [fechaDesde, setFechaDesde] = useState(defaultDesde);
  const [fechaHasta, setFechaHasta] = useState(defaultHasta);

  // ── Bre-B comercio ───────────────────────────────────────────────────────
  const [brebLlave,    setBrebLlave]    = useState<BrebLlave | null>(null);
  const [brebKeyType,  setBrebKeyType]  = useState('ID');
  const [brebKeyValue, setBrebKeyValue] = useState('');
  const [brebRegBusy,  setBrebRegBusy]  = useState(false);
  const [brebRegMsg,   setBrebRegMsg]   = useState<Msg | null>(null);
  const [brebRetiros,  setBrebRetiros]  = useState<BrebRetiro[]>([]);
  const [brebRetValor, setBrebRetValor] = useState('');
  const [brebRetBusy,  setBrebRetBusy]  = useState(false);
  const [brebRetMsg,   setBrebRetMsg]   = useState<Msg | null>(null);

  // ── Scope operativo ───────────────────────────────────────────────────────
  const [scope, setScope] = useState<ComercioScope | null>(null);

  // ── Disponibilidad ventas ─────────────────────────────────────────────────
  const [dispResumen,   setDispResumen]   = useState<ResumenDisponibilidad | null>(null);
  const [ventasNoDisp,  setVentasNoDisp]  = useState<VentaNoDisponible[]>([]);
  const [liquidando,    setLiquidando]    = useState<number | null>(null);
  const [dispMsg,       setDispMsg]       = useState<Msg | null>(null);

  // ── QR del comercio ───────────────────────────────────────────────────────
  const [qrComValor,   setQrComValor]   = useState('');
  const [qrComSrc,     setQrComSrc]     = useState<string | null>(null);
  const [qrComPayload, setQrComPayload] = useState<string>('');
  const [qrComBusy,    setQrComBusy]    = useState(false);
  const [qrComCopied,  setQrComCopied]  = useState(false);

  const loadData = useCallback(async () => {
    if (!demoInfo) return;
    setLoading(true);
    setDataErr(null);
    try {
      const [resumenResp, ventasResp, retirosResp] = await Promise.all([
        get<{ success: boolean; data: ResumenComercio }>(`/api/reportes/comercios/${demoInfo.idComercio}/resumen`),
        get<{ success: boolean; data: { items?: VentaQr[] } | VentaQr[] }>(
          `/api/admin/ventas-qr?idComercio=${demoInfo.idComercio}&pageSize=50&desde=${fechaDesde}&hasta=${fechaHasta}`),
        get<{ success: boolean; data: { items?: RetiroComercio[] } | RetiroComercio[] }>(
          `/api/comercios/retiros?idComercio=${demoInfo.idComercio}&pageSize=50&desde=${fechaDesde}&hasta=${fechaHasta}`),
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
  }, [demoInfo, fechaDesde, fechaHasta]);

  useEffect(() => {
    void loadData();
    void (async () => {
      try {
        const r = await get<{ success: boolean; data: ComercioScope | null }>('/api/comercio/mi-scope');
        setScope(r.data ?? null);
      } catch { /* non-critical */ }
    })();
    if (demoInfo) {
      void (async () => {
        try {
          const [llaveR, retirosR] = await Promise.all([
            get<{ success: boolean; data: BrebLlave | null }>(
              `/api/breb/mi-llave/comercio?idComercio=${demoInfo.idComercio}`),
            get<{ success: boolean; data: BrebRetiro[] }>(
              `/api/breb/mis-retiros/comercio?idComercio=${demoInfo.idComercio}`),
          ]);
          setBrebLlave(llaveR.data);
          setBrebRetiros(retirosR.data ?? []);
        } catch { /* non-critical */ }
      })();
      void (async () => {
        try {
          const [resR, listR] = await Promise.all([
            get<{ success: boolean; data: ResumenDisponibilidad }>(
              `/api/comercio/ventas-disponibilidad/resumen?idComercio=${demoInfo.idComercio}&desde=${fechaDesde}&hasta=${fechaHasta}`),
            get<{ success: boolean; data: VentaNoDisponible[] }>(
              `/api/comercio/ventas-no-disponibles?idComercio=${demoInfo.idComercio}&desde=${fechaDesde}&hasta=${fechaHasta}`),
          ]);
          setDispResumen(resR.data);
          setVentasNoDisp(listR.data ?? []);
        } catch { /* non-critical — tablas pueden no existir aún */ }
      })();
    }
  }, [loadData, demoInfo]);

  if (!user || !demoInfo) {
    return (
      <div className="page">
        <h2>Mi Comercio</h2>
        <div className="error-msg">Comercio no reconocido en el mapa demo QA. Contacta al administrador.</div>
      </div>
    );
  }

  async function handleGenerarQrComercio() {
    if (!resumen) return;
    setQrComBusy(true);
    try {
      const payload = JSON.stringify({
        type:         'XPAY_MERCHANT_PAYMENT',
        env:          'QA',
        version:      1,
        merchantName: resumen.nombreComercial,
        qrCode:       'QR-DEMO-XPAY-QA-001',
        amount:       qrComValor ? Number(qrComValor) : null,
        currency:     'COP',
      });
      const dataUrl = await QRCode.toDataURL(payload, { width: 280, margin: 2, color: { dark: '#1a202c' } });
      setQrComSrc(dataUrl);
      setQrComPayload(payload);
    } finally { setQrComBusy(false); }
  }

  function handleDescargarQrComercio() {
    if (!qrComSrc) return;
    const a = document.createElement('a');
    a.href = qrComSrc;
    a.download = 'xpay-comercio-QR-DEMO-XPAY-QA-001.png';
    a.click();
  }

  async function handleCopiarQrComercio() {
    if (!qrComPayload) return;
    try {
      await navigator.clipboard.writeText(qrComPayload);
      setQrComCopied(true);
      setTimeout(() => setQrComCopied(false), 2000);
    } catch { /* clipboard not available */ }
  }

  async function handleRegistrarLlaveComercio(e: FormEvent) {
    e.preventDefault();
    if (!brebKeyValue.trim() || !demoInfo) return;
    setBrebRegBusy(true); setBrebRegMsg(null);
    try {
      const r = await post<{ success: boolean; data?: BrebLlave; message?: string }>(
        '/api/breb/mi-llave/comercio',
        { keyType: brebKeyType, keyValue: brebKeyValue.trim(), idComercio: demoInfo.idComercio },
      );
      if (r.success && r.data) {
        setBrebLlave(r.data);
        setBrebKeyValue('');
        setBrebRegMsg({ ok: true, text: `Llave registrada: ${r.data.keyValueMasked} — ${r.data.estado}` });
      } else {
        setBrebRegMsg({ ok: false, text: r.message ?? 'Error registrando llave.' });
      }
    } catch (err) {
      setBrebRegMsg({ ok: false, text: (err as Error).message || 'Error registrando llave.' });
    } finally { setBrebRegBusy(false); }
  }

  async function handleSolicitarRetiroComercio(e: FormEvent) {
    e.preventDefault();
    const val = Number(brebRetValor);
    if (!val || val <= 0 || !demoInfo) { setBrebRetMsg({ ok: false, text: 'Ingresa un valor válido.' }); return; }
    setBrebRetBusy(true); setBrebRetMsg(null);
    try {
      const r = await post<{ success: boolean; data?: BrebRetiro; message?: string }>(
        '/api/breb/retiros/simular',
        { valor: val, idComercio: demoInfo.idComercio },
      );
      if (r.success && r.data) {
        setBrebRetiros(prev => [r.data!, ...prev]);
        setBrebRetValor('');
        setBrebRetMsg({ ok: true, text: `Retiro simulado. Ref: ${r.data.referenciaInterna} — ${r.data.estado}` });
      } else {
        setBrebRetMsg({ ok: false, text: r.message ?? 'Error creando retiro.' });
      }
    } catch (err) {
      setBrebRetMsg({ ok: false, text: (err as Error).message || 'Error creando retiro.' });
    } finally { setBrebRetBusy(false); }
  }

  return (
    <div className="page">
      <h2>Mi Comercio</h2>
      <p className="dashboard-subtitle">
        {resumen?.nombreComercial ?? 'Cargando...'}
        {' · '}idComercio #{demoInfo.idComercio}
        {' · '}<span className="badge badge-info">QA / Demo</span>
        {scope && <>{' · '}<span className="badge badge-ok">{scope.rolComercio}</span></>}
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

          {/* Filtros de fecha */}
          <div style={{
            display: 'flex', gap: '1rem', flexWrap: 'wrap', alignItems: 'flex-end',
            marginBottom: '1.25rem', padding: '0.75rem 1rem',
            background: '#f7fafc', border: '1px solid #e2e8f0', borderRadius: '8px',
          }}>
            <span style={{ fontSize: '0.85rem', color: '#4a5568', fontWeight: 600, alignSelf: 'center' }}>
              Filtrar por fecha
            </span>
            <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
              Desde
              <input type="date" value={fechaDesde}
                onChange={e => setFechaDesde(e.target.value)}
                style={{ maxWidth: '160px' }} />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
              Hasta
              <input type="date" value={fechaHasta}
                onChange={e => setFechaHasta(e.target.value)}
                style={{ maxWidth: '160px' }} />
            </label>
            <button className="btn-secondary" onClick={() => void loadData()}>
              Actualizar
            </button>
            <span style={{ fontSize: '0.78rem', color: '#a0aec0', alignSelf: 'center' }}>
              Aplica a ventas QR, retiros y disponibilidad
            </span>
          </div>

          {/* QR del comercio */}
          <div className="comercio-qr-section">
            <h3 style={{ marginBottom: '0.5rem' }}>QR del comercio</h3>
            <p className="tab-hint">
              Genera el QR de cobro de este comercio para mostrarlo a los usuarios o imprimirlo.
              Código: <code>QR-DEMO-XPAY-QA-001</code>
            </p>
            <label>
              Valor (opcional — COP ficticio)
              <input
                type="number"
                value={qrComValor}
                onChange={e => { setQrComValor(e.target.value); setQrComSrc(null); setQrComPayload(''); }}
                placeholder="Dejar vacío si el cliente elige el monto"
                min={0}
                style={{ maxWidth: '260px' }}
              />
            </label>
            <button
              className="btn-confirm"
              onClick={() => void handleGenerarQrComercio()}
              disabled={qrComBusy || !resumen}
              style={{ marginTop: '0.5rem' }}
            >
              {qrComBusy ? 'Generando...' : 'Generar QR comercio'}
            </button>

            {qrComSrc && (
              <div className="qr-display" style={{ marginTop: '1rem' }}>
                <img src={qrComSrc} alt="QR del comercio" className="qr-image" />
                <p className="qr-caption">
                  {qrComValor
                    ? `QR con valor ${fmtMoney(Number(qrComValor))} (COP ficticio)`
                    : 'QR sin valor fijo — el usuario ingresa el monto'}
                </p>
                <div className="qr-action-row">
                  <button className="btn-secondary" onClick={handleDescargarQrComercio}>
                    ↓ Descargar QR PNG
                  </button>
                  <button className="btn-secondary" onClick={() => void handleCopiarQrComercio()}>
                    {qrComCopied ? '✓ Copiado' : '⎘ Copiar JSON'}
                  </button>
                </div>
              </div>
            )}

            <p className="tab-warn">
              QA/Demo · el QR contiene type=XPAY_MERCHANT_PAYMENT, qrCode=QR-DEMO-XPAY-QA-001 ·
              datos ficticios · sin dinero real.
            </p>
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

      {/* ── DISPONIBILIDAD VENTAS (liquidación anticipada) ──────────────── */}
      {(scope == null || scope.puedeLiquidarAnticipado) && dispResumen && (
        <>
          <hr style={{ margin: '1.5rem 0', borderColor: '#e2e8f0' }} />
          <h3 style={{ margin: '0 0 0.75rem', fontSize: '1rem', color: '#2d3748' }}>Disponibilidad de ventas</h3>
          <div className="cards" style={{ marginBottom: '1rem' }}>
            <div className="card" style={{ borderLeftColor: '#f6ad55' }}>
              <div className="card-label">No disponibles</div>
              <div className="card-value">{dispResumen.cantidadNoDisponible}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#f6ad55' }}>
              <div className="card-label">Bruto retenido</div>
              <div className="card-value" style={{ fontSize: '1.1rem' }}>{fmtMoney(dispResumen.totalNoDisponibleBruto)}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#4299e1' }}>
              <div className="card-label">Neto programado</div>
              <div className="card-value" style={{ fontSize: '1.1rem' }}>{fmtMoney(dispResumen.totalNoDisponibleNetoProgramado)}</div>
            </div>
            <div className="card" style={{ borderLeftColor: '#68d391' }}>
              <div className="card-label">Ya liquidado</div>
              <div className="card-value" style={{ fontSize: '1.1rem', color:'#276749' }}>{fmtMoney(dispResumen.totalLiquidado)}</div>
            </div>
          </div>
          {dispResumen.proximaFechaDisponibilidad && (
            <p style={{ fontSize: '0.84rem', color: '#4a5568', marginBottom: '1rem' }}>
              Próxima liberación automática: <strong>{dispResumen.proximaFechaDisponibilidad}</strong> · valor estimado: <strong>{fmtMoney(dispResumen.valorEstimadoProximaLiberacion)}</strong>
            </p>
          )}

          {dispMsg && (
            <div className={dispMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginBottom: '0.75rem' }}>{dispMsg.text}</div>
          )}

          {ventasNoDisp.length === 0 ? (
            <p style={{ color: '#718096', fontSize: '0.87rem' }}>Sin ventas en periodo de indisponibilidad.</p>
          ) : (
            <div className="table-wrapper">
              <div className="table-title">Ventas no disponibles — Liquidar anticipadamente</div>
              <div style={{ overflowX: 'auto' }}>
                <table>
                  <thead>
                    <tr>
                      <th>#Venta</th>
                      <th>Bruto</th>
                      <th>Neto prog.</th>
                      <th>Días falt.</th>
                      <th>Tasa ant.</th>
                      <th>Neto si liquida ahora</th>
                      <th>Disponible el</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {ventasNoDisp.map(v => (
                      <tr key={v.idDisponibilidad}>
                        <td className="mono">{v.idVentaQr}</td>
                        <td className="mono">{fmtMoney(v.valorBruto)}</td>
                        <td className="mono">{fmtMoney(v.valorNetoProgramado)}</td>
                        <td className="mono">{v.diasFaltantes}</td>
                        <td className="mono">{v.tasaAnticipada}%</td>
                        <td className="credit">{fmtMoney(v.valorNetoSiLiquidaAhora)}</td>
                        <td className="mono" style={{ fontSize: '0.8rem' }}>{v.fechaDisponibleProgramada.replace('T', ' ').slice(0, 16)}</td>
                        <td>
                          <button
                            className="btn-confirm"
                            style={{ fontSize: '0.78rem', padding: '0.25rem 0.7rem' }}
                            disabled={liquidando === v.idDisponibilidad}
                            onClick={async () => {
                              if (!confirm(`¿Liquidar anticipadamente venta #${v.idVentaQr}?\nRecibirás ${fmtMoney(v.valorNetoSiLiquidaAhora)} (descuento de ${v.tasaAnticipada}%).`)) return;
                              setLiquidando(v.idDisponibilidad);
                              setDispMsg(null);
                              try {
                                const r = await post<{ success: boolean; data: any; message?: string }>(
                                  `/api/comercio/ventas-no-disponibles/${v.idDisponibilidad}/liquidar-ahora?idComercio=${demoInfo.idComercio}`, {}
                                );
                                if (r.success) {
                                  setDispMsg({ ok: true, text: `Venta #${v.idVentaQr} liquidada. Neto recibido: ${fmtMoney(r.data.valorNetoLiberado)}` });
                                  // Refresh
                                  const [rR, lR] = await Promise.all([
                                    get<{ success: boolean; data: ResumenDisponibilidad }>(`/api/comercio/ventas-disponibilidad/resumen?idComercio=${demoInfo.idComercio}&desde=${fechaDesde}&hasta=${fechaHasta}`),
                                    get<{ success: boolean; data: VentaNoDisponible[] }>(`/api/comercio/ventas-no-disponibles?idComercio=${demoInfo.idComercio}&desde=${fechaDesde}&hasta=${fechaHasta}`),
                                  ]);
                                  setDispResumen(rR.data);
                                  setVentasNoDisp(lR.data ?? []);
                                  await loadData(); // refresh wallet balance
                                } else {
                                  setDispMsg({ ok: false, text: r.message ?? 'Error liquidando.' });
                                }
                              } catch(e) {
                                setDispMsg({ ok: false, text: (e as Error).message });
                              } finally {
                                setLiquidando(null);
                              }
                            }}
                          >
                            {liquidando === v.idDisponibilidad ? '...' : 'Liquidar ahora'}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </>
      )}

      {/* ── RETIRAR SALDO DEL COMERCIO (Bre-B) ──────────────────────────── */}
      {/* Visible only for ADMIN_COMERCIO (puedeEnviarBreb) or when scope not loaded (legacy) */}
      {(scope == null || scope.puedeEnviarBreb) && <hr style={{ margin: '1.5rem 0', borderColor: '#e2e8f0' }} />}
      {(scope == null || scope.puedeEnviarBreb) && <>
      <h3 style={{ margin: '0 0 0.5rem', fontSize: '1rem', color: '#2d3748' }}>Retirar saldo del comercio</h3>
      <div className="breb-section">
        <span className="breb-sandbox-badge">Sandbox Passport — retiro simulado, sin dinero real</span>

        <div className="breb-status-card">
          <div className="breb-status-row">
            <span className="breb-status-label">Llave Bre-B del comercio aliado:</span>
            {brebLlave ? (
              <>
                <span className={`breb-badge breb-badge-${brebLlave.estado.toLowerCase().replace(/_/g, '-')}`}>
                  {brebLlave.estado.replace(/_/g, ' ')}
                </span>
                <span className="breb-key-masked">{brebLlave.keyType} · {brebLlave.keyValueMasked}</span>
              </>
            ) : (
              <span className="breb-badge breb-badge-no-registrada">NO REGISTRADA</span>
            )}
          </div>
        </div>

        <h4 style={{ margin: '0 0 0.3rem', fontSize: '0.88rem', color: '#2d3748' }}>
          {brebLlave ? 'Actualizar llave' : 'Registrar llave Bre-B'}
        </h4>
        <form className="breb-form" onSubmit={(e) => void handleRegistrarLlaveComercio(e)}>
          <label>
            Tipo de llave
            <select value={brebKeyType} onChange={e => setBrebKeyType(e.target.value)}>
              <option value="ID">NIT / ID</option>
              <option value="PHONE">Número de celular</option>
              <option value="EMAIL">Correo electrónico</option>
              <option value="ALPHA">Alias alfanumérico</option>
              <option value="BCODE">Código Bre-B</option>
            </select>
          </label>
          <label>
            Valor de la llave
            <input
              type="text"
              value={brebKeyValue}
              onChange={e => setBrebKeyValue(e.target.value)}
              placeholder="Llave Bre-B del comercio"
            />
          </label>
          <p className="breb-confirm-text">
            Esta es la llave Bre-B del comercio aliado como destinatario. XPAY realizará el pago desde su cuenta bancaria operativa en Coopcentral.
          </p>
          <button type="submit" className="btn-breb" disabled={brebRegBusy || !brebKeyValue.trim()}>
            {brebRegBusy ? 'Registrando...' : brebLlave ? 'Actualizar llave' : 'Registrar llave'}
          </button>
          {brebRegMsg && (
            <span className={brebRegMsg.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{brebRegMsg.text}</span>
          )}
        </form>

        {brebLlave?.estado === 'VALIDADA' && (
          <form className="breb-retiro-form" onSubmit={(e) => void handleSolicitarRetiroComercio(e)}>
            <h4 style={{ margin: '0', fontSize: '0.88rem', color: '#2d3748' }}>Solicitar retiro de saldo</h4>
            <p className="breb-retiro-note">
              Destino: <strong>{brebLlave.keyType} · {brebLlave.keyValueMasked}</strong>
            </p>
            <label>
              Valor a retirar (COP ficticio)
              <input
                type="number"
                min="1"
                step="1"
                value={brebRetValor}
                onChange={e => setBrebRetValor(e.target.value)}
                placeholder="Ej: 100000"
              />
            </label>
            <button type="submit" className="btn-breb" disabled={brebRetBusy || !brebRetValor}>
              {brebRetBusy ? 'Procesando...' : 'Solicitar retiro simulado'}
            </button>
            {brebRetMsg && (
              <span className={brebRetMsg.ok ? 'breb-msg-ok' : 'breb-msg-err'}>{brebRetMsg.text}</span>
            )}
          </form>
        )}

        {brebRetiros.length > 0 && (
          <>
            <h4 style={{ margin: '1rem 0 0.3rem', fontSize: '0.88rem', color: '#2d3748' }}>Historial retiros Bre-B</h4>
            <table className="breb-retiros-table">
              <thead>
                <tr><th>Ref</th><th>Valor</th><th>Estado</th><th>Llave</th><th>Fecha</th></tr>
              </thead>
              <tbody>
                {brebRetiros.map(r => (
                  <tr key={r.idBrebRetiro}>
                    <td className="mono">{r.referenciaInterna}</td>
                    <td>{fmtMoney(r.valor)}</td>
                    <td><span className={`breb-badge breb-badge-${r.estado.toLowerCase().replace(/_/g, '-')}`}>{r.estado}</span></td>
                    <td>{r.keyValueMasked}</td>
                    <td>{fmtDate(r.fechaSolicitud)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </div>
      </>}

      {scope && !scope.puedeEnviarBreb && (
        <p style={{ fontSize:'0.82rem', color:'#a0aec0', margin:'1.5rem 0 0' }}>
          Tu rol ({scope.rolComercio}) no tiene acceso a la sección de retiros Bre-B.
        </p>
      )}

      <p className="user-wallet-footer">
        Ambiente QA/Demo · datos ficticios · sin dinero real · sin producción
      </p>
    </div>
  );
}
