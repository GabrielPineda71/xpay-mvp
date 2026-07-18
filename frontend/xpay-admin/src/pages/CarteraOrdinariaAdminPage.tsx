import { useState, useEffect, useCallback } from 'react';
import { get, post, put } from '../api/client.ts';

interface ParametroUtilizacion {
  idParametro: number;
  tipoUtilizacion: string;
  tasaEmv: number;
  porcAval: number;
  porcAdmin: number;
  aplicaIva: boolean;
  porcIva: number;
  plazoMin: number;
  plazoMax: number;
  frecuencia: string;
  montoMin: number;
  montoMax: number;
  estado: string;
}

interface GastoCobranza {
  idGasto: number;
  diasDesde: number;
  diasHasta: number | null;
  tipoCobro: string;
  valorCobro: number;
  descripcion: string | null;
  estado: string;
}

interface PoliticaCredito {
  idPolitica: number;
  scoreDatacreditoMinimo: number | null;
  requiereVeriff: boolean;
  cupoMinimo: number;
  cupoMaximo: number;
  edadMinima: number;
  edadMaxima: number;
  estado: string;
  vigenteDesde: string;
}

interface CupoOrdinario {
  idCupo: number;
  idUsuario: number;
  nombreUsuario: string;
  idWallet: number;
  cupoAprobado: number;
  cupoUsado: number;
  cupoDisponible: number;
  estado: string;
  fechaAprobacion: string;
  fechaVencimiento: string | null;
}

type Tab = 'parametros' | 'gastos' | 'politica' | 'cupos';

const fmt = (v: number) =>
  new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(v);
const fmtPct = (v: number) => `${v}%`;

const TAB_LABELS: Record<Tab, string> = {
  parametros: 'Parámetros',
  gastos:     'Gastos Cobranza',
  politica:   'Política Crédito',
  cupos:      'Cupos QA',
};

