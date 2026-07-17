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
}

type Tab = 'parametros' | 'gastos' | 'politica' | 'cupos';

const fmt = (v: number) => new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 }).format(v);
const fmtPct = (v: number) => `${v}%`;

export function CarteraOrdinariaAdminPage() {
  const [tab, setTab] = useState<Tab>('parametros');
  const [parametros, setParametros] = useState<ParametroUtilizacion[]>([]);
  const [gastos, setGastos] = useState<GastoCobranza[]>([]);
  const [politica, setPolitica] = useState<PoliticaCredito | null>(null);
  const [cupos, setCupos] = useState<CupoOrdinario[]>([]);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState('');

  const [editParam, setEditParam] = useState<Partial<ParametroUtilizacion> & { tipo: string }>({ tipo: 'COMPRA_COMERCIO' });
  const [showParamForm, setShowParamForm] = useState(false);
  const [cupoForm, setCupoForm] = useState({ idUsuario: '', cupoAprobado: '', observaciones: '' });

  const load = useCallback(async () => {
    try {
      if (tab === 'parametros') {
        setParametros(await get<ParametroUtilizacion[]>('/api/cartera-ordinaria/admin/parametros'));
      } else if (tab === 'gastos') {
        setGastos(await get<GastoCobranza[]>('/api/cartera-ordinaria/admin/gastos-cobranza'));
      } else if (tab === 'politica') {
        try {
          setPolitica(await get<PoliticaCredito>('/api/cartera-ordinaria/admin/politica'));
        } catch { setPolitica(null); }
      } else if (tab === 'cupos') {
        setCupos(await get<CupoOrdinario[]>('/api/cartera-ordinaria/admin/cupos'));
      }
    } catch {
      setMsg('Error cargando datos');
    }
  }, [tab]);

  useEffect(() => { load(); }, [load]);

  const saveParam = async () => {
    setBusy(true); setMsg('');
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
      setMsg('Parámetros guardados');
      setShowParamForm(false);
      load();
    } catch (e: unknown) {
      setMsg(e instanceof Error ? e.message : 'Error guardando');
    } finally { setBusy(false); }
  };

  const asignarCupo = async () => {
    if (!cupoForm.idUsuario || !cupoForm.cupoAprobado) { setMsg('Completa todos los campos'); return; }
    setBusy(true); setMsg('');
    try {
      await post('/api/cartera-ordinaria/admin/cupos', {
        idUsuario:    Number(cupoForm.idUsuario),
        cupoAprobado: Number(cupoForm.cupoAprobado),
        observaciones: cupoForm.observaciones || null,
      });
      setMsg('Cupo asignado correctamente');
      setCupoForm({ idUsuario: '', cupoAprobado: '', observaciones: '' });
      load();
    } catch (e: unknown) {
      setMsg(e instanceof Error ? e.message : 'Error asignando cupo');
    } finally { setBusy(false); }
  };

  const openEditParam = (p: ParametroUtilizacion) => {
    setEditParam({ ...p, tipo: p.tipoUtilizacion });
    setShowParamForm(true);
  };

  const tabs: { key: Tab; label: string }[] = [
    { key: 'parametros', label: 'Parámetros' },
    { key: 'gastos',     label: 'Gastos Cobranza' },
    { key: 'politica',   label: 'Política Crédito' },
    { key: 'cupos',      label: 'Cupos QA' },
  ];

  return (
    <div style={{ padding: '1.5rem', maxWidth: 960 }}>
      <h2>Cartera Ordinaria — Administración</h2>

      <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem', borderBottom: '2px solid #e0e0e0' }}>
        {tabs.map(t => (
          <button key={t.key}
            onClick={() => { setTab(t.key); setMsg(''); }}
            style={{
              padding: '0.5rem 1.2rem', cursor: 'pointer', border: 'none',
              borderBottom: tab === t.key ? '2px solid #1976d2' : '2px solid transparent',
              background: 'none', fontWeight: tab === t.key ? 700 : 400,
              color: tab === t.key ? '#1976d2' : '#555',
            }}>
            {t.label}
          </button>
        ))}
      </div>

      {msg && <p style={{ color: msg.includes('Error') ? '#c62828' : '#2e7d32', marginBottom: '1rem' }}>{msg}</p>}

      {/* ── Tab: Parámetros ─────────────────────────────────────────── */}
      {tab === 'parametros' && (
        <div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 14 }}>
            <thead>
              <tr style={{ background: '#f5f5f5' }}>
                {['Tipo','Tasa EMV','Aval%','Admin%','IVA%','Plazo','Frecuencia','Monto Min','Monto Max',''].map(h =>
                  <th key={h} style={{ padding: '8px', textAlign: 'left', borderBottom: '1px solid #ddd' }}>{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {parametros.map(p => (
                <tr key={p.idParametro}>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{p.tipoUtilizacion}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmtPct(p.tasaEmv)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmtPct(p.porcAval)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmtPct(p.porcAdmin)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{p.aplicaIva ? fmtPct(p.porcIva) : 'No'}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{p.plazoMin}–{p.plazoMax} m</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{p.frecuencia}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(p.montoMin)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(p.montoMax)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>
                    <button onClick={() => openEditParam(p)} style={{ fontSize: 12, cursor: 'pointer' }}>Editar</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {showParamForm && (
            <div style={{ marginTop: '1.5rem', padding: '1rem', border: '1px solid #ddd', borderRadius: 6, maxWidth: 520 }}>
              <h4 style={{ marginTop: 0 }}>Editar parámetro — {editParam.tipo}</h4>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
                {([
                  ['Tasa EMV (%)', 'tasaEmv'],
                  ['Aval (%)',     'porcAval'],
                  ['Admin (%)',    'porcAdmin'],
                  ['IVA (%)',      'porcIva'],
                  ['Plazo mín (m)', 'plazoMin'],
                  ['Plazo máx (m)', 'plazoMax'],
                  ['Monto mín',   'montoMin'],
                  ['Monto máx',   'montoMax'],
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
                <label style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontSize: 13 }}>
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
      {tab === 'gastos' && (
        <div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 14 }}>
            <thead>
              <tr style={{ background: '#f5f5f5' }}>
                {['Días desde','Días hasta','Tipo','Valor','Descripción','Estado'].map(h =>
                  <th key={h} style={{ padding: '8px', textAlign: 'left', borderBottom: '1px solid #ddd' }}>{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {gastos.map(g => (
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
        </div>
      )}

      {/* ── Tab: Política de crédito ─────────────────────────────────── */}
      {tab === 'politica' && (
        <div style={{ maxWidth: 480 }}>
          {politica ? (
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 14 }}>
              <tbody>
                {([
                  ['Score Datacredito mín.', politica.scoreDatacreditoMinimo ?? 'No requerido'],
                  ['Requiere Veriff',        politica.requiereVeriff ? 'Sí' : 'No'],
                  ['Cupo mínimo',            fmt(politica.cupoMinimo)],
                  ['Cupo máximo',            fmt(politica.cupoMaximo)],
                  ['Edad mínima',            `${politica.edadMinima} años`],
                  ['Edad máxima',            `${politica.edadMaxima} años`],
                  ['Estado',                 politica.estado],
                  ['Vigente desde',          new Date(politica.vigenteDesde).toLocaleDateString('es-CO')],
                ] as [string, string | number][]).map(([k, v]) => (
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
      {tab === 'cupos' && (
        <div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 14, marginBottom: '1.5rem' }}>
            <thead>
              <tr style={{ background: '#f5f5f5' }}>
                {['Usuario','Aprobado','Usado','Disponible','Estado','Aprobación'].map(h =>
                  <th key={h} style={{ padding: '8px', textAlign: 'left', borderBottom: '1px solid #ddd' }}>{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {cupos.map(c => (
                <tr key={c.idCupo}>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{c.nombreUsuario}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(c.cupoAprobado)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(c.cupoUsado)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{fmt(c.cupoDisponible)}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{c.estado}</td>
                  <td style={{ padding: '8px', borderBottom: '1px solid #eee' }}>{new Date(c.fechaAprobacion).toLocaleDateString('es-CO')}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <div style={{ padding: '1rem', border: '1px solid #ddd', borderRadius: 6, maxWidth: 420 }}>
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
              Observaciones
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
