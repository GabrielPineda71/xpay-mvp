import { FormEvent, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

interface LedgerTxItem {
  idTransaccionLedger: number;
  tipoTransaccion:     string;
  referenciaTipo:      string | null;
  referenciaId:        number | null;
  descripcion:         string | null;
  valorTotal:          number;
  fechaTransaccion:    string;
  creadoPor:           number | null;
}

interface ListaData {
  items:    LedgerTxItem[];
  total:    number;
  page:     number;
  pageSize: number;
}

interface ApiResp { success: boolean; data: ListaData; }

type Filters = { tipoTransaccion: string; desde: string; hasta: string };

const EMPTY: Filters = { tipoTransaccion: '', desde: '', hasta: '' };

export function LedgerTransaccionesListPage() {
  const navigate = useNavigate();
  const [form,    setForm]    = useState<Filters>(EMPTY);
  const [applied, setApplied] = useState<Filters>(EMPTY);
  const [data,    setData]    = useState<ListaData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error,   setError]   = useState('');

  useEffect(() => {
    const params = new URLSearchParams();
    if (applied.tipoTransaccion) params.set('tipoTransaccion', applied.tipoTransaccion);
    if (applied.desde)           params.set('desde',           applied.desde);
    if (applied.hasta)           params.set('hasta',           applied.hasta);
    const qs  = params.toString();
    const url = `/api/admin/ledger-transacciones${qs ? `?${qs}` : ''}`;

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
      <h2>Transacciones ledger</h2>

      <form className="filter-form" onSubmit={handleBuscar}>
        <div className="filter-field">
          <label>Tipo transacción</label>
          <input
            type="text"
            value={form.tipoTransaccion}
            onChange={e => setForm(f => ({ ...f, tipoTransaccion: e.target.value }))}
            placeholder="ej. PAGO_QR"
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
              : `${data.total} transacción${data.total !== 1 ? 'es' : ''} encontrada${data.total !== 1 ? 's' : ''} — página ${data.page}`}
          </div>

          {data.items.length === 0 ? (
            <div className="empty">No hay transacciones que coincidan con los filtros.</div>
          ) : (
            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Tipo</th>
                    <th>Ref. tipo</th>
                    <th>Ref. ID</th>
                    <th>Descripción</th>
                    <th>Valor total</th>
                    <th>Fecha</th>
                    <th>Creado por</th>
                    <th>Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map(t => (
                    <tr key={t.idTransaccionLedger}>
                      <td className="mono">{t.idTransaccionLedger}</td>
                      <td>{t.tipoTransaccion}</td>
                      <td>{t.referenciaTipo ?? '—'}</td>
                      <td className="mono">{t.referenciaId ?? '—'}</td>
                      <td>{t.descripcion   ?? '—'}</td>
                      <td className="credit">{fmtMoney(t.valorTotal)}</td>
                      <td className="mono">{fmtDate(t.fechaTransaccion)}</td>
                      <td className="mono">{t.creadoPor ?? '—'}</td>
                      <td>
                        <button
                          className="btn-link"
                          onClick={() => navigate(`/ledger/${t.idTransaccionLedger}`)}
                        >
                          Ver detalle
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
