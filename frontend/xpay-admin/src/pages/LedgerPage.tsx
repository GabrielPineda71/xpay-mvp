import { FormEvent, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

interface LedgerMovimiento {
  idMovimiento: number;
  idCuenta:     number;
  naturaleza:   string;
  valor:        number;
  concepto:     string | null;
  descripcion:  string | null;
  fecha:        string;
}

interface LedgerTransaccion {
  idTransaccion:   number;
  tipoTransaccion: string;
  descripcion:     string | null;
  valorTotal:      number;
  estado:          string;
  fecha:           string;
  totalDebitos:    number;
  totalCreditos:   number;
  balanceado:      boolean;
  movimientos:     LedgerMovimiento[];
}

interface ApiResp {
  success: boolean;
  data: LedgerTransaccion;
}

export function LedgerPage() {
  const { idTransaccion } = useParams();
  const navigate = useNavigate();
  const [inputId, setInputId] = useState(idTransaccion ?? '');
  const [data, setData] = useState<LedgerTransaccion | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!idTransaccion) { setData(null); setError(''); return; }
    setLoading(true);
    setError('');
    get<ApiResp>(`/api/reportes/ledger/transaccion/${idTransaccion}`)
      .then(r => setData(r.data))
      .catch(err => { setData(null); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [idTransaccion]);

  function handleSearch(e: FormEvent) {
    e.preventDefault();
    const id = inputId.trim();
    if (id) navigate(`/ledger/${id}`);
  }

  return (
    <div className="page">
      <h2>Detalle Transacción Ledger</h2>

      <form className="search-form" onSubmit={handleSearch}>
        <label>ID Transacción</label>
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
      {!idTransaccion && !loading && <div className="hint">Ingresa un ID de transacción para ver su detalle en el ledger.</div>}

      {data && (
        <>
          <div className="info-section">
            <h3>Cabecera</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">ID</span>              <span className="value">{data.idTransaccion}</span></div>
              <div className="info-item"><span className="label">Tipo</span>            <span className="value">{data.tipoTransaccion}</span></div>
              <div className="info-item"><span className="label">Estado</span>          <span className="value"><span className={`badge ${data.estado === 'CONFIRMADA' ? 'badge-ok' : 'badge-info'}`}>{data.estado}</span></span></div>
              <div className="info-item"><span className="label">Fecha</span>           <span className="value">{fmtDate(data.fecha)}</span></div>
              <div className="info-item"><span className="label">Valor Total</span>     <span className="value">{fmtMoney(data.valorTotal)}</span></div>
              <div className="info-item"><span className="label">Total Débitos</span>   <span className="value debit">{fmtMoney(data.totalDebitos)}</span></div>
              <div className="info-item"><span className="label">Total Créditos</span>  <span className="value credit">{fmtMoney(data.totalCreditos)}</span></div>
              <div className="info-item">
                <span className="label">Balanceado</span>
                <span className="value"><span className={`badge ${data.balanceado ? 'badge-ok' : 'badge-warn'}`}>{data.balanceado ? 'SÍ' : 'NO'}</span></span>
              </div>
            </div>
          </div>

          <div className="table-wrapper">
            <div className="table-title">Movimientos ledger ({data.movimientos.length})</div>
            {data.movimientos.length === 0 ? (
              <div className="empty">Sin movimientos.</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>ID Cuenta</th>
                    <th>Nat.</th>
                    <th>Valor</th>
                    <th>Concepto</th>
                    <th>Descripción</th>
                    <th>Fecha</th>
                  </tr>
                </thead>
                <tbody>
                  {data.movimientos.map(m => (
                    <tr key={m.idMovimiento}>
                      <td className="mono">{m.idMovimiento}</td>
                      <td className="mono">{m.idCuenta}</td>
                      <td><span className={m.naturaleza === 'D' ? 'debit' : 'credit'}>{m.naturaleza}</span></td>
                      <td className={m.naturaleza === 'D' ? 'debit' : 'credit'}>{fmtMoney(m.valor)}</td>
                      <td>{m.concepto ?? '—'}</td>
                      <td>{m.descripcion ?? '—'}</td>
                      <td className="mono">{fmtDate(m.fecha)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      )}
    </div>
  );
}
