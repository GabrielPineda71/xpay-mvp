import { useEffect, useState } from 'react';
import { get, post, put } from '../api/client.ts';
import { fmtMoney } from '../utils.ts';

interface Parametro {
  idParametro:         number;
  idComercioAliado:    number | null;
  diasFaltantes:       number;
  porcentajeDescuento: number;
  aplicaIva:           boolean;
  porcentajeIva:       number;
  estado:              string;
}

interface ComercioAliadoOption {
  idComercioAliado: number;
  nombreComercial:  string;
}

interface LiquidacionProcesada {
  idDisponibilidad:    number;
  idVentaQr:           number;
  valorBruto:          number;
  valorDescuento:      number;
  valorNeto:           number;
  idTransaccionLedger: number;
}

interface LiquidacionError {
  idDisponibilidad: number;
  idVentaQr:        number;
  mensaje:          string;
}

interface LiquidacionResult {
  cantidadProcesadas:  number;
  totalBruto:          number;
  totalNetoLiberado:   number;
  totalDescuento:      number;
  idsVentasLiquidadas: number[];
  procesadas:          LiquidacionProcesada[];
  errores:             LiquidacionError[];
  fechaCorteUsada:     string;
}

type Msg = { ok: boolean; text: string };

// Opciones de selección de scope de parámetros
const SCOPE_GLOBAL  = 'global';
const SCOPE_CA      = 'ca';

// Hardcode del Comercio Aliado Demo QA hasta que haya endpoint de listado genérico
const COMERCIOS_ALIADOS: ComercioAliadoOption[] = [
  { idComercioAliado: 1, nombreComercial: 'Comercio Aliado Demo XPAY (QA)' },
];

