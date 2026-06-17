import { FormEvent, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtMoney, fmtNum } from '../utils.ts';

interface ResumenComercio {
  idComercio:       number;
  nombreComercial:  string;
  idWalletComercio: number;
  saldoDisponible:  number;
  ventasQr: {
    total: number; contingencia: number; liquidadas: number; valorTotal: number;
  };
  liquidaciones: { total: number; valorTotal: number };
  retiros: {
    total: number; pendientes: number; pagados: number; rechazados: number;
    valorPendiente: number; valorPagado: number; valorRechazado: number;
  };
}

interface ApiResp {
  success: boolean;
  data: ResumenComercio;
}

export function ComercioPage() {
  const { idComercio } = useParams();
  const navigate = useNavigate();
  const [inputId, setInputId] = useState(idComercio ?? '');
  const [data, setData] = useState<ResumenComercio | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!idComercio) { setData(null); setError(''); return; }
    setLoading(true);
    setError('');
    get<ApiResp>(`/api/reportes/comercios/${idComercio}/resumen`)
      .then(r => setData(r.data))
      .catch(err => { setData(null); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [idComercio]);

  function handleSearch(e: FormEvent) {
    e.preventDefault();
    const id = inputId.trim();
    if (id) navigate(`/comercios/${id}`);
  }

  return (
    <div className="page">
      <h2>Resumen Comercio</h2>

      <form className="search-form" onSubmit={handleSearch}>
        <label>ID Comercio</label>
        <input
          type="number"
          value={inputId}
          onChange={e => setInputId(e.target.value)}
          placeholder="ej. 1"
          min={1}
        />
        <button type="submit">Buscar</button>
      </form>

      {loading && <div className="loading">Cargando...</div>}
      {error   && <div className="error-msg">Error: {error}</div>}
      {!idComercio && !loading && <div className="hint">Ingresa un ID de comercio para ver su resumen.</div>}

      {data && (
        <>
          <div className="info-section">
            <h3>Información general</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">ID Comercio</span>     <span className="value">{data.idComercio}</span></div>
              <div className="info-item"><span className="label">Nombre</span>           <span className="value">{data.nombreComercial}</span></div>
              <div className="info-item"><span className="label">ID Wallet</span>        <span className="value">{data.idWalletComercio}</span></div>
              <div className="info-item"><span className="label">Saldo Disponible</span> <span className="value">{fmtMoney(data.saldoDisponible)}</span></div>
            </div>
          </div>

          <div className="info-section">
            <h3>Ventas QR</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">Total</span>       <span className="value">{fmtNum(data.ventasQr.total)}</span></div>
              <div className="info-item"><span className="label">Liquidadas</span>  <span className="value">{fmtNum(data.ventasQr.liquidadas)}</span></div>
              <div className="info-item"><span className="label">Contingencia</span><span className="value">{fmtNum(data.ventasQr.contingencia)}</span></div>
              <div className="info-item"><span className="label">Valor Total</span> <span className="value">{fmtMoney(data.ventasQr.valorTotal)}</span></div>
            </div>
          </div>

          <div className="info-section">
            <h3>Liquidaciones</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">Total</span>       <span className="value">{fmtNum(data.liquidaciones.total)}</span></div>
              <div className="info-item"><span className="label">Valor Total</span> <span className="value">{fmtMoney(data.liquidaciones.valorTotal)}</span></div>
            </div>
          </div>

          <div className="info-section">
            <h3>Retiros</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">Total</span>       <span className="value">{fmtNum(data.retiros.total)}</span></div>
              <div className="info-item"><span className="label">Pendientes</span>  <span className="value badge badge-warn">{data.retiros.pendientes} — {fmtMoney(data.retiros.valorPendiente)}</span></div>
              <div className="info-item"><span className="label">Pagados</span>     <span className="value badge badge-ok">{data.retiros.pagados} — {fmtMoney(data.retiros.valorPagado)}</span></div>
              <div className="info-item"><span className="label">Rechazados</span>  <span className="value badge badge-warn">{data.retiros.rechazados} — {fmtMoney(data.retiros.valorRechazado)}</span></div>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
