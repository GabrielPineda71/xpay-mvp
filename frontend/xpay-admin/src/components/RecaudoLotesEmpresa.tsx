import { useEffect, useState } from 'react';
import QRCode from 'qrcode';
import { ApiError, HttpUncertainError, get, post } from '../api/client.ts';

// D3 — recaudo empresarial por lote + QR dinámico Passport (LibranzaRecaudoService).
// Ninguna acción de esta pantalla mueve dinero: crear el lote congela los
// anticipos DESEMBOLSADOS del corte, generar el QR solo pide a Passport un QR
// por el total EXACTO, y la aplicación contable ocurre únicamente cuando llega
// un payment.inbound.settled con firma válida (LibranzaRecaudoAplicacionService).
export interface LoteDetalle {
  idDetalle:       number;
  idAnticipo:      number;
  nombresEmpleado: string;
  montoCapital:    number;
  montoComision:   number;
  montoIva:        number;
  montoTotal:      number;
  estado:          string;
}

export interface Lote {
  idLote:            number;
  fechaCorte:        string;
  estado:            string;
  referenciaPublica: string;
  montoTotal:        number;
  cantidadDetalles:  number;
  fechaCreacion:     string;
  qrEstado:          string | null;
  qrCodeData:        string | null;
  fechaAplicacion:   string | null;
  motivoRevision:    string | null;
  detalles:          LoteDetalle[];
}

type ApiOk<T> = { success: boolean; data: T };

// Generar el QR espera a Passport: más margen que el timeout por defecto.
const TIMEOUT_QR_MS = 45_000;

export const DESCRIPCION_ESTADO: Record<string, string> = {
  BORRADOR:          'Lote creado con los anticipos del corte. Falta generar el QR de pago.',
  PENDIENTE_PAGO:    'QR generado. Paga el valor exacto desde la cuenta de la empresa.',
  PAGO_EN_PROCESO:   'Passport reportó el pago; falta la confirmación de fondos.',
  APLICADO:          'Pago confirmado y aplicado: los anticipos del lote quedaron PAGADOS.',
  REQUIERE_REVISION: 'Se recibió un pago que no coincide con el lote. XPAY lo revisará; no vuelvas a pagar.',
  CANCELADO:         'Lote cancelado antes de cualquier pago. Sus anticipos pueden agruparse de nuevo.',
};

export const MENSAJE_QR_INCIERTO =
  'No se confirmó la generación del QR. Espera un minuto y vuelve a intentarlo: se reutiliza la misma referencia y el mismo monto.';

