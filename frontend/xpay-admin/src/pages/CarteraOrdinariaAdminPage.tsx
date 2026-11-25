import { useState, useEffect, useCallback } from 'react';
import { get, post } from '../api/client.ts';
import { CarteraParametrosSection } from '../components/cartera/CarteraParametrosSection.tsx';

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

const TAB_LABELS: Record<Tab, string> = {
  parametros: 'Parámetros',
  gastos:     'Gastos Cobranza',
  politica:   'Política Crédito',
  cupos:      'Cupos QA',
};

export function CarteraOrdinariaAdminPage() {
  const [tab, setTab] = useState<Tab>('parametros');
  const [gastos, setGastos]           = useState<GastoCobranza[]>([]);
  const [politica, setPolitica]       = useState<PoliticaCredito | null>(null);
  const [cupos, setCupos]             = useState<CupoOrdinario[]>([]);
  const [tabErrors, setTabErrors]     = useState<Partial<Record<Tab, string>>>({});
  const [busy, setBusy]               = useState(false);
  const [actionMsg, setActionMsg]     = useState('');

  const [parametrosBusy, setParametrosBusy] = useState(false);
  const [cupoForm, setCupoForm] = useState({ idUsuario: '', cupoAprobado: '', observaciones: '' });

  const setTabError = (t: Tab, msg: string) =>
    setTabErrors(prev => ({ ...prev, [t]: msg }));
  const clearTabError = (t: Tab) =>
    setTabErrors(prev => { const n = { ...prev }; delete n[t]; return n; });

  const load = useCallback(async (t: Tab) => {
    clearTabError(t);
    try {
      if (t === 'gastos') {
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

  useEffect(() => { if (tab !== 'parametros') void load(tab); }, [load, tab]);

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

  const tabs = (Object.keys(TAB_LABELS) as Tab[]);

  const tabHasError = (t: Tab) => !!tabErrors[t];

  return (
    <div style={{ padding: 'clamp(0.75rem, 3vw, 1.5rem)', maxWidth: 980, minWidth: 0 }}>
      <h2 style={{ marginBottom: '0.25rem' }}>Cartera Ordinaria — Administración</h2>
      <p style={{ fontSize: 13, color: '#666', marginTop: 0, marginBottom: '1rem' }}>
        Parámetros de crédito, gastos de cobranza, políticas y cupos QA.
      </p>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem', marginBottom: '1rem', borderBottom: '2px solid #e0e0e0' }}>
        {tabs.map(t => (
          <button key={t} disabled={parametrosBusy}
            onClick={() => { if (!parametrosBusy) { setTab(t); setActionMsg(''); } }}
            style={{
              padding: '0.5rem 1.2rem', minHeight: 44, cursor: parametrosBusy ? 'default' : 'pointer', border: 'none',
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
      {tab === 'parametros' && <CarteraParametrosSection onBusyChange={setParametrosBusy} />}

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
