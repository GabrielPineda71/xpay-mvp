import { useState, useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { get, post } from '../api/client.ts';

interface MiCupo {
  idCupo: number;
  idWallet: number;
  cupoAprobado: number;
  cupoUsado: number;
  cupoDisponible: number;
  estado: string;
  fechaAprobacion: string;
  fechaVencimiento: string | null;
}

interface MiCredito {
  idUtilizacion: number;
  nroCredito: number;
  tipoUtilizacion: string;
  valorCapital: number;
  estado: string;
  fechaDesembolso: string | null;
  totalCuotas: number;
  cuotasPagadas: number;
  saldoPendiente: number;
  proximaCuota: number | null;
  valorProximaCuota: number | null;
}

interface CuotaDetalle {
  idCuota: number;
  numeroCuota: number;
  fechaVencimiento: string;
  valorCapital: number;
  valorInteres: number;
  valorAval: number;
  valorAdmin: number;
  valorIva: number;
  valorGastosCobranza: number;
  valorTotal: number;
  pagadoCapital: number;
  pagadoInteres: number;
  pagadoAval: number;
  pagadoAdmin: number;
  pagadoIva: number;
  saldoCuota: number;
  estado: string;
}

interface CuotaAfectada {
  idCuota: number;
  numeroCuota: number;
  capitalPagado: number;
  interesPagado: number;
  avalPagado: number;
  adminPagado: number;
  ivaPagado: number;
  valorPagado: number;
  saldoCuotaDespues: number;
  estado: string;
}

interface PagoCuotaResult {
  idPago: number;
  idTransaccionLedger: number | null;
  valorPago: number;
  saldoWalletAntes: number;
  saldoWalletDespues: number;
  cupoUsadoAntes: number;
  cupoUsadoDespues: number;
  cupoDisponibleAntes: number;
  cupoDisponibleDespues: number;
  capitalPagado: number;
  interesesPagados: number;
  avalPagado: number;
  adminPagado: number;
  ivaPagado: number;
  cuotasAfectadas: CuotaAfectada[];
}

interface WalletEstadoCuenta {
  success: boolean;
  data: { saldoDisponible: number };
}

interface CuotaSimulada {
  numeroCuota: number;
  fechaVencimiento: string;
  valorCapital: number;
  valorInteres: number;
  valorAval: number;
  valorAdmin: number;
  valorIva: number;
  valorTotal: number;
  saldoCapitalAntes: number;
  saldoCapitalDespues: number;
}

interface SimulacionResult {
  tipoUtilizacion: string;
  valorCapital: number;
  tasaEmv: number;
  porcAval: number;
  porcAdmin: number;
  aplicaIva: boolean;
  porcIva: number;
  plazoMeses: number;
  frecuencia: string;
  totalCuotas: number;
  valorCuota: number;
  valorTotalIntereses: number;
  valorTotalAval: number;
  valorTotalAdmin: number;
  valorTotalIva: number;
  valorTotalPagar: number;
  cuotas: CuotaSimulada[];
}

interface ConfirmacionResult {
  idUtilizacion: number;
  tipoUtilizacion: string;
  valorCapital: number;
  estado: string;
  fechaDesembolso: string;
  nuevoSaldoWallet: number;
  nuevoCupoDisponible: number;
  cuotas: CuotaSimulada[];
}

const fmt = (v: number) =>
  new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(v);
const fmtPct = (v: number) => `${v}%`;

// PIN: format-only validation for QA/Demo phase — same convention as UserWalletPage.
// Full cryptographic validation (backend hash, attempt limits, lockout) is pending for production.
function validatePin(pin: string): string | null {
  if (!/^\d{7}$/.test(pin)) return 'La clave debe ser exactamente 7 dígitos numéricos.';
  return null;
}

type ModoSimulador = 'compra' | 'desembolso';

export function MiCarteraOrdinariaPage() {
  const location = useLocation();
  const qp = new URLSearchParams(location.search);
  const origenQR   = qp.get('origen') === 'QR';
  const qpValor    = qp.get('valor') ?? '';
  const qpQrCode   = qp.get('qrCode') ?? '';

  const [cupo, setCupo]           = useState<MiCupo | null>(null);
  const [cupoError, setCupoError] = useState('');

  // Simulador estado
  const [modo, setModo]               = useState<ModoSimulador>(origenQR ? 'compra' : 'compra');
  const [tipo, setTipo]               = useState(origenQR ? 'COMPRA_COMERCIO' : 'COMPRA_COMERCIO');
  const [monto, setMonto]             = useState(qpValor);
  const [plazo, setPlazo]             = useState('12');
  const [frecuencia, setFrecuencia]   = useState('MENSUAL');
  const [sim, setSim]                 = useState<SimulacionResult | null>(null);
  const [simBusy, setSimBusy]         = useState(false);
  const [simError, setSimError]       = useState('');
  const [showCuotas, setShowCuotas]   = useState(false);

  // Confirmación real (AVANCE_WALLET)
  const [confirmBusy, setConfirmBusy]   = useState(false);
  const [confirmError, setConfirmError] = useState('');
  const [confirmResult, setConfirmResult] = useState<ConfirmacionResult | null>(null);
  const [confirmPin, setConfirmPin]     = useState('');

  // Mis créditos / pago manual de cuotas
  const [creditos, setCreditos]           = useState<MiCredito[]>([]);
  const [creditosError, setCreditosError] = useState('');
  const [cuotasPorCredito, setCuotasPorCredito] = useState<Record<number, CuotaDetalle[]>>({});
  const [cuotasVisibles, setCuotasVisibles]     = useState<Record<number, boolean>>({});
  const [cuotasBusy, setCuotasBusy]             = useState<Record<number, boolean>>({});
  const [walletSaldo, setWalletSaldo]     = useState<number | null>(null);
  const [pagoAbierto, setPagoAbierto]     = useState<number | null>(null); // idUtilizacion
  const [pagoValor, setPagoValor]         = useState('');
  const [pagoPin, setPagoPin]             = useState('');
  const [pagoBusy, setPagoBusy]           = useState(false);
  const [pagoError, setPagoError]         = useState('');
  const [pagoResult, setPagoResult]       = useState<PagoCuotaResult | null>(null);

  const cargarWalletSaldo = (idWallet: number) => {
    get<WalletEstadoCuenta>(`/api/reportes/wallet/${idWallet}/estado-cuenta`)
      .then(res => setWalletSaldo(res.data.saldoDisponible))
      .catch(() => { /* no bloquea la pantalla si falla */ });
  };

  const cargarCupo = () => {
    get<MiCupo>('/api/cartera-ordinaria/mi-cupo')
      .then(data => { setCupo(data); cargarWalletSaldo(data.idWallet); })
      .catch(() => setCupoError('No tienes un cupo ordinario activo en este momento.'));
  };

  const cargarCreditos = () => {
    get<MiCredito[]>('/api/cartera-ordinaria/mis-creditos')
      .then(data => setCreditos(data))
      .catch(() => setCreditosError('No se pudieron cargar tus créditos de Cartera Ordinaria.'));
  };

  useEffect(cargarCupo, []);
  useEffect(cargarCreditos, []);

  const toggleCuotas = async (idUtilizacion: number) => {
    const yaVisible = !!cuotasVisibles[idUtilizacion];
    setCuotasVisibles(prev => ({ ...prev, [idUtilizacion]: !yaVisible }));
    if (!yaVisible && !cuotasPorCredito[idUtilizacion]) {
      setCuotasBusy(prev => ({ ...prev, [idUtilizacion]: true }));
      try {
        const data = await get<CuotaDetalle[]>(`/api/cartera-ordinaria/mis-creditos/${idUtilizacion}/cuotas`);
        setCuotasPorCredito(prev => ({ ...prev, [idUtilizacion]: data }));
      } catch {
        // silencioso: la sección simplemente queda vacía si falla
      } finally {
        setCuotasBusy(prev => ({ ...prev, [idUtilizacion]: false }));
      }
    }
  };

  const recargarCuotasSiVisibles = async (idUtilizacion: number) => {
    if (!cuotasVisibles[idUtilizacion]) return;
    const data = await get<CuotaDetalle[]>(`/api/cartera-ordinaria/mis-creditos/${idUtilizacion}/cuotas`);
    setCuotasPorCredito(prev => ({ ...prev, [idUtilizacion]: data }));
  };

  const abrirPago = (credito: MiCredito) => {
    setPagoAbierto(credito.idUtilizacion);
    setPagoValor(credito.valorProximaCuota ? String(credito.valorProximaCuota) : '');
    setPagoPin('');
    setPagoError('');
    setPagoResult(null);
  };

  const cerrarPago = () => {
    setPagoAbierto(null);
    setPagoValor('');
    setPagoPin('');
    setPagoError('');
    setPagoResult(null);
  };

  const pagarCuota = async () => {
    if (pagoAbierto === null) return;
    const valorNum = Number(pagoValor) || 0;
    if (valorNum <= 0) { setPagoError('Ingresa un valor válido'); return; }
    if (walletSaldo !== null && valorNum > walletSaldo) {
      setPagoError(`El valor supera tu saldo de Wallet (${fmt(walletSaldo)})`);
      return;
    }
    const pinErr = validatePin(pagoPin);
    if (pinErr) { setPagoError(pinErr); return; }

    setPagoBusy(true); setPagoError(''); setPagoResult(null);
    try {
      const result = await post<PagoCuotaResult>('/api/cartera-ordinaria/pagar-cuota-wallet', {
        idUtilizacion: pagoAbierto,
        valorPago:     valorNum,
        pin:           pagoPin,
      });
      setPagoResult(result);
      setCupo(prev => prev ? {
        ...prev,
        cupoUsado:      result.cupoUsadoDespues,
        cupoDisponible: result.cupoDisponibleDespues,
      } : prev);
      setWalletSaldo(result.saldoWalletDespues);
      cargarCreditos();
      await recargarCuotasSiVisibles(pagoAbierto);
    } catch (e: unknown) {
      setPagoError(e instanceof Error ? e.message : 'Error al procesar el pago');
    } finally { setPagoBusy(false); setPagoPin(''); }
  };

  const confirmarAvanceWallet = async () => {
    if (!sim) return;
    const pinErr = validatePin(confirmPin);
    if (pinErr) { setConfirmError(pinErr); return; }
    setConfirmBusy(true); setConfirmError(''); setConfirmResult(null);
    try {
      // Usa los valores congelados de la simulación mostrada, no el estado
      // vivo de los inputs — evita confirmar un monto/plazo distinto al
      // que el usuario efectivamente vio y aprobó en pantalla.
      const result = await post<ConfirmacionResult>('/api/cartera-ordinaria/confirmar-avance-wallet', {
        tipoUtilizacion: sim.tipoUtilizacion,
        valorCapital:    sim.valorCapital,
        plazoMeses:      sim.plazoMeses,
        frecuencia:      sim.frecuencia,
      });
      setConfirmResult(result);
      setCupo(prev => prev ? {
        ...prev,
        cupoUsado:      prev.cupoAprobado - result.nuevoCupoDisponible,
        cupoDisponible: result.nuevoCupoDisponible,
      } : prev);
    } catch (e: unknown) {
      setConfirmError(e instanceof Error ? e.message : 'Error al confirmar el desembolso');
    } finally { setConfirmBusy(false); setConfirmPin(''); }
  };

  // Derivar tipo de utilización según modo
  const tipoUtilizacion = modo === 'desembolso' ? 'AVANCE_WALLET' : 'COMPRA_COMERCIO';

  const cupoDisponible = cupo ? cupo.cupoDisponible : 0;
  const montoNum       = Number(monto) || 0;
  const superaCupo     = cupo && montoNum > cupoDisponible;

  const simular = async () => {
    if (!monto || montoNum <= 0) { setSimError('Ingresa un monto válido'); return; }
    if (!cupo)                   { setSimError('No tienes cupo activo'); return; }
    if (superaCupo)              { setSimError(`El valor supera tu cupo disponible (${fmt(cupoDisponible)})`); return; }
    setSimBusy(true); setSimError(''); setSim(null); setShowCuotas(false);
    setConfirmResult(null); setConfirmError(''); setConfirmPin('');
    try {
      const result = await post<SimulacionResult>('/api/cartera-ordinaria/simular', {
        tipoUtilizacion,
        valorCapital: montoNum,
        plazoMeses:   Number(plazo),
        frecuencia,
      });
      setSim(result);
    } catch (e: unknown) {
      setSimError(e instanceof Error ? e.message : 'Error en simulación');
    } finally { setSimBusy(false); }
  };

  const cambiarModo = (nuevoModo: ModoSimulador) => {
    setModo(nuevoModo);
    setTipo(nuevoModo === 'desembolso' ? 'AVANCE_WALLET' : 'COMPRA_COMERCIO');
    setSim(null);
    setSimError('');
    setShowCuotas(false);
    setConfirmResult(null);
    setConfirmError('');
    setConfirmPin('');
    if (nuevoModo === 'compra') setMonto(qpValor);
    else setMonto('');
  };

  return (
    <div style={{ padding: '1.5rem', maxWidth: 900 }}>
      <h2 style={{ marginBottom: '0.25rem' }}>Mi Cartera Ordinaria</h2>
      <p style={{ fontSize: 13, color: '#666', margin: '0 0 1.5rem' }}>
        El simulador nunca mueve saldo ni cupo. La confirmación real de "Desembolsar a Wallet" sí acredita
        tu Wallet y descuenta tu cupo de inmediato. La confirmación de compra en comercio llegará en una próxima fase.
      </p>

      {/* Contexto QR */}
      {origenQR && qpQrCode && (
        <div style={{ padding: '0.75rem 1rem', background: '#e8f5e9', border: '1px solid #c8e6c9', borderRadius: 6, marginBottom: '1rem', fontSize: 13 }}>
          <strong>Pago desde QR:</strong> comercio <code>{qpQrCode}</code>
          {qpValor && <> · Valor: <strong>{fmt(Number(qpValor))}</strong></>}
          <span style={{ marginLeft: 8, color: '#555' }}>— Usa el simulador abajo para ver el plan en cuotas.</span>
        </div>
      )}

      {/* Panel cupo */}
      {cupoError ? (
        <div style={{ padding: '1rem', background: '#fff3e0', borderRadius: 6, marginBottom: '1.5rem', color: '#e65100' }}>
          {cupoError}
        </div>
      ) : cupo ? (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '1rem', marginBottom: '2rem' }}>
          {[
            { label: 'Cupo aprobado',   value: fmt(cupo.cupoAprobado),   color: '#1565c0' },
            { label: 'Cupo utilizado',  value: fmt(cupo.cupoUsado),      color: '#c62828' },
            { label: 'Cupo disponible', value: fmt(cupo.cupoDisponible), color: '#2e7d32' },
          ].map(c => (
            <div key={c.label} style={{
              padding: '1rem', border: '1px solid #e0e0e0', borderRadius: 8,
              background: '#fff', boxShadow: '0 1px 3px rgba(0,0,0,.08)',
            }}>
              <div style={{ fontSize: 12, color: '#666', marginBottom: 4 }}>{c.label}</div>
              <div style={{ fontSize: 22, fontWeight: 700, color: c.color }}>{c.value}</div>
            </div>
          ))}
        </div>
      ) : (
        <p style={{ color: '#888' }}>Cargando cupo…</p>
      )}

      {/* Selector de modo */}
      <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1.25rem' }}>
        <button
          onClick={() => cambiarModo('compra')}
          style={{
            padding: '0.5rem 1.25rem', cursor: 'pointer', borderRadius: 6, fontSize: 14,
            fontWeight: modo === 'compra' ? 700 : 400,
            background: modo === 'compra' ? '#1976d2' : '#f0f0f0',
            color: modo === 'compra' ? '#fff' : '#333',
            border: 'none',
          }}>
          🛒 Compra en comercio
        </button>
        <button
          onClick={() => cambiarModo('desembolso')}
          style={{
            padding: '0.5rem 1.25rem', cursor: 'pointer', borderRadius: 6, fontSize: 14,
            fontWeight: modo === 'desembolso' ? 700 : 400,
            background: modo === 'desembolso' ? '#388e3c' : '#f0f0f0',
            color: modo === 'desembolso' ? '#fff' : '#333',
            border: 'none',
          }}>
          💸 Desembolsar a mi Wallet
        </button>
      </div>

      {/* Simulador */}
      <div style={{ padding: '1.25rem', border: '1px solid #ddd', borderRadius: 8, maxWidth: 520, marginBottom: '2rem', background: '#fafafa' }}>
        <h3 style={{ marginTop: 0, fontSize: 16 }}>
          {modo === 'compra' ? 'Simular compra en comercio' : 'Simular desembolso a Wallet'}
        </h3>
        <p style={{ fontSize: 12, color: '#888', margin: '-8px 0 12px', fontStyle: 'italic' }}>
          Tipo: <strong>{tipoUtilizacion}</strong> · Simulación — sin movimiento de saldo ni cupo
        </p>

        {modo === 'desembolso' && (
          <div style={{ padding: '0.6rem 0.8rem', background: '#e8f5e9', borderRadius: 4, marginBottom: '0.75rem', fontSize: 13 }}>
            El monto se acredita completo a tu Wallet al confirmar; las cuotas (capital + interés + aval/admin/IVA) se cobran en el plazo elegido.
          </div>
        )}

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem', marginBottom: '0.75rem' }}>
          <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13 }}>
            Monto (COP)
            <input type="number" value={monto} placeholder="Ej: 500000"
              onChange={e => setMonto(e.target.value)}
              style={{
                marginTop: 4, padding: '6px 8px',
                border: `1px solid ${superaCupo ? '#e53935' : '#ccc'}`,
                borderRadius: 4,
              }} />
            {superaCupo && (
              <span style={{ fontSize: 11, color: '#e53935', marginTop: 2 }}>
                El valor supera tu cupo disponible ({fmt(cupoDisponible)})
              </span>
            )}
          </label>

          <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13 }}>
            Plazo (meses)
            <input type="number" value={plazo} min={1} max={36}
              onChange={e => setPlazo(e.target.value)}
              style={{ marginTop: 4, padding: '6px 8px', border: '1px solid #ccc', borderRadius: 4 }} />
          </label>

          <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13 }}>
            Frecuencia de pago
            <select value={frecuencia} onChange={e => setFrecuencia(e.target.value)}
              style={{ marginTop: 4, padding: '6px 8px', border: '1px solid #ccc', borderRadius: 4 }}>
              <option value="MENSUAL">Mensual</option>
              <option value="QUINCENAL">Quincenal</option>
            </select>
          </label>
        </div>

        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
          <button onClick={simular} disabled={simBusy || !!superaCupo || !cupo}
            style={{
              padding: '8px 24px',
              background: simBusy || !!superaCupo || !cupo ? '#ccc' : (modo === 'desembolso' ? '#388e3c' : '#1976d2'),
              color: '#fff', border: 'none', borderRadius: 4,
              cursor: simBusy || !!superaCupo || !cupo ? 'not-allowed' : 'pointer',
            }}>
            {simBusy ? 'Calculando…' : 'Simular'}
          </button>
        </div>

        {simError && <p style={{ color: '#c62828', marginTop: '0.5rem', fontSize: 13 }}>{simError}</p>}
      </div>

      {/* Resultado simulación */}
      {sim && (
        <div style={{
          padding: '1.25rem',
          border: `1px solid ${modo === 'desembolso' ? '#c8e6c9' : '#bbdefb'}`,
          borderRadius: 8,
          background: modo === 'desembolso' ? '#f1f8e9' : '#e3f2fd',
          maxWidth: 700,
        }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '0.75rem' }}>
            <h3 style={{ margin: 0, fontSize: 16, color: modo === 'desembolso' ? '#1b5e20' : '#0d47a1' }}>
              {confirmResult ? 'Desembolso confirmado' : 'Resultado de simulación'} — {modo === 'desembolso' ? 'Desembolso a Wallet' : 'Compra en comercio'}
            </h3>
            <span style={{
              fontSize: 11, padding: '2px 8px', borderRadius: 10,
              background: confirmResult ? '#e8f5e9' : '#fff3e0',
              color: confirmResult ? '#2e7d32' : '#e65100',
              border: `1px solid ${confirmResult ? '#a5d6a7' : '#ffe0b2'}`,
            }}>
              {confirmResult ? 'Confirmado' : 'Solo simulación'}
            </span>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '0.75rem', marginBottom: '1rem' }}>
            {[
              { label: modo === 'desembolso' ? 'Monto a recibir' : 'Valor compra', value: fmt(sim.valorCapital) },
              { label: `Cuota ${sim.frecuencia.toLowerCase()}`, value: fmt(sim.valorCuota) },
              { label: 'Total cuotas',       value: String(sim.totalCuotas) },
              { label: 'Tasa EMV',           value: fmtPct(sim.tasaEmv) },
              { label: 'Total intereses',    value: fmt(sim.valorTotalIntereses) },
              { label: 'Total aval',         value: fmt(sim.valorTotalAval) },
              { label: 'Total admin',        value: fmt(sim.valorTotalAdmin) },
              { label: 'Total IVA',          value: sim.aplicaIva ? fmt(sim.valorTotalIva) : 'No aplica' },
              { label: 'TOTAL A PAGAR',      value: fmt(sim.valorTotalPagar) },
            ].map(item => (
              <div key={item.label} style={{ background: '#fff', borderRadius: 6, padding: '0.6rem 0.8rem' }}>
                <div style={{ fontSize: 11, color: '#555', marginBottom: 2 }}>{item.label}</div>
                <div style={{ fontSize: 15, fontWeight: 600, color: modo === 'desembolso' ? '#1b5e20' : '#0d47a1' }}>{item.value}</div>
              </div>
            ))}
          </div>

          {/* Confirmación real — solo AVANCE_WALLET (desembolso) en esta fase */}
          {modo === 'desembolso' ? (
            confirmResult ? (
              <div style={{ marginBottom: '0.75rem', padding: '0.75rem', background: '#e8f5e9', borderRadius: 6, border: '1px solid #a5d6a7', fontSize: 13 }}>
                <strong>Desembolso confirmado #{confirmResult.idUtilizacion}</strong> — {fmt(confirmResult.valorCapital)} acreditado a tu Wallet.
                <br />Nuevo saldo de Wallet: <strong>{fmt(confirmResult.nuevoSaldoWallet)}</strong> · Nuevo cupo disponible: <strong>{fmt(confirmResult.nuevoCupoDisponible)}</strong>
              </div>
            ) : (
              <>
                <div style={{ marginBottom: '0.75rem', padding: '0.75rem', background: '#fff8e1', borderRadius: 6, border: '1px solid #ffe082', fontSize: 13 }}>
                  Al confirmar, {fmt(sim.valorCapital)} se acreditará de inmediato a tu Wallet y se generará el plan de cuotas para el cobro.
                </div>
                <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13, maxWidth: 200, marginBottom: '0.75rem' }}>
                  Clave de 7 dígitos
                  <span style={{ fontSize: 11, color: '#888', fontStyle: 'italic' }}> — QA/Demo: solo se valida formato, no hay backend PIN en esta fase</span>
                  <input
                    type="password"
                    inputMode="numeric"
                    maxLength={7}
                    value={confirmPin}
                    onChange={e => setConfirmPin(e.target.value.replace(/\D/g, '').slice(0, 7))}
                    placeholder="·······"
                    autoComplete="off"
                    style={{ marginTop: 4, padding: '6px 8px', border: '1px solid #ccc', borderRadius: 4 }}
                  />
                </label>
                <button onClick={confirmarAvanceWallet} disabled={confirmBusy || confirmPin.length !== 7}
                  style={{
                    padding: '8px 24px',
                    background: confirmBusy || confirmPin.length !== 7 ? '#ccc' : '#388e3c',
                    color: '#fff', border: 'none', borderRadius: 4,
                    cursor: confirmBusy || confirmPin.length !== 7 ? 'not-allowed' : 'pointer', marginBottom: '0.75rem', fontSize: 14,
                  }}>
                  {confirmBusy ? 'Desembolsando…' : 'Desembolsar a Wallet'}
                </button>
                {confirmError && <p style={{ color: '#c62828', margin: '0 0 0.75rem', fontSize: 13 }}>{confirmError}</p>}
              </>
            )
          ) : (
            <div style={{ marginBottom: '0.75rem', padding: '0.75rem', background: '#fff8e1', borderRadius: 6, border: '1px solid #ffe082', fontSize: 13 }}>
              <strong>Confirmación real:</strong> disponible en una próxima fase. El cargo al cupo y el pago al comercio se activarán cuando el flujo de confirmación esté listo.
              <br />
              <button disabled style={{ marginTop: 8, padding: '8px 24px', background: '#bdbdbd', color: '#fff', border: 'none', borderRadius: 4, cursor: 'not-allowed', fontSize: 14 }}>
                Confirmar utilización — próxima fase
              </button>
            </div>
          )}

          <br />
          <button onClick={() => setShowCuotas(v => !v)}
            style={{ background: 'none', border: `1px solid ${modo === 'desembolso' ? '#388e3c' : '#1976d2'}`, color: modo === 'desembolso' ? '#388e3c' : '#1976d2', borderRadius: 4, padding: '4px 14px', cursor: 'pointer', fontSize: 13 }}>
            {showCuotas ? 'Ocultar tabla de cuotas' : 'Ver tabla de amortización'}
          </button>

          {showCuotas && (
            <div style={{ marginTop: '1rem', overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                <thead>
                  <tr style={{ background: modo === 'desembolso' ? '#388e3c' : '#1976d2', color: '#fff' }}>
                    {['#','Vencimiento','Saldo antes','Capital','Interés','Aval','Admin','IVA','Cuota total','Saldo después'].map(h =>
                      <th key={h} style={{ padding: '6px 8px', textAlign: 'right', fontWeight: 600 }}>{h}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {sim.cuotas.map(c => (
                    <tr key={c.numeroCuota} style={{ background: c.numeroCuota % 2 === 0 ? '#f9f9f9' : '#fff' }}>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{c.numeroCuota}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{c.fechaVencimiento}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(c.saldoCapitalAntes)}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(c.valorCapital)}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(c.valorInteres)}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(c.valorAval)}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(c.valorAdmin)}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(c.valorIva)}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right', fontWeight: 600 }}>{fmt(c.valorTotal)}</td>
                      <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(c.saldoCapitalDespues)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Mis créditos de Cartera Ordinaria — pago manual de cuotas desde Wallet */}
      <div style={{ marginTop: '2.5rem', paddingTop: '1.5rem', borderTop: '2px solid #e0e0e0' }}>
        <h2 style={{ marginBottom: '0.25rem', fontSize: 18 }}>Mis créditos de Cartera Ordinaria</h2>
        <p style={{ fontSize: 13, color: '#666', margin: '0 0 1rem' }}>
          Paga tus cuotas manualmente desde tu Wallet. El cupo solo se libera por la parte aplicada a capital.
        </p>

        {creditosError && <p style={{ color: '#c62828', fontSize: 13 }}>{creditosError}</p>}

        {creditos.length === 0 && !creditosError ? (
          <p style={{ color: '#888', fontSize: 13 }}>No tienes créditos de Cartera Ordinaria todavía.</p>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            {creditos.map(c => (
              <div key={c.idUtilizacion} style={{ border: '1px solid #ddd', borderRadius: 8, padding: '1rem', background: '#fff', maxWidth: 780 }}>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '0.75rem', marginBottom: '0.75rem' }}>
                  <div><div style={{ fontSize: 11, color: '#666' }}>Crédito #</div><div style={{ fontWeight: 600 }}>{c.nroCredito}</div></div>
                  <div><div style={{ fontSize: 11, color: '#666' }}>Tipo</div><div>{c.tipoUtilizacion}</div></div>
                  <div><div style={{ fontSize: 11, color: '#666' }}>Valor desembolsado</div><div>{fmt(c.valorCapital)}</div></div>
                  <div>
                    <div style={{ fontSize: 11, color: '#666' }}>Estado</div>
                    <span style={{
                      fontSize: 11, padding: '2px 8px', borderRadius: 10,
                      background: c.estado === 'PAGADA' ? '#e8f5e9' : '#fff3e0',
                      color: c.estado === 'PAGADA' ? '#2e7d32' : '#e65100',
                    }}>{c.estado}</span>
                  </div>
                  <div><div style={{ fontSize: 11, color: '#666' }}>Saldo pendiente</div><div style={{ fontWeight: 600 }}>{fmt(c.saldoPendiente)}</div></div>
                  <div>
                    <div style={{ fontSize: 11, color: '#666' }}>Próxima cuota</div>
                    <div>{c.proximaCuota ? `#${c.proximaCuota} — ${fmt(c.valorProximaCuota ?? 0)}` : '—'}</div>
                  </div>
                </div>

                <div style={{ display: 'flex', gap: '0.5rem' }}>
                  <button onClick={() => toggleCuotas(c.idUtilizacion)}
                    style={{ background: 'none', border: '1px solid #1976d2', color: '#1976d2', borderRadius: 4, padding: '4px 12px', cursor: 'pointer', fontSize: 13 }}>
                    {cuotasVisibles[c.idUtilizacion] ? 'Ocultar cuotas' : 'Ver cuotas'}
                  </button>
                  {c.saldoPendiente > 0 && (
                    <button onClick={() => abrirPago(c)}
                      style={{ background: '#388e3c', border: 'none', color: '#fff', borderRadius: 4, padding: '4px 12px', cursor: 'pointer', fontSize: 13 }}>
                      Pagar desde Wallet
                    </button>
                  )}
                </div>

                {cuotasVisibles[c.idUtilizacion] && (
                  <div style={{ marginTop: '0.75rem', overflowX: 'auto' }}>
                    {cuotasBusy[c.idUtilizacion] ? (
                      <p style={{ fontSize: 12, color: '#888' }}>Cargando cuotas…</p>
                    ) : (
                      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                        <thead>
                          <tr style={{ background: '#f5f5f5' }}>
                            {['#', 'Vencimiento', 'Capital', 'Interés', 'Aval', 'Admin', 'IVA', 'Total', 'Saldo', 'Estado'].map(h =>
                              <th key={h} style={{ padding: '5px 8px', textAlign: 'right', borderBottom: '1px solid #ddd' }}>{h}</th>)}
                          </tr>
                        </thead>
                        <tbody>
                          {(cuotasPorCredito[c.idUtilizacion] ?? []).map(q => (
                            <tr key={q.idCuota}>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{q.numeroCuota}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{q.fechaVencimiento}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(q.valorCapital)}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(q.valorInteres)}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(q.valorAval)}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(q.valorAdmin)}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(q.valorIva)}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right', fontWeight: 600 }}>{fmt(q.valorTotal)}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{fmt(q.saldoCuota)}</td>
                              <td style={{ padding: '5px 8px', textAlign: 'right' }}>{q.estado}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    )}
                  </div>
                )}

                {pagoAbierto === c.idUtilizacion && (
                  <div style={{ marginTop: '1rem', padding: '1rem', border: '1px solid #a5d6a7', borderRadius: 6, background: '#f1f8e9', maxWidth: 420 }}>
                    {pagoResult ? (
                      <div style={{ fontSize: 13 }}>
                        <strong>Pago aplicado #{pagoResult.idPago}</strong>
                        <table style={{ width: '100%', marginTop: '0.5rem', fontSize: 12 }}>
                          <tbody>
                            <tr><td>Valor pagado</td><td style={{ textAlign: 'right' }}>{fmt(pagoResult.valorPago)}</td></tr>
                            <tr><td>Capital aplicado</td><td style={{ textAlign: 'right' }}>{fmt(pagoResult.capitalPagado)}</td></tr>
                            <tr><td>Intereses</td><td style={{ textAlign: 'right' }}>{fmt(pagoResult.interesesPagados)}</td></tr>
                            <tr><td>Aval</td><td style={{ textAlign: 'right' }}>{fmt(pagoResult.avalPagado)}</td></tr>
                            <tr><td>Administración</td><td style={{ textAlign: 'right' }}>{fmt(pagoResult.adminPagado)}</td></tr>
                            <tr><td>IVA</td><td style={{ textAlign: 'right' }}>{fmt(pagoResult.ivaPagado)}</td></tr>
                            <tr><td>Nuevo cupo disponible</td><td style={{ textAlign: 'right', fontWeight: 600 }}>{fmt(pagoResult.cupoDisponibleDespues)}</td></tr>
                            <tr><td>Nuevo saldo Wallet</td><td style={{ textAlign: 'right', fontWeight: 600 }}>{fmt(pagoResult.saldoWalletDespues)}</td></tr>
                            <tr><td>idPago</td><td style={{ textAlign: 'right' }}>{pagoResult.idPago}</td></tr>
                            <tr><td>idTransaccionLedger</td><td style={{ textAlign: 'right' }}>{pagoResult.idTransaccionLedger ?? '—'}</td></tr>
                          </tbody>
                        </table>
                        <button onClick={cerrarPago}
                          style={{ marginTop: '0.75rem', padding: '6px 16px', background: '#eee', border: 'none', borderRadius: 4, cursor: 'pointer', fontSize: 13 }}>
                          Cerrar
                        </button>
                      </div>
                    ) : (
                      <>
                        <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13, marginBottom: '0.6rem' }}>
                          Valor a pagar (COP)
                          <input type="number" value={pagoValor} onChange={e => setPagoValor(e.target.value)}
                            style={{ marginTop: 4, padding: '6px 8px', border: '1px solid #ccc', borderRadius: 4 }} />
                        </label>
                        <p style={{ fontSize: 12, color: '#555', margin: '0 0 0.6rem' }}>
                          Saldo Wallet actual: <strong>{walletSaldo !== null ? fmt(walletSaldo) : '—'}</strong>
                        </p>
                        <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13, maxWidth: 200, marginBottom: '0.6rem' }}>
                          Clave de 7 dígitos
                          <input
                            type="password"
                            inputMode="numeric"
                            maxLength={7}
                            value={pagoPin}
                            onChange={e => setPagoPin(e.target.value.replace(/\D/g, '').slice(0, 7))}
                            placeholder="·······"
                            autoComplete="off"
                            style={{ marginTop: 4, padding: '6px 8px', border: '1px solid #ccc', borderRadius: 4 }}
                          />
                        </label>
                        <div style={{ padding: '0.6rem 0.8rem', background: '#fff8e1', borderRadius: 4, marginBottom: '0.6rem', fontSize: 12 }}>
                          El pago se descontará de tu Wallet. El cupo solo se libera por la parte aplicada a capital.
                        </div>
                        <div style={{ display: 'flex', gap: '0.5rem' }}>
                          <button onClick={pagarCuota}
                            disabled={pagoBusy || !pagoValor || Number(pagoValor) <= 0 || (walletSaldo !== null && Number(pagoValor) > walletSaldo) || pagoPin.length !== 7}
                            style={{
                              padding: '8px 20px',
                              background: pagoBusy || !pagoValor || Number(pagoValor) <= 0 || (walletSaldo !== null && Number(pagoValor) > walletSaldo) || pagoPin.length !== 7 ? '#ccc' : '#388e3c',
                              color: '#fff', border: 'none', borderRadius: 4,
                              cursor: pagoBusy || !pagoValor || Number(pagoValor) <= 0 || (walletSaldo !== null && Number(pagoValor) > walletSaldo) || pagoPin.length !== 7 ? 'not-allowed' : 'pointer', fontSize: 14,
                            }}>
                            {pagoBusy ? 'Pagando…' : 'Confirmar pago'}
                          </button>
                          <button onClick={cerrarPago}
                            style={{ padding: '8px 16px', background: '#eee', border: 'none', borderRadius: 4, cursor: 'pointer', fontSize: 14 }}>
                            Cancelar
                          </button>
                        </div>
                        {pagoError && <p style={{ color: '#c62828', marginTop: '0.5rem', fontSize: 13 }}>{pagoError}</p>}
                      </>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
