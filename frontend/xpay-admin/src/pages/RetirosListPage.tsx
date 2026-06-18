import { FormEvent, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

interface RetiroItem {
  idRetiro:        number;
  idComercio:      number;
  idWalletComercio: number;
  valor:           number;
  estado:          string;
  medioRetiro:     string | null;
  banco:           string | null;
  titularCuenta:   string | null;
  fechaSolicitud:  string;
  fechaPago:       string | null;
  fechaRechazo:    string | null;
}

interface ListaData {
  items:    RetiroItem[];
  total:    number;
  page:     number;
  pageSize: number;
}

interface ApiResp { success: boolean; data: ListaData; }

type Filters = { estado: string; idComercio: string; desde: string; hasta: string };

const EMPTY: Filters = { estado: '', idComercio: '', desde: '', hasta: '' };

function estadoBadge(estado: string) {
  const cls = estado === 'PAGADO' ? 'badge-ok' : estado === 'PENDIENTE' ? 'badge-info' : 'badge-warn';
  return <span className={`badge ${cls}`}>{estado}</span>;
}

export function RetirosListPage() {
  const navigate = useNavigate();
  const [form,    setForm]    = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const [data,    setData]    = useState<ListaData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error,   setError]   = useState('');

  useEffect(() => {
    const params = new URLSearchParams();
    if (applied.estado)     params.set('estado',     applied.estado);
    if (applied.idComercio) params.set('idComercio', applied.idComercio);
    if (applied.desde)      params.set('desde',      applied.desde);
    if (applied.hasta)      params.set('hasta',      applied.hasta);
    const qs  = params.toString();
    const url = `/api/comercios/retiros${qs ? `?${qs}` : ''}`;

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
      <h2>Listado de retiros</h2>

      <form className="filter-form" onSubmit={handleBuscar}>
        <div className="filter-field">
          <label>Estado</label>
          <select value={form.estado} onChange={e => setForm(f => ({ ...f, estado: e.target.value }))}>
            <option value="">Todos</option>
            <option value="PENDIENTE">Pendiente</option>
            <option value="PAGADO">Pagado</option>
            <option value="RECHAZADO">Rechazado</option>
          </select>
        </div>
        <div className="filter-field">
          <label>ID Comercio</label>
          <input
            type="number"
            value={form.idComercio}
            onChange={e => setForm(f => ({ ...f, idComercio: e.target.value }))}
            placeholder="ej. 1"
            min={1}
          />
        </div>
        <div className="filter-field">
          <label>Desde</label>
          <input
            type="date"
            value={form.desde}
            onChange={e => setForm(f => ({ ...f, desde: e.target.value }))}
          />
        </div>
        <div className="filter-field">
          <label>Hasta</label>
          <input
            type="date"
            value={form.hasta}
            onChange={e => setForm(f => ({ ...f, hasta: e.target.value }))}
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
              : `${data.total} retiro${data.total !== 1 ? 's' : ''} encontrado${data.total !== 1 ? 's' : ''} — página ${data.page}`}
          </div>

          {data.items.length === 0 ? (
            <div className="empty">No hay retiros que coincidan con los filtros.</div>
          ) : (
            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Comercio</th>
                    <th>Valor</th>
                    <th>Estado</th>
                    <th>Medio</th>
                    <th>Banco</th>
                    <th>Titular</th>
                    <th>Fecha Solicitud</th>
                    <th>Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map(r => (
                    <tr key={r.idRetiro}>
                      <td className="mono">{r.idRetiro}</td>
                      <td className="mono">{r.idComercio}</td>
                      <td>{fmtMoney(r.valor)}</td>
                      <td>{estadoBadge(r.estado)}</td>
                      <td>{r.medioRetiro ?? '—'}</td>
                      <td>{r.banco       ?? '—'}</td>
                      <td>{r.titularCuenta ?? '—'}</td>
                      <td className="mono">{fmtDate(r.fechaSolicitud)}</td>
                      <td>
                        <button
                          className="btn-link"
                          onClick={() => navigate(`/retiros/${r.idRetiro}`)}
                        >
                          Ver / gestionar
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
