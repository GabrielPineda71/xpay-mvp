import { FormEvent, useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { get, post } from '../api/client.ts';
import { useAuth } from '../auth/AuthContext.tsx';
import { fmtDate, fmtMoney } from '../utils.ts';

interface Retiro {
  idRetiro:          number;
  idComercio:        number;
  idWalletComercio:  number;
  valor:             number;
  estado:            string;
  medioRetiro:       string | null;
  banco:             string | null;
  tipoCuenta:        string | null;
  numeroCuenta:      string | null;
  titularCuenta:     string | null;
  documentoTitular:  string | null;
  observacion:       string | null;
  fechaSolicitud:    string;
  fechaPago:         string | null;
  referenciaPago:    string | null;
  fechaRechazo:      string | null;
  motivoRechazo:     string | null;
}

interface ApiResp { success: boolean; data: Retiro; }
interface ActionResp { success: boolean; message?: string; data?: { estado: string } }

function estadoBadge(estado: string) {
  const cls = estado === 'PAGADO' ? 'badge-ok' : estado === 'PENDIENTE' ? 'badge-info' : 'badge-warn';
  return <span className={`badge ${cls}`}>{estado}</span>;
}

export function RetiroPage() {
  const { idRetiro } = useParams();
  const { user } = useAuth();
  const navigate = useNavigate();

  const [inputId,  setInputId]  = useState(idRetiro ?? '');
  const [data,     setData]     = useState<Retiro | null>(null);
  const [error,    setError]    = useState('');
  const [loading,  setLoading]  = useState(false);

  // Confirmar pago
  const [cRef,     setCRef]     = useState('');
  const [cObs,     setCObs]     = useState('');
  const [cLoading, setCLoading] = useState(false);
  const [cSuccess, setCSuccess] = useState('');
  const [cError,   setCError]   = useState('');

  // Rechazar
  const [rMotivo,  setRMotivo]  = useState('');
  const [rObs,     setRObs]     = useState('');
  const [rLoading, setRLoading] = useState(false);
  const [rSuccess, setRSuccess] = useState('');
  const [rError,   setRError]   = useState('');

  const fetchRetiro = useCallback(() => {
    if (!idRetiro) return;
    setLoading(true);
    setError('');
    get<ApiResp>(`/api/comercios/retiros/${idRetiro}`)
      .then(r => setData(r.data))
      .catch(err => { setData(null); setError((err as Error).message); })
      .finally(() => setLoading(false));
  }, [idRetiro]);

  useEffect(() => { fetchRetiro(); }, [fetchRetiro]);

  function handleSearch(e: FormEvent) {
    e.preventDefault();
    const id = inputId.trim();
    if (id) navigate(`/retiros/${id}`);
  }

  async function handleConfirmar(e: FormEvent) {
    e.preventDefault();
    if (!data) return;
    setCLoading(true);
    setCError('');
    setCSuccess('');
    try {
      const resp = await post<ActionResp>('/api/comercios/retiros/confirmar-pago', {
        idRetiro:       data.idRetiro,
        referenciaPago: cRef.trim() || undefined,
        observacion:    cObs.trim() || undefined,
        creadoPor:      user?.idUsuario ?? undefined,
      });
      if (!resp.success) throw new Error(resp.message ?? 'Error al confirmar pago');
      setCSuccess(`Pago confirmado. Estado: ${resp.data?.estado ?? 'PAGADO'}`);
      setCRef('');
      setCObs('');
      fetchRetiro();
    } catch (err) {
      setCError((err as Error).message);
    } finally {
      setCLoading(false);
    }
  }

  async function handleRechazar(e: FormEvent) {
    e.preventDefault();
    if (!data) return;
    setRLoading(true);
    setRError('');
    setRSuccess('');
    try {
      const resp = await post<ActionResp>('/api/comercios/retiros/rechazar', {
        idRetiro:     data.idRetiro,
        motivoRechazo: rMotivo.trim() || undefined,
        observacion:   rObs.trim() || undefined,
        creadoPor:     user?.idUsuario ?? undefined,
      });
      if (!resp.success) throw new Error(resp.message ?? 'Error al rechazar retiro');
      setRSuccess(`Retiro rechazado. Estado: ${resp.data?.estado ?? 'RECHAZADO'}`);
      setRMotivo('');
      setRObs('');
      fetchRetiro();
    } catch (err) {
      setRError((err as Error).message);
    } finally {
      setRLoading(false);
    }
  }

  const isPendiente = data?.estado === 'PENDIENTE';

  return (
    <div className="page">
      <h2>Gestión de Retiro</h2>

      <form className="search-form" onSubmit={handleSearch}>
        <label>ID Retiro</label>
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
      {!idRetiro && !loading && (
        <div className="hint">Ingresa un ID de retiro para ver su detalle y gestionar su estado.</div>
      )}

      {data && (
        <>
          <div className="info-section">
            <h3>Identificación</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">ID Retiro</span>        <span className="value">{data.idRetiro}</span></div>
              <div className="info-item"><span className="label">ID Comercio</span>       <span className="value">{data.idComercio}</span></div>
              <div className="info-item"><span className="label">ID Wallet Comercio</span><span className="value">{data.idWalletComercio}</span></div>
              <div className="info-item"><span className="label">Valor</span>             <span className="value">{fmtMoney(data.valor)}</span></div>
              <div className="info-item"><span className="label">Estado</span>            <span className="value">{estadoBadge(data.estado)}</span></div>
              <div className="info-item"><span className="label">Fecha Solicitud</span>   <span className="value">{fmtDate(data.fechaSolicitud)}</span></div>
            </div>
          </div>

          <div className="info-section">
            <h3>Datos bancarios</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">Medio</span>             <span className="value">{data.medioRetiro    ?? '—'}</span></div>
              <div className="info-item"><span className="label">Banco</span>             <span className="value">{data.banco          ?? '—'}</span></div>
              <div className="info-item"><span className="label">Tipo Cuenta</span>       <span className="value">{data.tipoCuenta      ?? '—'}</span></div>
              <div className="info-item"><span className="label">Número Cuenta</span>     <span className="value">{data.numeroCuenta    ?? '—'}</span></div>
              <div className="info-item"><span className="label">Titular</span>           <span className="value">{data.titularCuenta   ?? '—'}</span></div>
              <div className="info-item"><span className="label">Documento Titular</span> <span className="value">{data.documentoTitular ?? '—'}</span></div>
            </div>
          </div>

          <div className="info-section">
            <h3>Resultado</h3>
            <div className="info-grid">
              <div className="info-item"><span className="label">Observación</span>    <span className="value">{data.observacion   ?? '—'}</span></div>
              <div className="info-item"><span className="label">Fecha Pago</span>     <span className="value">{fmtDate(data.fechaPago)}</span></div>
              <div className="info-item"><span className="label">Referencia Pago</span><span className="value">{data.referenciaPago ?? '—'}</span></div>
              <div className="info-item"><span className="label">Fecha Rechazo</span>  <span className="value">{fmtDate(data.fechaRechazo)}</span></div>
              <div className="info-item"><span className="label">Motivo Rechazo</span> <span className="value">{data.motivoRechazo  ?? '—'}</span></div>
            </div>
          </div>

          {isPendiente && (
            <div className="action-row">
              <div className="action-section">
                <h3>Confirmar pago</h3>
                {cSuccess && <div className="success-msg">{cSuccess}</div>}
                {cError   && <div className="error-msg">Error: {cError}</div>}
                <form className="action-form" onSubmit={handleConfirmar}>
                  <label>
                    Referencia de pago
                    <input
                      value={cRef}
                      onChange={e => setCRef(e.target.value)}
                      placeholder="ej. TRX-001"
                    />
                  </label>
                  <label>
                    Observación
                    <textarea
                      value={cObs}
                      onChange={e => setCObs(e.target.value)}
                      rows={2}
                      placeholder="Confirmado desde XPAY Admin"
                    />
                  </label>
                  <button type="submit" className="btn-confirm" disabled={cLoading}>
                    {cLoading ? 'Procesando...' : 'Confirmar pago'}
                  </button>
                </form>
              </div>

              <div className="action-section">
                <h3>Rechazar retiro</h3>
                {rSuccess && <div className="success-msg">{rSuccess}</div>}
                {rError   && <div className="error-msg">Error: {rError}</div>}
                <form className="action-form" onSubmit={handleRechazar}>
                  <label>
                    Motivo de rechazo
                    <input
                      value={rMotivo}
                      onChange={e => setRMotivo(e.target.value)}
                      placeholder="ej. Cuenta bancaria inválida"
                    />
                  </label>
                  <label>
                    Observación
                    <textarea
                      value={rObs}
                      onChange={e => setRObs(e.target.value)}
                      rows={2}
                      placeholder="Rechazado desde XPAY Admin"
                    />
                  </label>
                  <button type="submit" className="btn-reject" disabled={rLoading}>
                    {rLoading ? 'Procesando...' : 'Rechazar retiro'}
                  </button>
                </form>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