export function CarteraOrdinariaAdminPage() {
  const [tab, setTab] = useState<Tab>('parametros');
  const [parametros, setParametros]   = useState<ParametroUtilizacion[]>([]);
  const [gastos, setGastos]           = useState<GastoCobranza[]>([]);
  const [politica, setPolitica]       = useState<PoliticaCredito | null>(null);
  const [cupos, setCupos]             = useState<CupoOrdinario[]>([]);
  const [tabErrors, setTabErrors]     = useState<Partial<Record<Tab, string>>>({});
  const [busy, setBusy]               = useState(false);
  const [actionMsg, setActionMsg]     = useState('');

  const [editParam, setEditParam] = useState<Partial<ParametroUtilizacion> & { tipo: string }>({ tipo: 'COMPRA_COMERCIO' });
  const [showParamForm, setShowParamForm] = useState(false);
  const [cupoForm, setCupoForm] = useState({ idUsuario: '', cupoAprobado: '', observaciones: '' });

  const setTabError = (t: Tab, msg: string) =>
    setTabErrors(prev => ({ ...prev, [t]: msg }));
  const clearTabError = (t: Tab) =>
    setTabErrors(prev => { const n = { ...prev }; delete n[t]; return n; });

  const load = useCallback(async (t: Tab) => {
    clearTabError(t);
    try {
      if (t === 'parametros') {
        setParametros(await get<ParametroUtilizacion[]>('/api/cartera-ordinaria/admin/parametros'));
      } else if (t === 'gastos') {
        setGastos(await get<GastoCobranza[]>('/api/cartera-ordinaria/admin/gastos-cobranza'));
      } else if (t === 'politica') {
        try {
          setPolitica(await get<PoliticaCredito>('/api/cartera-ordinaria/admin/politica'));
        } catch { setPolitica(null); }
      } else if (t === 'cupos') {
        setCupos(await get<CupoOrdinario[]>('/api/cartera-ordinaria/admin/cupos'));
      }
    } catch (e: unknown) {
      setTabError(t, `Error cargando ${TAB_LABELS[t]}: ${e instanceof Error ? e.message : 'Error desconocido'}`);
    }
  }, []);

  useEffect(() => { load(tab); }, [load, tab]);

  const saveParam = async () => {
    setBusy(true); setActionMsg('');
    try {
      await put(`/api/cartera-ordinaria/admin/parametros/${editParam.tipo}`, {
        tasaEmv:    editParam.tasaEmv,
        porcAval:   editParam.porcAval,
        porcAdmin:  editParam.porcAdmin,
        aplicaIva:  editParam.aplicaIva,
        porcIva:    editParam.porcIva,
        plazoMin:   editParam.plazoMin,
        plazoMax:   editParam.plazoMax,
        frecuencia: editParam.frecuencia,
        montoMin:   editParam.montoMin,
        montoMax:   editParam.montoMax,
      });
      setActionMsg('Parámetros guardados correctamente');
      setShowParamForm(false);
      load('parametros');
    } catch (e: unknown) {
      setActionMsg(`Error guardando: ${e instanceof Error ? e.message : 'Error desconocido'}`);
    } finally { setBusy(false); }
  };

  const asignarCupo = async () => {
    if (!cupoForm.idUsuario || !cupoForm.cupoAprobado) {
      setActionMsg('Completa ID usuario y cupo aprobado');
      return;
    }
    setBusy(true); setActionMsg('');
    try {
      await post('/api/cartera-ordinaria/admin/cupos', {
        idUsuario:     Number(cupoForm.idUsuario),
        cupoAprobado:  Number(cupoForm.cupoAprobado),
        observaciones: cupoForm.observaciones || null,
      });
      setActionMsg('Cupo asignado correctamente');
      setCupoForm({ idUsuario: '', cupoAprobado: '', observaciones: '' });
      load('cupos');
    } catch (e: unknown) {
      setActionMsg(`Error asignando cupo: ${e instanceof Error ? e.message : 'Error desconocido'}`);
    } finally { setBusy(false); }
  };

  const openEditParam = (p: ParametroUtilizacion) => {
    setEditParam({ ...p, tipo: p.tipoUtilizacion });
    setShowParamForm(true);
    setActionMsg('');
  };

  const tabs = (Object.keys(TAB_LABELS) as Tab[]);

  const tabHasError = (t: Tab) => !!tabErrors[t];

  return (
    <div style={{ padding: '1.5rem', maxWidth: 980 }}>
      <h2 style={{ marginBottom: '0.25rem' }}>Cartera Ordinaria — Administración</h2>
      <p style={{ fontSize: 13, color: '#666', marginTop: 0, marginBottom: '1rem' }}>
        Parámetros de crédito, gastos de cobranza, políticas y cupos QA.
      </p>

      <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem', borderBottom: '2px solid #e0e0e0' }}>
        {tabs.map(t => (
          <button key={t}
            onClick={() => { setTab(t); setActionMsg(''); }}
            style={{
              padding: '0.5rem 1.2rem', cursor: 'pointer', border: 'none',
              borderBottom: tab === t ? '2px solid #1976d2' : '2px solid transparent',
              background: 'none', fontWeight: tab === t ? 700 : 400,
              color: tabHasError(t) ? '#c62828' : (tab === t ? '#1976d2' : '#555'),
            }}>
            {TAB_LABELS[t]}{tabHasError(t) ? ' ⚠' : ''}
          </button>
        ))}
      </div>

      {tabErrors[tab] && (
        <div style={{ padding: '0.75rem 1rem', background: '#ffebee', border: '1px solid #ffcdd2', borderRadius: 4, marginBottom: '1rem', fontSize: 13, color: '#c62828' }}>
          {tabErrors[tab]}
        </div>
      )}

      {actionMsg && (
        <p style={{ color: actionMsg.toLowerCase().includes('error') ? '#c62828' : '#2e7d32', marginBottom: '1rem', fontSize: 13 }}>
          {actionMsg}
        </p>
      )}

      {/* ── Tab: Parámetros ─────────────────────────────────────────── */}
      {tab === 'parametros' && !tabErrors.parametros && (
        <div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 14 }}>
            <thead>
              <tr style={{ background: '#f5f5f5' }}>
                {['Tipo','Tasa EMV','Aval%','Admin%','IVA%','Plazo','Frecuencia','Monto Mín','Monto Máx',''].map(h =>
                  <th key={h} style={{ padding: '8px', textAlign: 'left', borderBottom: '1px solid #ddd', whiteSpace: 'nowrap' }}>{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {parametros.length === 0 ? (
                <tr><td colSpan={10} style={{ padding: '1rem', color: '#888', textAlign: 'center' }}>Sin parámetros registrados</td></tr>
              ) : parametros.map(p => (
                <tr key={p.idParametro}>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{p.tipoUtilizacion}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmtPct(p.tasaEmv)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmtPct(p.porcAval)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmtPct(p.porcAdmin)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{p.aplicaIva ? fmtPct(p.porcIva) : '—'}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{p.plazoMin}–{p.plazoMax} m</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{p.frecuencia}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(p.montoMin)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(p.montoMax)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>
                    <button onClick={() => openEditParam(p)} style={{ fontSize: 12, cursor: 'pointer', padding: '3px 8px' }}>Editar</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {showParamForm && (
            <div style={{ marginTop: '1.5rem', padding: '1rem', border: '1px solid #ddd', borderRadius: 6, maxWidth: 520, background: '#fafafa' }}>
              <h4 style={{ marginTop: 0 }}>Editar — {editParam.tipo}</h4>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
                {([
                  ['Tasa EMV (%)', 'tasaEmv'],
                  ['Aval (%)',     'porcAval'],
                  ['Admin (%)',    'porcAdmin'],
                  ['IVA (%)',      'porcIva'],
                  ['Plazo mín (m)', 'plazoMin'],
                  ['Plazo máx (m)', 'plazoMax'],
                  ['Monto mín (COP)', 'montoMin'],
                  ['Monto máx (COP)', 'montoMax'],
                ] as [string, keyof ParametroUtilizacion][]).map(([label, field]) => (
                  <label key={field} style={{ display: 'flex', flexDirection: 'column', fontSize: 13 }}>
                    {label}
                    <input type="number" value={(editParam as Record<string, unknown>)[field] as number ?? ''}
                      onChange={e => setEditParam(prev => ({ ...prev, [field]: e.target.value === '' ? '' : Number(e.target.value) }))}
                      style={{ marginTop: 4, padding: '4px 8px', border: '1px solid #ccc', borderRadius: 4 }} />
                  </label>
                ))}
                <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13 }}>
                  Frecuencia
                  <select value={editParam.frecuencia ?? 'MENSUAL'}
                    onChange={e => setEditParam(prev => ({ ...prev, frecuencia: e.target.value }))}
                    style={{ marginTop: 4, padding: '4px 8px', border: '1px solid #ccc', borderRadius: 4 }}>
                    <option value="MENSUAL">MENSUAL</option>
                    <option value="QUINCENAL">QUINCENAL</option>
                  </select>
                </label>
                <label style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontSize: 13, marginTop: 4 }}>
                  <input type="checkbox" checked={editParam.aplicaIva ?? false}
                    onChange={e => setEditParam(prev => ({ ...prev, aplicaIva: e.target.checked }))} />
                  Aplica IVA
                </label>
              </div>
              <div style={{ marginTop: '1rem', display: 'flex', gap: '0.5rem' }}>
                <button onClick={saveParam} disabled={busy}
                  style={{ padding: '8px 20px', background: '#1976d2', color: '#fff', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                  {busy ? 'Guardando…' : 'Guardar'}
                </button>
                <button onClick={() => setShowParamForm(false)}
                  style={{ padding: '8px 16px', background: '#eee', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
                  Cancelar
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* ── Tab: Gastos de cobranza ──────────────────────────────────── */}
      {tab === 'gastos' && !tabErrors.gastos && (
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 14 }}>
          <thead>
            <tr style={{ background: '#f5f5f5' }}>
              {['Días desde','Días hasta','Tipo','Valor','Descripción','Estado'].map(h =>
                <th key={h} style={{ padding: '8px', textAlign: 'left', borderBottom: '1px solid #ddd' }}>{h}</th>)}
            </tr>
          </thead>
          <tbody>
            {gastos.length === 0 ? (
              <tr><td colSpan={6} style={{ padding: '1rem', color: '#888', textAlign: 'center' }}>Sin tramos registrados</td></tr>
            ) : gastos.map(g => (
              <tr key={g.idGasto}>
                <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{g.diasDesde}</td>
                <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{g.diasHasta ?? '∞'}</td>
                <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{g.tipoCobro}</td>
                <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>
                  {g.tipoCobro === 'FIJO' ? fmt(g.valorCobro) : `${g.valorCobro}%`}
                </td>
                <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{g.descripcion ?? '—'}</td>
                <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{g.estado}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {/* ── Tab: Política de crédito ─────────────────────────────────── */}
      {tab === 'politica' && !tabErrors.politica && (
        <div style={{ maxWidth: 480 }}>
          {politica ? (
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 14 }}>
              <tbody>
                {([
                  ['Score Datacredito mín.', politica.scoreDatacreditoMinimo != null ? String(politica.scoreDatacreditoMinimo) : 'No requerido'],
                  ['Requiere Veriff',        politica.requiereVeriff ? 'Sí' : 'No'],
                  ['Cupo mínimo',            fmt(politica.cupoMinimo)],
                  ['Cupo máximo',            fmt(politica.cupoMaximo)],
                  ['Edad mínima',            `${politica.edadMinima} años`],
                  ['Edad máxima',            `${politica.edadMaxima} años`],
                  ['Estado',                 politica.estado],
                  ['Vigente desde',          new Date(politica.vigenteDesde).toLocaleDateString('es-CO')],
                ] as [string, string][]).map(([k, v]) => (
                  <tr key={k}>
                    <td style={{ padding: '8px', fontWeight: 600, borderBottom: '1px solid #eee', width: '50%' }}>{k}</td>
                    <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{v}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <p style={{ color: '#888' }}>No hay política activa configurada.</p>
          )}
        </div>
      )}

      {/* ── Tab: Cupos QA ────────────────────────────────────────────── */}
      {tab === 'cupos' && !tabErrors.cupos && (
        <div>
          <p style={{ fontSize: 12, color: '#888', marginTop: '-0.5rem', marginBottom: '0.75rem' }}>
            Columnas idCupo/idUsuario/idWallet/Vencimiento agregadas temporalmente para diagnóstico (Fase 69.2).
          </p>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13, marginBottom: '1.5rem' }}>
            <thead>
              <tr style={{ background: '#f5f5f5' }}>
                {['idCupo','idUsuario','idWallet','Usuario','Aprobado','Usado','Disponible','Estado','Aprobación','Vencimiento'].map(h =>
                  <th key={h} style={{ padding: '8px', textAlign: 'left', borderBottom: '1px solid #ddd', whiteSpace: 'nowrap' }}>{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {cupos.length === 0 ? (
                <tr><td colSpan={10} style={{ padding: '1rem', color: '#888', textAlign: 'center' }}>Sin cupos registrados</td></tr>
              ) : cupos.map(c => (
                <tr key={c.idCupo}>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee', fontFamily: 'monospace' }}>{c.idCupo}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee', fontFamily: 'monospace' }}>{c.idUsuario}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee', fontFamily: 'monospace' }}>{c.idWallet}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{c.nombreUsuario}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(c.cupoAprobado)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(c.cupoUsado)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(c.cupoDisponible)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{c.estado}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{new Date(c.fechaAprobacion).toLocaleDateString('es-CO')}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{c.fechaVencimiento ? new Date(c.fechaVencimiento).toLocaleDateString('es-CO') : '— (sin vencimiento)'}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <div style={{ padding: '1rem', border: '1px solid #ddd', borderRadius: 6, maxWidth: 420, background: '#fafafa' }}>
            <h4 style={{ marginTop: 0 }}>Asignar / actualizar cupo</h4>
            <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13, marginBottom: '0.75rem' }}>
              ID Usuario
              <input type="number" value={cupoForm.idUsuario}
                onChange={e => setCupoForm(f => ({ ...f, idUsuario: e.target.value }))}
                style={{ marginTop: 4, padding: '4px 8px', border: '1px solid #ccc', borderRadius: 4 }} />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13, marginBottom: '0.75rem' }}>
              Cupo aprobado (COP)
              <input type="number" value={cupoForm.cupoAprobado}
                onChange={e => setCupoForm(f => ({ ...f, cupoAprobado: e.target.value }))}
                style={{ marginTop: 4, padding: '4px 8px', border: '1px solid #ccc', borderRadius: 4 }} />
            </label>
            <label style={{ display: 'flex', flexDirection: 'column', fontSize: 13, marginBottom: '0.75rem' }}>
              Observaciones (opcional)
              <input type="text" value={cupoForm.observaciones}
                onChange={e => setCupoForm(f => ({ ...f, observaciones: e.target.value }))}
                style={{ marginTop: 4, padding: '4px 8px', border: '1px solid #ccc', borderRadius: 4 }} />
            </label>
            <button onClick={asignarCupo} disabled={busy}
              style={{ padding: '8px 20px', background: '#1976d2', color: '#fff', border: 'none', borderRadius: 4, cursor: 'pointer' }}>
              {busy ? 'Guardando…' : 'Asignar cupo'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
