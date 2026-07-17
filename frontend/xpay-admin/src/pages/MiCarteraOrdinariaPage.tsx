import { useState, useEffect } from 'react';
import { get, post } from '../api/client.ts';

interface MiCupo {
  idCupo: number;
  cupoAprobado: number;
  cupoUsado: number;
  cupoDisponible: number;
  estado: string;
  fechaAprobacion: string;
  fechaVencimiento: string | null;
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

const fmt = (v: number) => new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(v);
const fmtPct = (v: number) => `${v}%`;

export function MiCarteraOrdinariaPage() {
  const [cupo, setCupo] = useState<MiCupo | null>(null);
  const [cupoError, setCupoError] = useState('');
  const [sim, setSim] = useState<SimulacionResult | null>(null);
  const [simBusy, setSimBusy] = useState(false);
  const [simError, setSimError] = useState('');
  const [showCuotas, setShowCuotas] = useState(false);

  const [tipo, setTipo] = useState('COMPRA_COMERCIO');
  const [monto, setMonto] = useState('');
  const [plazo, setPlazo] = useState('12');
  const [frecuencia, setFrecuencia] = useState('MENSUAL');

  useEffect(() => {
    get<MiCupo>('/api/cartera-ordinaria/mi-cupo')
      .then(data => setCupo(data))
      .catch(() => setCupoError('No tienes un cupo ordinario activo en este momento.'));
  }, []);

  const simular = async () => {
    if (!monto || Number(monto) <= 0) { setSimError('Ingresa un monto válido'); return; }
    setSimBusy(true); setSimError(''); setSim(null); setShowCuotas(false);
    try {
      const result = await post<SimulacionResult>('/api/cartera-ordinaria/simular', {
        tipoUtilizacion: tipo,
        valorCapital:    Number(monto),
        plazoMeses:      Number(plazo),
        frecuencia,
      });
      setSim(result);
    } catch (e: unknown) {
      setSimError(e instanceof Error ? e.message : 'Error en simulación');
    } finally { setSimBusy(false); }
  };

  return (
    <div style={{ padding: '1.5rem', maxWidth: 900 }}>
      <h2>Mi Cartera Ordinaria</h2>

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
              background: '#fff', boxShadow: '0 1px 3px rgba(0,0,0,.08)'
            }}>
              <div style={{ fontSize: 12, color: '#666', marginBottom: 4 }}>{c.label}</div>
              <div style={{ fontSize: 22, fontWeight: 700, color: c.color }}>{c.value}</div>
            </div>
          ))}
        </div>
      ) : (
        <p style={{ color: '#888' }}>Cargando cupo…</p>
      )}

      <div style={{ padding: '1.25rem', border: '1px solid #ddd', borderRadius: 8, maxWidth: 520, marginBottom: '2rem' }}>
        <h3 style={{ marginTop: 0, fontSize: 16 }}>Simulador de amortización</h3>
        <p style={{ fontSize: 13, color: '#666', marginTop: -8 }}>
          La simulación no genera compromisos ni afecta tu cupo.
        </p>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem', marginBottom: '0.75rem' }}>
          <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13 }}>
            Tipo de utilización
            <select value={tipo} onChange={e => setTipo(e.target.value)}
              style={{ marginTop: 4, padding: '6px 8px', border: '1px solid #ccc', borderRadius: 4 }}>
              <option value="COMPRA_COMERCIO">Compra en comercio</option>
              <option value="AVANCE_WALLET">Avance a wallet</option>
            </select>
          </label>

          <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13 }}>
            Monto (COP)
            <input type="number" value={monto} placeholder="Ej: 500000"
              onChange={e => setMonto(e.target.value)}
              style={{ marginTop: 4, padding: '6px 8px', border: '1px solid #ccc', borderRadius: 4 }} />
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

        <button onClick={simular} disabled={simBusy}
          style={{ padding: '8px 24px', background: '#1976d2', color: '#fff', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
          {simBusy ? 'Calculando…' : 'Simular'}
        </button>

        {simError && <p style={{ color: '#c62828', marginTop: '0.5rem', fontSize: 13 }}>{simError}</p>}
      </div>

      {sim && (
        <div style={{ padding: '1.25rem', border: '1px solid #bbdefb', borderRadius: 8, background: '#e3f2fd', maxWidth: 700 }}>
          <h3 style={{ marginTop: 0, fontSize: 16, color: '#0d47a1' }}>Resultado de simulación</h3>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '0.75rem', marginBottom: '1rem' }}>
            {[
              { label: 'Valor a recibir',    value: fmt(sim.valorCapital) },
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
                <div style={{ fontSize: 15, fontWeight: 600, color: '#0d47a1' }}>{item.value}</div>
              </div>
            ))}
          </div>

          <button onClick={() => setShowCuotas(v => !v)}
            style={{ background: 'none', border: '1px solid #1976d2', color: '#1976d2', borderRadius: 4, padding: '4px 14px', cursor: 'pointer', fontSize: 13 }}>
            {showCuotas ? 'Ocultar tabla de cuotas' : 'Ver tabla de amortización'}
          </button>

          {showCuotas && (
            <div style={{ marginTop: '1rem', overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                <thead>
                  <tr style={{ background: '#1976d2', color: '#fff' }}>
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
    </div>
  );
}