export function ParametrosLiquidacionPage() {
  const [scope,    setScope]    = useState<typeof SCOPE_GLOBAL | typeof SCOPE_CA>(SCOPE_CA);
  const [caId,     setCaId]     = useState<number>(1);

  const [params,   setParams]   = useState<Parametro[]>([]);
  const [loading,  setLoading]  = useState(true);
  const [err,      setErr]      = useState<string | null>(null);
  const [editing,  setEditing]  = useState<Record<number, { pct?: string; aplIva?: boolean; pctIva?: string }>>({});
  const [saving,   setSaving]   = useState<number | null>(null);
  const [saveMsg,  setSaveMsg]  = useState<Record<number, Msg>>({});

  const [liqFechaCorte,  setLiqFechaCorte]  = useState('');
  const [liqComercioId,  setLiqComercioId]  = useState('');
  const [liqDispId,      setLiqDispId]      = useState('');
  const [liqBusy,        setLiqBusy]        = useState(false);
  const [liqMsg,         setLiqMsg]         = useState<Msg | null>(null);
  const [liqResult,      setLiqResult]      = useState<LiquidacionResult | null>(null);

  useEffect(() => { void loadParams(); }, [scope, caId]);

  async function loadParams() {
    setLoading(true); setErr(null);
    try {
      const url = scope === SCOPE_GLOBAL
        ? '/api/comercios-aliados/admin/parametros-liquidacion'
        : `/api/comercios-aliados/admin/parametros-liquidacion?idComercioAliado=${caId}`;
      const r = await get<{ success: boolean; data: Parametro[] }>(url);
      setParams(r.data ?? []);
      setEditing({});
    } catch (e) {
      setErr((e as Error).message);
    } finally { setLoading(false); }
  }

  async function handleSaveParam(p: Parametro) {
    const edit = editing[p.idParametro];
    if (!edit) return;

    const pct    = edit.pct    !== undefined ? Number(edit.pct)    : p.porcentajeDescuento;
    const aplIva = edit.aplIva !== undefined ? edit.aplIva         : p.aplicaIva;
    const pctIva = edit.pctIva !== undefined ? Number(edit.pctIva) : p.porcentajeIva;

    if (isNaN(pct) || pct < 0 || pct > 100) {
      setSaveMsg(m => ({ ...m, [p.idParametro]: { ok: false, text: 'Descuento 0–100.' } }));
      return;
    }
    if (aplIva && (isNaN(pctIva) || pctIva <= 0 || pctIva > 100)) {
      setSaveMsg(m => ({ ...m, [p.idParametro]: { ok: false, text: 'IVA debe ser > 0 y ≤ 100.' } }));
      return;
    }

    setSaving(p.idParametro);
    try {
      await put(`/api/comercios-aliados/admin/parametros-liquidacion/${p.idParametro}`,
        { porcentajeDescuento: pct, aplicaIva: aplIva, porcentajeIva: aplIva ? pctIva : 0 });
      setParams(ps => ps.map(x =>
        x.idParametro === p.idParametro
          ? { ...x, porcentajeDescuento: pct, aplicaIva: aplIva, porcentajeIva: aplIva ? pctIva : 0 }
          : x));
      setEditing(e => { const c = { ...e }; delete c[p.idParametro]; return c; });
      setSaveMsg(m => ({ ...m, [p.idParametro]: { ok: true, text: '✓' } }));
      setTimeout(() => setSaveMsg(m => { const c = { ...m }; delete c[p.idParametro]; return c; }), 2000);
    } catch (e) {
      setSaveMsg(m => ({ ...m, [p.idParametro]: { ok: false, text: (e as Error).message } }));
    } finally { setSaving(null); }
  }

  async function handleEjecutarLiquidacion() {
    setLiqBusy(true); setLiqMsg(null); setLiqResult(null);
    try {
      const body: Record<string, unknown> = {};
      if (liqFechaCorte) body.fechaCorte = liqFechaCorte + 'T23:59:59';
      if (liqComercioId) body.soloComercioAliadoId = Number(liqComercioId);
      if (liqDispId)     body.soloIdDisponibilidad  = Number(liqDispId);
      const r = await post<{ success: boolean; data: LiquidacionResult; message?: string }>(
        '/api/comercios-aliados/admin/liquidacion-automatica/ejecutar', body);
      if (r.success) {
        setLiqResult(r.data);
        const ok  = r.data.cantidadProcesadas;
        const err = r.data.errores.length;
        setLiqMsg({
          ok:   err === 0,
          text: `${ok} ventas procesadas${err > 0 ? ` · ${err} error(es)` : ''}`,
        });
      } else {
        setLiqMsg({ ok: false, text: r.message ?? 'Error en liquidación.' });
      }
    } catch (e) {
      setLiqMsg({ ok: false, text: (e as Error).message });
    } finally { setLiqBusy(false); }
  }

  function editVal(p: Parametro, field: 'pct' | 'aplIva' | 'pctIva') {
    const ed = editing[p.idParametro];
    if (field === 'pct')    return ed?.pct    !== undefined ? ed.pct    : String(p.porcentajeDescuento);
    if (field === 'aplIva') return ed?.aplIva !== undefined ? ed.aplIva : p.aplicaIva;
    if (field === 'pctIva') return ed?.pctIva !== undefined ? ed.pctIva : String(p.porcentajeIva);
    return '';
  }

  function setEdit(idParam: number, field: 'pct' | 'aplIva' | 'pctIva', val: string | boolean) {
    setEditing(prev => ({
      ...prev,
      [idParam]: { ...(prev[idParam] ?? {}), [field]: val },
    }));
  }

  function isDirty(p: Parametro) {
    const ed = editing[p.idParametro];
    if (!ed) return false;
    const pct    = ed.pct    !== undefined ? Number(ed.pct)    : p.porcentajeDescuento;
    const aplIva = ed.aplIva !== undefined ? ed.aplIva         : p.aplicaIva;
    const pctIva = ed.pctIva !== undefined ? Number(ed.pctIva) : p.porcentajeIva;
    return pct !== p.porcentajeDescuento || aplIva !== p.aplicaIva || pctIva !== p.porcentajeIva;
  }

  return (
    <div className="page">
      <h2>Parámetros Liquidación</h2>
      <p className="tab-hint">
        Tasas de descuento para liquidación <strong>anticipada</strong> (días 0–60 faltantes).
        Aplica solo cuando el comercio decide liquidar antes de la fecha pactada.
        El descuento anticipado y el convenio se calculan sobre el <strong>valor bruto</strong>.
        El IVA se calcula sobre el valor de cada descuento.
      </p>
      <div style={{
        background: '#fffbeb', border: '1px solid #f6e05e',
        borderRadius: '6px', padding: '0.75rem 1rem', marginBottom: '1.5rem', fontSize: '0.84rem',
      }}>
        Primero se aplica el descuento normal del convenio del comercio,
        luego el descuento anticipado, y el IVA se calcula según la parametrización de cada regla.
        Los cambios aquí <strong>no</strong> afectan ventas ya liquidadas.
      </div>

      {/* ── Selector de scope ── */}
      <div style={{
        display: 'flex', gap: '1rem', alignItems: 'center',
        marginBottom: '1.25rem', padding: '0.75rem 1rem',
        background: '#f7fafc', border: '1px solid #e2e8f0', borderRadius: '8px',
      }}>
        <span style={{ fontSize: '0.85rem', fontWeight: 600, color: '#4a5568' }}>Ver parámetros de:</span>
        <label style={{ display: 'flex', alignItems: 'center', gap: '0.35rem', fontSize: '0.84rem', cursor: 'pointer' }}>
          <input type="radio" value={SCOPE_CA} checked={scope === SCOPE_CA}
            onChange={() => setScope(SCOPE_CA)} />
          Comercio aliado
        </label>
        <label style={{ display: 'flex', alignItems: 'center', gap: '0.35rem', fontSize: '0.84rem', cursor: 'pointer' }}>
          <input type="radio" value={SCOPE_GLOBAL} checked={scope === SCOPE_GLOBAL}
            onChange={() => setScope(SCOPE_GLOBAL)} />
          Global XPAY (default)
        </label>
        {scope === SCOPE_CA && (
          <select value={caId} onChange={e => setCaId(Number(e.target.value))}
            style={{ fontSize: '0.84rem', padding: '0.2rem 0.4rem' }}>
            {COMERCIOS_ALIADOS.map(ca => (
              <option key={ca.idComercioAliado} value={ca.idComercioAliado}>
                #{ca.idComercioAliado} — {ca.nombreComercial}
              </option>
            ))}
          </select>
        )}
      </div>

      {/* ── Tabla de parámetros ── */}
      {loading ? (
        <div className="loading">Cargando parámetros...</div>
      ) : err ? (
        <div className="error-msg">{err}</div>
      ) : (
        <div className="table-wrapper" style={{ marginBottom: '2rem' }}>
          <div className="table-title">
            Parámetros descuento anticipado — {scope === SCOPE_CA
              ? `Comercio Aliado #${caId}`
              : 'Global XPAY (default)'}
            {' '}({params.length} entradas)
          </div>
          <div style={{ overflowX: 'auto' }}>
            <table>
              <thead>
                <tr>
                  <th>Días falt.</th>
                  <th>Desc. anticipado (%)</th>
                  <th>Aplica IVA</th>
                  <th>IVA (%)</th>
                  <th>Estado</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {params.map(p => {
                  const aplIvaVal = editVal(p, 'aplIva') as boolean;
                  const msg = saveMsg[p.idParametro];
                  return (
                    <tr key={p.idParametro}>
                      <td className="mono">{p.diasFaltantes}</td>
                      <td>
                        <input
                          type="number" min={0} max={100} step={0.01}
                          value={editVal(p, 'pct') as string}
                          onChange={e => setEdit(p.idParametro, 'pct', e.target.value)}
                          style={{ width: '80px', padding: '0.18rem 0.35rem' }}
                        />
                      </td>
                      <td>
                        <input
                          type="checkbox"
                          checked={aplIvaVal}
                          onChange={e => setEdit(p.idParametro, 'aplIva', e.target.checked)}
                          style={{ cursor: 'pointer' }}
                        />
                      </td>
                      <td>
                        <input
                          type="number" min={0} max={100} step={0.01}
                          value={editVal(p, 'pctIva') as string}
                          disabled={!aplIvaVal}
                          onChange={e => setEdit(p.idParametro, 'pctIva', e.target.value)}
                          style={{ width: '72px', padding: '0.18rem 0.35rem',
                            opacity: aplIvaVal ? 1 : 0.4 }}
                        />
                      </td>
                      <td>
                        <span className={`badge ${p.estado === 'ACTIVO' ? 'badge-ok' : 'badge-warn'}`}>
                          {p.estado}
                        </span>
                      </td>
                      <td>
                        {isDirty(p) && (
                          <button
                            className="btn-confirm"
                            style={{ fontSize: '0.78rem', padding: '0.2rem 0.6rem' }}
                            disabled={saving === p.idParametro}
                            onClick={() => void handleSaveParam(p)}
                          >
                            {saving === p.idParametro ? '...' : 'Guardar'}
                          </button>
                        )}
                        {msg && (
                          <span
                            className={msg.ok ? 'breb-msg-ok' : 'breb-msg-err'}
                            style={{ marginLeft: '0.4rem', fontSize: '0.8rem' }}
                          >
                            {msg.text}
                          </span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── Ejecutar liquidación automática ── */}
      <div style={{
        marginBottom: '2rem', background: '#f7fafc',
        border: '1px solid #e2e8f0', borderRadius: '8px', padding: '1rem',
      }}>
        <h3 style={{ margin: '0 0 0.4rem', fontSize: '0.95rem' }}>
          Liquidación automática — ventas vencidas
        </h3>
        <p style={{ fontSize: '0.83rem', color: '#4a5568', margin: '0 0 0.75rem' }}>
          Procesa ventas con <code>estado = NO_DISPONIBLE</code> y{' '}
          <code>fecha_disponible_programada ≤ fecha_corte</code>.
          Sin descuento anticipado · tipo <code>AUTOMATICA</code>.
        </p>
        <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '0.2rem', fontSize: '0.84rem' }}>
            Fecha de corte (opcional — default: ahora)
            <input type="date" value={liqFechaCorte}
              onChange={e => setLiqFechaCorte(e.target.value)} style={{ maxWidth: '180px' }} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '0.2rem', fontSize: '0.84rem' }}>
            Solo comercio aliado ID
            <input type="number" value={liqComercioId}
              onChange={e => setLiqComercioId(e.target.value)}
              placeholder="Todos" style={{ maxWidth: '130px' }} />
          </label>
          <label style={{ display: 'flex', flexDirection: 'column', gap: '0.2rem', fontSize: '0.84rem' }}>
            Solo id_disponibilidad (prueba unitaria)
            <input type="number" value={liqDispId}
              onChange={e => setLiqDispId(e.target.value)}
              placeholder="Todas" style={{ maxWidth: '150px' }} />
          </label>
          <button className="btn-confirm" onClick={() => void handleEjecutarLiquidacion()} disabled={liqBusy}>
            {liqBusy ? 'Ejecutando...' : 'Ejecutar liquidación'}
          </button>
        </div>

        {liqMsg && (
          <div className={liqMsg.ok ? 'success-msg' : 'error-msg'} style={{ marginTop: '0.5rem' }}>
            {liqMsg.text}
          </div>
        )}

        {liqResult && (
          <div style={{ marginTop: '0.75rem' }}>
            <div style={{
              fontSize: '0.83rem', background: '#edf2f7',
              borderRadius: '6px', padding: '0.6rem 0.75rem', marginBottom: '0.5rem',
            }}>
              <strong>Resumen</strong> · {liqResult.cantidadProcesadas} ventas ·
              Bruto total: <strong>{fmtMoney(liqResult.totalBruto)}</strong> ·
              Descuento: {fmtMoney(liqResult.totalDescuento)} ·
              Neto liberado: <strong>{fmtMoney(liqResult.totalNetoLiberado)}</strong> ·
              Corte: {liqResult.fechaCorteUsada}
            </div>

            {liqResult.procesadas.length > 0 && (
              <div className="table-wrapper" style={{ marginBottom: '0.5rem' }}>
                <div className="table-title" style={{ fontSize: '0.82rem' }}>
                  Ventas procesadas ({liqResult.procesadas.length})
                </div>
                <table style={{ fontSize: '0.8rem' }}>
                  <thead>
                    <tr>
                      <th>#Venta</th><th>#Disp</th><th>Bruto</th>
                      <th>Descuento</th><th>Neto</th><th>#Ledger</th>
                    </tr>
                  </thead>
                  <tbody>
                    {liqResult.procesadas.map(p => (
                      <tr key={p.idDisponibilidad}>
                        <td className="mono">{p.idVentaQr}</td>
                        <td className="mono">{p.idDisponibilidad}</td>
                        <td>{fmtMoney(p.valorBruto)}</td>
                        <td>{fmtMoney(p.valorDescuento)}</td>
                        <td className="credit">{fmtMoney(p.valorNeto)}</td>
                        <td className="mono">{p.idTransaccionLedger}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {liqResult.errores.length > 0 && (
              <div style={{
                background: '#fff5f5', border: '1px solid #fc8181',
                borderRadius: '6px', padding: '0.6rem 0.75rem',
              }}>
                <div style={{ fontWeight: 600, color: '#c53030', fontSize: '0.83rem', marginBottom: '0.4rem' }}>
                  {liqResult.errores.length} error(es)
                </div>
                <table style={{ fontSize: '0.78rem', width: '100%' }}>
                  <thead>
                    <tr>
                      <th style={{ textAlign: 'left', padding: '0.15rem 0.4rem' }}>#Venta</th>
                      <th style={{ textAlign: 'left', padding: '0.15rem 0.4rem' }}>#Disp</th>
                      <th style={{ textAlign: 'left', padding: '0.15rem 0.4rem' }}>Error</th>
                    </tr>
                  </thead>
                  <tbody>
                    {liqResult.errores.map(e => (
                      <tr key={e.idDisponibilidad} style={{ verticalAlign: 'top' }}>
                        <td style={{ padding: '0.15rem 0.4rem' }} className="mono">{e.idVentaQr}</td>
                        <td style={{ padding: '0.15rem 0.4rem' }} className="mono">{e.idDisponibilidad}</td>
                        <td style={{ padding: '0.15rem 0.4rem', color: '#c53030', wordBreak: 'break-word' }}>
                          {e.mensaje}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        <p style={{ fontSize: '0.78rem', color: '#a0aec0', margin: '0.75rem 0 0' }}>
          Programación diaria automática: Azure Logic App →
          POST /api/comercios-aliados/admin/liquidacion-automatica/ejecutar ·
          6:00 AM Colombia (11:00 UTC) · ver docs/COMERCIO_LIQUIDACION_AUTOMATICA.md
        </p>
      </div>
    </div>
  );
}
