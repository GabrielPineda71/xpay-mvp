import { FormEvent, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtMoney } from '../utils.ts';

interface ComercioItem {
  idComercio:       number;
  nombreComercial:  string;
  nit:              string | null;
  estado:           string;
  idWalletComercio: number | null;
  saldoDisponible:  number;
}

interface ListaData {
  items:    ComercioItem[];
  total:    number;
  page:     number;
  pageSize: number;
}

interface ApiResp { success: boolean; data: ListaData; }

type Filters = { estado: string; texto: string };

const EMPTY: Filters = { estado: '', texto: '' };

function estadoBadge(estado: string) {
  const cls = estado === 'ACTIVO' ? 'badge-ok' : 'badge-warn';
  return <span className={`badge ${cls}`}>{estado}</span>;
}

export function ComerciosListPage() {
  const navigate = useNavigate();
  const [form,    setForm]    = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const [data,    setData]    = useState<ListaData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error,   setError]   = useState('');

  useEffect(() => {
    const params = new URLSearchParams();
    if (applied.estado) params.set('estado', applied.estado);
    if (applied.texto)  params.set('texto',  applied.texto);
    const qs  = params.toString();
    const url = `/api/admin/comercios${qs ? `?${qs}` : ''}`;

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
      <h2>Listado de comercios</h2>

      <form className="filter-form" onSubmit={handleBuscar}>
        <div className="filter-field">
          <label>Estado</label>
          <input
            type="text"
            value={form.estado}
            onChange={e => setForm(f => ({ ...f, estado: e.target.value }))}
            placeholder="ej. ACTIVO"
          />
        </div>
        <div className="filter-field">
          <label>Nombre / NIT</label>
          <input
            type="text"
            value={form.texto}
            onChange={e => setForm(f => ({ ...f, texto: e.target.value }))}
            placeholder="ej. Demo"
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
              : `${data.total} comercio${data.total !== 1 ? 's' : ''} encontrado${data.total !== 1 ? 's' : ''} — página ${data.page}`}
          </div>

          {data.items.length === 0 ? (
            <div className="empty">No hay comercios que coincidan con los filtros.</div>
          ) : (
            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Nombre comercial</th>
                    <th>NIT</th>
                    <th>Estado</th>
                    <th>ID Wallet</th>
                    <th>Saldo disponible</th>
                    <th>Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map(c => (
                    <tr key={c.idComercio}>
                      <td className="mono">{c.idComercio}</td>
                      <td>{c.nombreComercial}</td>
                      <td className="mono">{c.nit ?? '—'}</td>
                      <td>{estadoBadge(c.estado)}</td>
                      <td className="mono">{c.idWalletComercio ?? '—'}</td>
                      <td className="credit">{fmtMoney(c.saldoDisponible)}</td>
                      <td>
                        <button
                          className="btn-link"
                          onClick={() => navigate(`/comercios/${c.idComercio}`)}
                        >
                          Ver resumen
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