function fmt(n: number) {
  return n.toLocaleString('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
}

export function puedeOperarRecaudo(rol: string): boolean {
  return ['ADMIN_EMPRESA', 'OPERADOR_EMPRESA'].includes(rol.trim().toUpperCase());
}

function mensajeError(err: unknown, porDefecto: string): string {
  if (err instanceof ApiError && err.status === 503) return err.message || 'Operación no disponible en este momento.';
  return err instanceof Error ? err.message : porDefecto;
}

export function RecaudoLotesEmpresa({ idConvenio, rolEmpresa }: { idConvenio: number; rolEmpresa: string }) {
  const opera = puedeOperarRecaudo(rolEmpresa);
  const [lotes,      setLotes]      = useState<Lote[]>([]);
  const [lote,       setLote]       = useState<Lote | null>(null);
  const [fechaCorte, setFechaCorte] = useState('');
  const [qrImagen,   setQrImagen]   = useState<string | null>(null);
  const [ocupado,    setOcupado]    = useState(false);
  const [confirmarCancelar, setConfirmarCancelar] = useState(false);
  const [error,      setError]      = useState('');
  const [aviso,      setAviso]      = useState('');

  const q = `idConvenio=${idConvenio}`;

  async function cargarLotes() {
    try {
      const r = await get<ApiOk<Lote[]>>(`/api/libranza/empresa/lotes?${q}`);
      setLotes(r.data);
    } catch (err) { setError(mensajeError(err, 'Error cargando lotes.')); }
  }

  async function abrirLote(idLote: number) {
    setError(''); setAviso(''); setConfirmarCancelar(false);
    try {
      const r = await get<ApiOk<Lote>>(`/api/libranza/empresa/lotes/${idLote}?${q}`);
      setLote(r.data);
    } catch (err) { setError(mensajeError(err, 'Error cargando el lote.')); }
  }

  useEffect(() => { cargarLotes(); setLote(null); }, [idConvenio]);

  useEffect(() => {
    let vigente = true;
    setQrImagen(null);
    if (lote?.estado === 'PENDIENTE_PAGO' && lote.qrCodeData) {
      QRCode.toDataURL(lote.qrCodeData, { width: 280, margin: 2, color: { dark: '#1a202c' } })
        .then(url => { if (vigente) setQrImagen(url); })
        .catch(() => { if (vigente) setError('No fue posible dibujar el QR; usa la referencia y el valor exacto.'); });
    }
    return () => { vigente = false; };
  }, [lote?.idLote, lote?.estado, lote?.qrCodeData]);

  async function accion(fn: () => Promise<Lote>, ok: string, porDefecto: string, incierto?: string) {
    setOcupado(true); setError(''); setAviso('');
    try {
      const l = await fn();
      setLote(l);
      setAviso(ok);
      setConfirmarCancelar(false);
      await cargarLotes();
    } catch (err) {
      if (incierto && (err instanceof HttpUncertainError || (err instanceof ApiError && err.status === 502)))
        setError(incierto);
      else setError(mensajeError(err, porDefecto));
    } finally { setOcupado(false); }
  }

  function crearLote() {
    if (!fechaCorte) { setError('Indica la fecha de pago (corte) del lote.'); return; }
    accion(async () => (await post<ApiOk<Lote>>(`/api/libranza/empresa/lotes?${q}`, { fechaCorte })).data,
      'Lote listo. Revisa los anticipos incluidos y genera el QR.', 'Error creando el lote.');
  }

  function generarQr(l: Lote) {
    accion(async () => (await post<ApiOk<Lote>>(`/api/libranza/empresa/lotes/${l.idLote}/qr?${q}`, {}, undefined, TIMEOUT_QR_MS)).data,
      'QR generado.', 'Error generando el QR.', MENSAJE_QR_INCIERTO);
  }

  function cancelarLote(l: Lote) {
    accion(async () => (await post<ApiOk<Lote>>(`/api/libranza/empresa/lotes/${l.idLote}/cancelar?${q}`, {})).data,
      'Lote cancelado.', 'Error cancelando el lote.');
  }

  const cancelable = lote && (lote.estado === 'BORRADOR' || lote.estado === 'PENDIENTE_PAGO') && lote.qrEstado !== 'EN_CURSO';

  return (
    <div>
      <h3>Recaudo por lote y QR</h3>
      <p style={{ fontSize: '0.85rem', color: '#4a5568' }}>
        Agrupa los anticipos desembolsados de un corte en un lote y págalo con un único QR por el valor exacto.
        Los anticipos solo quedan PAGADOS cuando Passport confirma los fondos.
      </p>

      {error && <div className="msg-error" role="alert" style={{ marginBottom: '0.75rem' }}>{error}</div>}
      {aviso && <div role="status" style={{ marginBottom: '0.75rem', color: '#166534' }}>{aviso}</div>}

      {opera && (
        <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end', flexWrap: 'wrap', marginBottom: '1rem' }}>
          <label style={{ fontSize: '0.8rem', fontWeight: 500 }}>
            Fecha de pago (corte)
            <input type="date" value={fechaCorte} onChange={e => setFechaCorte(e.target.value)}
              style={{ display: 'block', padding: '0.35rem 0.6rem', marginTop: '0.2rem' }} />
          </label>
          <button type="button" className="btn-search" disabled={ocupado} onClick={crearLote}>Crear lote del corte</button>
        </div>
      )}

      {lotes.length === 0
        ? <p style={{ color: '#718096', fontSize: '0.9rem' }}>No hay lotes de recaudo.</p>
        : (
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.82rem', marginBottom: '1rem' }}>
            <thead><tr>{['Lote', 'Corte', 'Estado', 'Anticipos', 'Total', ''].map(h =>
              <th key={h} style={{ textAlign: 'left', padding: '0.4rem 0.5rem', color: '#718096', borderBottom: '1px solid #e2e8f0' }}>{h}</th>)}
            </tr></thead>
            <tbody>
              {lotes.map(l => (
                <tr key={l.idLote}>
                  <td style={{ padding: '0.4rem 0.5rem' }}>#{l.idLote}</td>
                  <td style={{ padding: '0.4rem 0.5rem' }}>{l.fechaCorte}</td>
                  <td style={{ padding: '0.4rem 0.5rem' }}><span className="badge badge-info">{l.estado}</span></td>
                  <td style={{ padding: '0.4rem 0.5rem' }}>{l.cantidadDetalles}</td>
                  <td style={{ padding: '0.4rem 0.5rem', fontWeight: 600 }}>{fmt(l.montoTotal)}</td>
                  <td style={{ padding: '0.4rem 0.5rem' }}>
                    <button type="button" className="btn-link" onClick={() => abrirLote(l.idLote)}>Ver lote #{l.idLote}</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

      {lote && (
        <section aria-label={`Lote ${lote.idLote}`} style={{ border: '1px solid #e2e8f0', borderRadius: '8px', padding: '1rem' }}>
          <h4 style={{ marginTop: 0 }}>Lote #{lote.idLote} · corte {lote.fechaCorte} · {lote.estado}</h4>
          <p style={{ fontSize: '0.85rem' }}>{DESCRIPCION_ESTADO[lote.estado] ?? lote.estado}</p>
          {lote.motivoRevision && <p style={{ fontSize: '0.8rem', color: '#92400e' }}>Motivo: {lote.motivoRevision}</p>}
          <div style={{ fontSize: '0.9rem', marginBottom: '0.75rem' }}>
            Valor exacto a pagar: <strong>{fmt(lote.montoTotal)}</strong> · Referencia: <code>{lote.referenciaPublica}</code>
          </div>

          {qrImagen && (
            <img src={qrImagen} alt={`QR de pago del lote ${lote.idLote}`} width={280} height={280}
              style={{ display: 'block', marginBottom: '0.75rem' }} />
          )}

          <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', marginBottom: '0.75rem' }}>
            {opera && lote.estado === 'BORRADOR' && (
              <button type="button" className="btn-search" disabled={ocupado} onClick={() => generarQr(lote)}>
                {ocupado ? 'Procesando...' : 'Generar QR de pago'}
              </button>
            )}
            <button type="button" className="btn-link" disabled={ocupado} onClick={() => abrirLote(lote.idLote)}>
              Actualizar estado
            </button>
            {opera && cancelable && !confirmarCancelar && (
              <button type="button" className="btn-link" disabled={ocupado} onClick={() => setConfirmarCancelar(true)}>
                Cancelar lote
              </button>
            )}
          </div>

          {confirmarCancelar && lote && (
            <div role="group" aria-label="Confirmar cancelación" style={{ background: '#fff5f5', padding: '0.6rem', borderRadius: '6px', marginBottom: '0.75rem' }}>
              <p style={{ margin: '0 0 0.4rem', fontSize: '0.85rem' }}>
                Cancela el lote solo si la empresa NO ha pagado el QR. Los anticipos quedarán disponibles para otro lote.
              </p>
              <button type="button" className="btn-search" disabled={ocupado} onClick={() => cancelarLote(lote)}>Confirmar cancelación</button>{' '}
              <button type="button" className="btn-link" disabled={ocupado} onClick={() => setConfirmarCancelar(false)}>Volver</button>
            </div>
          )}

          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
            <thead><tr>{['Anticipo', 'Empleado', 'Capital', 'Comisión', 'IVA', 'Total', 'Estado'].map(h =>
              <th key={h} style={{ textAlign: 'left', padding: '0.3rem 0.5rem', color: '#718096', borderBottom: '1px solid #e2e8f0' }}>{h}</th>)}
            </tr></thead>
            <tbody>
              {lote.detalles.map(d => (
                <tr key={d.idDetalle}>
                  <td style={{ padding: '0.3rem 0.5rem' }}>#{d.idAnticipo}</td>
                  <td style={{ padding: '0.3rem 0.5rem' }}>{d.nombresEmpleado}</td>
                  <td style={{ padding: '0.3rem 0.5rem' }}>{fmt(d.montoCapital)}</td>
                  <td style={{ padding: '0.3rem 0.5rem' }}>{fmt(d.montoComision)}</td>
                  <td style={{ padding: '0.3rem 0.5rem' }}>{fmt(d.montoIva)}</td>
                  <td style={{ padding: '0.3rem 0.5rem', fontWeight: 600 }}>{fmt(d.montoTotal)}</td>
                  <td style={{ padding: '0.3rem 0.5rem' }}>{d.estado}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  );
}
