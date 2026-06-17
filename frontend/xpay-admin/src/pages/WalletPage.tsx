import { FormEvent, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { get } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

interface Movimiento {
  idMovimiento:   number;
  fecha:          string;
  tipoMovimiento: string;
  naturaleza:     string;
  valor:          number;
  saldoAntes:     number;
  saldoDespues:   number;
  descripcion:    string | null;
  referenciaTipo: string | null;
  referenciaId:   number | null;
}

interface EstadoCuenta {
  idWallet:        number;
  tipoWallet:      string;
  nombreWallet:    string;
  estado:          string;
  saldoDisponible: number;
  movimientos:     Movimiento[];
}

interface ApiResp {
  success: boolean;
  data: EstadoCuenta;
}

export function WalletPage() {
  const { idWallet } = useParams();
  const navigate = useNavigate();
  const [inputId, setInputId] = useState(idWallet ?? '');
  const [data, setData] = useState<EstadoCuenta | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!idWallet) { setData(null); setError(''); return; }
    setLoading(true);
    setError('');
    get<ApiResp>(`/api/reportes/wallet/${idWallet}/estado-cuenta`)
      .then(r => setData(r.data))
      .catch(err => { setData(null); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [idWallet]);

  function handleSearch(e: FormEvent) {
    e.preventDefault();
    const id = inputId.trim();
    if (id) navigate(`/wallets/${id}`);
  }

  return (
    <div className="page">
      <h2>Estado de Cuenta — Wallet</h2>

      <form className="search-form" onSubmit={handleSearch}>
        <label>ID Wallet</label>
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
      {!idWallet && !loading && <div className="hint">Ingresa un ID de wallet para ver su estado de cuenta.</div>}

      {data && (
        <>
          <div className="info-section">
            <h3>Información</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">ID Wallet</span>     <span className="value">{data.idWallet}</span></div>
              <div className="info-item"><span className="label">Tipo</span>           <span className="value">{data.tipoWallet}</span></div>
              <div className="info-item"><span className="label">Nombre</span>         <span className="value">{data.nombreWallet}</span></div>
              <div className="info-item"><span className="label">Estado</span>         <span className="value"><span className={`badge ${data.estado === 'ACTIVO' ? 'badge-ok' : 'badge-warn'}`}>{data.estado}</span></span></div>
              <div className="info-item"><span className="label">Saldo Disponible</span><span className="value">{fmtMoney(data.saldoDisponible)}</span></div>
            </div>
          </div>

          <div className="table-wrapper">
            <div className="table-title">Movimientos ({data.movimientos.length})</div>
            {data.movimientos.length === 0 ? (
              <div className="empty">Sin movimientos registrados.</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>Fecha</th>
                    <th>Tipo</th>
                    <th>Nat.</th>
                    <th>Valor</th>
                    <th>Saldo Antes</th>
                    <th>Saldo Después</th>
                    <th>Descripción</th>
                  </tr>
                </thead>
                <tbody>
                  {data.movimientos.map(m => (
                    <tr key={m.idMovimiento}>
                      <td className="mono">{m.idMovimiento}</td>
                      <td className="mono">{fmtDate(m.fecha)}</td>
                      <td>{m.tipoMovimiento}</td>
                      <td><span className={m.naturaleza === 'D' ? 'debit' : 'credit'}>{m.naturaleza}</span></td>
                      <td className={m.naturaleza === 'D' ? 'debit' : 'credit'}>{fmtMoney(m.valor)}</td>
                      <td>{fmtMoney(m.saldoAntes)}</td>
                      <td>{fmtMoney(m.saldoDespues)}</td>
                      <td>{m.descripcion ?? '—'}</td>
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
