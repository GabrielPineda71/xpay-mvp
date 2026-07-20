import { useEffect, useState, useCallback } from 'react';
import { get, post } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

interface RecaudoPendiente {
  idRecarga:           number;
  idComercio:           number;
  nombreComercio:       string | null;
  idComercioAliado:     number | null;
  idTienda:             number | null;
  nombreTienda:         string | null;
  idUsuarioCajero:      number;
  nombreUsuarioCajero:  string | null;
  idUsuarioWallet:      number;
  nombreUsuarioWallet:  string | null;
  idWallet:             number;
  valor:                number;
  estado:               string;
  fechaRecarga:         string;
  observaciones:        string | null;
}

interface ResumenPendiente {
  idComercio:           number;
  nombreComercio:       string | null;
  idTienda:             number | null;
  nombreTienda:         string | null;
  cantidadRecargas:     number;
  valorTotalPendiente:  number;
}

interface LiquidarResultado {
  idLiquidacion:        number;
  idTransaccionLedger:  number | null;
  idComercio:           number;
  idTienda:             number | null;
  metodoLiquidacion:    string;
  valorTotal:           number;
  cantidadRecargas:     number;
  estado:               string;
  idUsuarioAdmin:       number;
  fechaLiquidacion:     string;
  comprobanteTexto:     string;
}

interface PendientesResp   { success: boolean; data: RecaudoPendiente[]; }
interface ResumenResp      { success: boolean; data: ResumenPendiente[]; }
interface LiquidarResp     { success: boolean; data: LiquidarResultado; }

const METODOS = [
  { value: 'EFECTIVO_BOVEDA',    label: 'Efectivo en Bóveda (DR 110101)' },
  { value: 'CONSIGNACION_BANCO', label: 'Consignación Bancaria (DR 110102)' },
];

