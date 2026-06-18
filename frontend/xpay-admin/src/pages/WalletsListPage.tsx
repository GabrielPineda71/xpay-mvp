import { FormEvent, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

interface WalletItem {
  idWallet:        number;
  idPersona:       number | null;
  idComercio:      number | null;
  tipoWallet:      string;
  nombreWallet:    string | null;
  estado:          string;
  saldoDisponible: number;
  fechaCreacion:   string;
}

interface ListaData {
  items:    WalletItem[];
  total:    number;
  page:     number;
  pageSize: number;
}

interface ApiResp { success: boolean; data: ListaData; }

type Filters = { tipoWallet: string; estado: string; idPersona: string };

const EMPTY: Filters = { tipoWallet: '', estado: '', idPersona: '' };

function estadoBadge(estado: string) {
  const cls = estado === 'ACTIVA' ? 'badge-ok' : 'badge-warn';
  return <span className={`badge ${cls}`}>{estado}</span>;
}

export function WalletsListPage() {
  const navigate = useNavigate();
  const [form,    setForm]    = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const [data,    setData]    = useState<ListaData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error,   setError]   = useState('');

  useEffect(() => {
    const params = new URLSearchParams();
    if (applied.tipoWallet) params.set('tipoWallet', applied.tipoWallet);
    if (applied.estado)     params.set('estado',     applied.estado);
    if (applied.idPersona)  params.set('idPersona',  applied.idPersona);
    const qs  = params.toString();
    const url = `/api/admin/wallets${qs ? `?${qs}` : ''}`;

    setLoading(true);
    setError('');
    get<ApiResp>(url)
      .then(r => setData(r.data))
      .catch(err => { setData(null); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [applied]);

  function handleBuscar(e: FormEvent) {
    e.preventDefault();
    setApplied({ ...form });
  }

  function handleLimpiar() {
    setForm(EMPTY);
    setApplied(EMPTY);
  }

  return (
    <div className="page">
      <h2>Listado de wallets</h2>

      <form className="filter-form" onSubmit={handleBuscar}>
        <div className="filter-field">
          <label>Tipo</label>
          <select value={form.tipoWallet} onChange={e => setForm(f => ({ ...f, tipoWallet: e.target.value }))}>
            <option value="">Todos</option>
            <option value="PERSONA">Persona</option>
            <option value="COMERCIO">Comercio</option>
            <option value="XPAY">XPAY</option>
          </select>
        </div>
        <div className="filter-field">
          <label>Estado</label>
          <input
            type="text"
            value={form.estado}
            onChange={e => setForm(f => ({ ...f, estado: e.target.value }))}
            placeholder="ej. ACTIVA"
          />
        </div>
        <div className="filter-field">
          <label>ID Persona</label>
          <input
            type="number"
            value={form.idPersona}
            onChange={e => setForm(f => ({ ...f, idPersona: e.target.value }))}
            placeholder="ej. 1"
            min={1}
          />
        </div>
        <button type="submit" className="btn-search">Buscar</button>
        <button type="button" className="btn-search" style={{ background: '#718096' }} onClick={handleLimpiar}>
          Limpiar
        </button>
      </form>

      {loading && <div className="loading">Cargando...</div>}
      {error   && <div className="error-msg">Error: {error}</div>}

      {data && !loading && (
        <>
          <div className="results-meta">
            {data.total === 0
              ? 'Sin resultados para los filtros aplicados.'
              : `${data.total} wallet${data.total !== 1 ? 's' : ''} encontrada${data.total !== 1 ? 's' : ''} — página ${data.page}`}
          </div>

          {data.items.length === 0 ? (
            <div className="empty">No hay wallets que coincidan con los filtros.</div>
          ) : (
            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Tipo</th>
                    <th>Nombre</th>
                    <th>ID Persona</th>
                    <th>ID Comercio</th>
                    <th>Estado</th>
                    <th>Saldo disponible</th>
                    <th>Fecha creación</th>
                    <th>Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map(w => (
                    <tr key={w.idWallet}>
                      <td className="mono">{w.idWallet}</td>
                      <td>{w.tipoWallet}</td>
                      <td>{w.nombreWallet ?? '—'}</td>
                      <td className="mono">{w.idPersona  ?? '—'}</td>
                      <td className="mono">{w.idComercio ?? '—'}</td>
                      <td>{estadoBadge(w.estado)}</td>
                      <td className="credit">{fmtMoney(w.saldoDisponible)}</td>
                      <td className="mono">{fmtDate(w.fechaCreacion)}</td>
                      <td>
                        <button
                          className="btn-link"
                          onClick={() => navigate(`/wallets/${w.idWallet}`)}
                        >
                          Ver estado de cuenta
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}
