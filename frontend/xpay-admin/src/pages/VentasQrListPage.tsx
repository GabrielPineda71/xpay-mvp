import { FormEvent, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

interface VentaQrItem {
  idVentaQr:                number;
  idComercio:               number;
  idTienda:                 number;
  idWalletUsuario:          number;
  valorBruto:               number;
  estado:                   string;
  idTransaccionLedger:      number | null;
  idTransaccionLiquidacion: number | null;
  fechaVenta:               string;
}

interface ListaData {
  items:    VentaQrItem[];
  total:    number;
  page:     number;
  pageSize: number;
}

interface ApiResp { success: boolean; data: ListaData; }

type Filters = {
  estado:     string;
  idComercio: string;
  idTienda:   string;
  desde:      string;
  hasta:      string;
};

const EMPTY: Filters = { estado: '', idComercio: '', idTienda: '', desde: '', hasta: '' };

function estadoBadge(estado: string) {
  const cls = estado === 'LIQUIDADA' ? 'badge-ok' : estado === 'CONTINGENCIA' ? 'badge-info' : 'badge-warn';
  return <span className={`badge ${cls}`}>{estado}</span>;
}

export function VentasQrListPage() {
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
    if (applied.idTienda)   params.set('idTienda',   applied.idTienda);
    if (applied.desde)      params.set('desde',      applied.desde);
    if (applied.hasta)      params.set('hasta',      applied.hasta);
    const qs  = params.toString();
    const url = `/api/admin/ventas-qr${qs ? `?${qs}` : ''}`;

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
      <h2>Listado de ventas QR</h2>

      <form className="filter-form" onSubmit={handleBuscar}>
        <div className="filter-field">
          <label>Estado</label>
          <select value={form.estado} onChange={e => setForm(f => ({ ...f, estado: e.target.value }))}>
            <option value="">Todos</option>
            <option value="CONTINGENCIA">Contingencia</option>
            <option value="LIQUIDADA">Liquidada</option>
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
          <label>ID Tienda</label>
          <input
            type="number"
            value={form.idTienda}
            onChange={e => setForm(f => ({ ...f, idTienda: e.target.value }))}
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
              : `${data.total} venta${data.total !== 1 ? 's' : ''} encontrada${data.total !== 1 ? 's' : ''} — página ${data.page}`}
          </div>

          {data.items.length === 0 ? (
            <div className="empty">No hay ventas QR que coincidan con los filtros.</div>
          ) : (
            <div className="table-wrapper">
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Comercio</th>
                    <th>Tienda</th>
                    <th>Wallet usuario</th>
                    <th>Valor bruto</th>
                    <th>Estado</th>
                    <th>Tx pago</th>
                    <th>Tx liquidación</th>
                    <th>Fecha venta</th>
                    <th>Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map(v => (
                    <tr key={v.idVentaQr}>
                      <td className="mono">{v.idVentaQr}</td>
                      <td className="mono">{v.idComercio}</td>
                      <td className="mono">{v.idTienda}</td>
                      <td className="mono">{v.idWalletUsuario}</td>
                      <td className="credit">{fmtMoney(v.valorBruto)}</td>
                      <td>{estadoBadge(v.estado)}</td>
                      <td className="mono">{v.idTransaccionLedger      ?? '—'}</td>
                      <td className="mono">{v.idTransaccionLiquidacion ?? '—'}</td>
                      <td className="mono">{fmtDate(v.fechaVenta)}</td>
                      <td>
                        <button
                          className="btn-link"
                          onClick={() => navigate(`/comercios/${v.idComercio}`)}
                        >
                          Ver comercio
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