export function AdminWalletRecaudosComercioPage() {
  const [fechaDesde, setFechaDesde]   = useState('');
  const [fechaHasta, setFechaHasta]   = useState('');
  const [idComercio, setIdComercio]   = useState('');
  const [idTienda, setIdTienda]       = useState('');
  const [idCajero, setIdCajero]       = useState('');

  const [resumen, setResumen]         = useState<ResumenPendiente[]>([]);
  const [pendientes, setPendientes]   = useState<RecaudoPendiente[]>([]);
  const [seleccion, setSeleccion]     = useState<Set<number>>(new Set());
  const [loading, setLoading]         = useState(false);
  const [error, setError]             = useState('');

  const [metodo, setMetodo]           = useState('EFECTIVO_BOVEDA');
  const [referencia, setReferencia]   = useState('');
  const [observaciones, setObservaciones] = useState('');
  const [liquidando, setLiquidando]   = useState(false);
  const [resultado, setResultado]     = useState<LiquidarResultado | null>(null);

  const buildQuery = useCallback(() => {
    const params = new URLSearchParams();
    if (fechaDesde) params.set('fechaDesde', fechaDesde);
    if (fechaHasta) params.set('fechaHasta', fechaHasta);
    if (idComercio) params.set('idComercio', idComercio);
    if (idTienda)   params.set('idTienda', idTienda);
    if (idCajero)   params.set('idUsuarioCajero', idCajero);
    return params.toString();
  }, [fechaDesde, fechaHasta, idComercio, idTienda, idCajero]);

  const cargar = useCallback(() => {
    setLoading(true);
    setError('');
    const qs = buildQuery();
    Promise.all([
      get<ResumenResp>(`/api/admin/wallet-recaudos-comercio/resumen-pendientes?${qs}`),
      get<PendientesResp>(`/api/admin/wallet-recaudos-comercio/pendientes?${qs}`),
    ])
      .then(([r, p]) => {
        setResumen(r.data);
        setPendientes(p.data);
        setSeleccion(prev => new Set([...prev].filter(id => p.data.some(x => x.idRecarga === id))));
      })
      .catch(err => { setResumen([]); setPendientes([]); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [buildQuery]);

  useEffect(cargar, [cargar]);

  function toggleSeleccion(idRecarga: number) {
    setSeleccion(prev => {
      const next = new Set(prev);
      if (next.has(idRecarga)) next.delete(idRecarga); else next.add(idRecarga);
      return next;
    });
  }

  function toggleSeleccionTodas() {
    setSeleccion(prev =>
      prev.size === pendientes.length ? new Set() : new Set(pendientes.map(p => p.idRecarga)));
  }

  const seleccionadas = pendientes.filter(p => seleccion.has(p.idRecarga));
  const valorSeleccionado = seleccionadas.reduce((sum, p) => sum + p.valor, 0);

  async function liquidar() {
    if (seleccion.size === 0) return;
    const metodoLabel = METODOS.find(m => m.value === metodo)?.label ?? metodo;
    if (!window.confirm(
      `¿Liquidar ${seleccion.size} recarga(s) por ${fmtMoney(valorSeleccionado)}?\n\nMétodo: ${metodoLabel}`
    )) return;

    setLiquidando(true);
    setError('');
    setResultado(null);
    try {
      const r = await post<LiquidarResp>('/api/admin/wallet-recaudos-comercio/liquidar', {
        idsRecarga: [...seleccion],
        metodoLiquidacion: metodo,
        referenciaExterna: referencia.trim() || null,
        observaciones: observaciones.trim() || null,
      });
      setResultado(r.data);
      setSeleccion(new Set());
      setReferencia('');
      setObservaciones('');
      cargar();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLiquidando(false);
    }
  }

  return (
    <div className="page">
      <h2>Liquidación Recaudos Comercio</h2>
      <p style={{ color: '#718096', marginBottom: '1.5rem', fontSize: '0.9rem' }}>
        QA · registra el efectivo/consignación que XPAY recibe de un comercio por recargas ya
        aplicadas · no modifica saldo de Wallet · sin producción
      </p>

      {error && <div className="error-msg" style={{ marginBottom: '1rem' }}>Error: {error}</div>}

      {resultado && (
        <div style={{
          background: '#f0fff4', color: '#276749', padding: '1rem', borderRadius: '6px',
          borderLeft: '3px solid #48bb78', marginBottom: '1.5rem',
        }}>
          <strong>Liquidación #{resultado.idLiquidacion} aplicada.</strong>
          <div style={{ marginTop: '0.4rem', fontSize: '0.88rem' }}>
            {resultado.comprobanteTexto}
          </div>
          <div style={{ marginTop: '0.4rem', fontSize: '0.82rem', color: '#2f855a' }}>
            Transacción ledger #{resultado.idTransaccionLedger ?? '—'} · {resultado.cantidadRecargas} recarga(s) ·
            {' '}{fmtMoney(resultado.valorTotal)}
          </div>
        </div>
      )}

      {/* Filtros */}
      <div style={{
        display: 'flex', gap: '1rem', flexWrap: 'wrap', alignItems: 'flex-end',
        marginBottom: '1.25rem', padding: '0.75rem 1rem',
        background: '#f7fafc', border: '1px solid #e2e8f0', borderRadius: '8px',
      }}>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Desde
          <input type="date" value={fechaDesde} onChange={e => setFechaDesde(e.target.value)} style={{ maxWidth: '160px' }} />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Hasta
          <input type="date" value={fechaHasta} onChange={e => setFechaHasta(e.target.value)} style={{ maxWidth: '160px' }} />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Comercio
          <input type="number" value={idComercio} onChange={e => setIdComercio(e.target.value)} placeholder="idComercio" style={{ maxWidth: '110px' }} />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Sede
          <input type="number" value={idTienda} onChange={e => setIdTienda(e.target.value)} placeholder="idTienda" style={{ maxWidth: '110px' }} />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
          Cajero
          <input type="number" value={idCajero} onChange={e => setIdCajero(e.target.value)} placeholder="idUsuario" style={{ maxWidth: '110px' }} />
        </label>
        <button className="btn-secondary" onClick={cargar}>Actualizar</button>
      </div>

      {loading && <div className="loading">Cargando...</div>}

      {!loading && (
        <>
          <h3 style={{ marginBottom: '0.75rem' }}>Resumen por comercio / sede</h3>
          {resumen.length === 0 ? (
            <div className="empty">No hay recaudos pendientes con los filtros actuales.</div>
          ) : (
            <div className="table-wrapper" style={{ marginBottom: '1.5rem' }}>
              <table>
                <thead>
                  <tr>
                    <th>Comercio</th>
                    <th>Sede</th>
                    <th>Recargas pendientes</th>
                    <th>Valor pendiente</th>
                  </tr>
                </thead>
                <tbody>
                  {resumen.map((r, i) => (
                    <tr key={i}>
                      <td>{r.nombreComercio ?? `comercio #${r.idComercio}`}</td>
                      <td>{r.idTienda ? (r.nombreTienda ?? `sede #${r.idTienda}`) : 'todas las sedes'}</td>
                      <td className="mono">{r.cantidadRecargas}</td>
                      <td style={{ fontWeight: 600 }}>{fmtMoney(r.valorTotalPendiente)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <h3 style={{ marginBottom: '0.75rem' }}>Recargas pendientes de liquidar</h3>
          {pendientes.length === 0 ? (
            <div className="empty">No hay recargas pendientes de liquidar.</div>
          ) : (
            <>
              <div className="table-wrapper" style={{ marginBottom: '1rem' }}>
                <table>
                  <thead>
                    <tr>
                      <th>
                        <input type="checkbox"
                          checked={pendientes.length > 0 && seleccion.size === pendientes.length}
                          onChange={toggleSeleccionTodas} />
                      </th>
                      <th>ID</th>
                      <th>Comercio</th>
                      <th>Sede</th>
                      <th>Cajero</th>
                      <th>Usuario wallet</th>
                      <th>Valor</th>
                      <th>Fecha</th>
                    </tr>
                  </thead>
                  <tbody>
                    {pendientes.map(p => (
                      <tr key={p.idRecarga}>
                        <td>
                          <input type="checkbox" checked={seleccion.has(p.idRecarga)}
                            onChange={() => toggleSeleccion(p.idRecarga)} />
                        </td>
                        <td className="mono">{p.idRecarga}</td>
                        <td>{p.nombreComercio ?? `comercio #${p.idComercio}`}</td>
                        <td>{p.idTienda ? (p.nombreTienda ?? `sede #${p.idTienda}`) : '—'}</td>
                        <td>{p.nombreUsuarioCajero ?? `usuario #${p.idUsuarioCajero}`}</td>
                        <td>{p.nombreUsuarioWallet ?? `usuario #${p.idUsuarioWallet}`}</td>
                        <td style={{ fontWeight: 600 }}>{fmtMoney(p.valor)}</td>
                        <td className="mono">{fmtDate(p.fechaRecarga)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div style={{
                display: 'flex', gap: '1rem', flexWrap: 'wrap', alignItems: 'flex-end',
                padding: '1rem', background: '#f7fafc', border: '1px solid #e2e8f0', borderRadius: '8px',
              }}>
                <div style={{ fontSize: '0.9rem', fontWeight: 600 }}>
                  {seleccion.size} seleccionada(s) · {fmtMoney(valorSeleccionado)}
                </div>
                <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
                  Método
                  <select value={metodo} onChange={e => setMetodo(e.target.value)}>
                    {METODOS.map(m => <option key={m.value} value={m.value}>{m.label}</option>)}
                  </select>
                </label>
                <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
                  Referencia externa (opcional)
                  <input type="text" value={referencia} onChange={e => setReferencia(e.target.value)}
                    placeholder="ej. consignación #123" style={{ minWidth: '180px' }} />
                </label>
                <label style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem', fontSize: '0.82rem' }}>
                  Observaciones (opcional)
                  <input type="text" value={observaciones} onChange={e => setObservaciones(e.target.value)}
                    style={{ minWidth: '180px' }} />
                </label>
                <button className="btn-primary" disabled={seleccion.size === 0 || liquidando} onClick={() => void liquidar()}>
                  {liquidando ? 'Liquidando...' : 'Liquidar seleccionadas'}
                </button>
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
