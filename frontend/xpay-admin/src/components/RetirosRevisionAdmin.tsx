import { useEffect, useState } from 'react';
import { get, post } from '../api/client.ts';
import { fmtDate, fmtMoney } from '../utils.ts';

// D5 — gestión de retiros Bre-B reales que requieren intervención humana.
// Las tres acciones ya existen en el backend con autorización ADMIN_XPAY /
// SUPERUSUARIO, auditoría ATTEMPT/OK/RECHAZADO, lock de la fila y
// precondiciones validadas en servidor (BrebPaymentService):
//   · consultar estado: pregunta a Passport (estado externo) por un retiro con payment_id;
//   · liberar huérfano: PENDIENTE_ENVIO_PASSPORT > 15 min, nunca llegó a Passport;
//   · rechazo confirmado: ENVIADO_PASSPORT sin payment_id, > 2 h, resolution vencida y
//     evidencia FIRME de Passport aportada por el admin (20-500 caracteres).
// Esta pantalla no decide nada por su cuenta: solo exige confirmación explícita.
export interface RetiroRevision {
  idBrebRetiro:         number;
  valor:                number;
  estado:               string;
  fechaSolicitud:       string;
  fechaEnvioPassport?:  string | null;
  paymentIdFingerprint?: string | null;
}

export interface EnvioIncierto {
  idBrebRetiro:        number;
  valor:               number;
  estado:              string;
  fechaSolicitud:      string;
  fechaEnvioPassport:  string | null;
  resolucionExpiraUtc: string | null;
  antiguedadMinutos:   number;
}

export const UMBRAL_HUERFANO_MIN = 15;
export const EVIDENCIA_MIN = 20;
export const EVIDENCIA_MAX = 500;

type Accion =
  | { tipo: 'liberar'; id: number }
  | { tipo: 'rechazo'; id: number };

export function esHuerfano(r: RetiroRevision, ahora: Date): boolean {
  return r.estado === 'PENDIENTE_ENVIO_PASSPORT'
    && ahora.getTime() - new Date(r.fechaSolicitud).getTime() > UMBRAL_HUERFANO_MIN * 60_000;
}

export function resolucionVencida(e: EnvioIncierto, ahora: Date): boolean {
  return e.resolucionExpiraUtc !== null && new Date(e.resolucionExpiraUtc).getTime() < ahora.getTime();
}

export function RetirosRevisionAdmin({ retiros, onCambio }: { retiros: RetiroRevision[]; onCambio: () => void }) {
  const [inciertos, setInciertos] = useState<EnvioIncierto[]>([]);
  const [accion,    setAccion]    = useState<Accion | null>(null);
  const [evidencia, setEvidencia] = useState('');
  const [certifico, setCertifico] = useState(false);
  const [ocupado,   setOcupado]   = useState<number | null>(null);
  const [error,     setError]     = useState('');
  const [aviso,     setAviso]     = useState('');

  function cargarInciertos() {
    get<{ success: boolean; data: EnvioIncierto[] }>('/api/breb/admin/retiros/envio-incierto')
      .then(r => setInciertos(r.data))
      .catch(err => setError((err as Error).message));
  }
  useEffect(cargarInciertos, []);

  const ahora = new Date();
  const huerfanos = retiros.filter(r => esHuerfano(r, ahora));
  const consultables = retiros.filter(r => r.estado === 'ENVIADO_PASSPORT' && r.paymentIdFingerprint);

  function abrir(a: Accion) { setAccion(a); setEvidencia(''); setCertifico(false); setError(''); setAviso(''); }

  async function ejecutar(id: number, url: string, body: unknown, ok: string) {
    setOcupado(id); setError(''); setAviso('');
    try {
      const r = await post<{ success: boolean; data?: { estado?: string } }>(url, body);
      setAviso(`${ok} Estado actual del retiro #${id}: ${r.data?.estado ?? '—'}.`);
      setAccion(null);
      cargarInciertos();
      onCambio();
    } catch (err) {
      setError(`Retiro #${id}: ${(err as Error).message}`);
    } finally { setOcupado(null); }
  }

  function confirmarRechazo(id: number) {
    const ev = evidencia.trim();
    if (ev.length < EVIDENCIA_MIN || ev.length > EVIDENCIA_MAX) {
      setError(`La evidencia de Passport es obligatoria (${EVIDENCIA_MIN}-${EVIDENCIA_MAX} caracteres).`); return;
    }
    if (!certifico) { setError('Debes certificar que Passport confirmó que el pago no existe.'); return; }
    ejecutar(id, `/api/breb/admin/retiros/${id}/resolver-envio-incierto/rechazo-confirmado`, { evidencia: ev },
      'Rechazo confirmado registrado: el saldo retenido volvió al usuario.');
  }

  const nada = inciertos.length === 0 && huerfanos.length === 0 && consultables.length === 0;

  return (
    <section aria-label="Retiros que requieren revisión" style={{ marginBottom: '1.5rem' }}>
      <h3>Retiros que requieren revisión</h3>
      {error && <div className="error-msg" role="alert" style={{ marginBottom: '0.75rem' }}>{error}</div>}
      {aviso && <div role="status" style={{ marginBottom: '0.75rem', color: '#276749' }}>{aviso}</div>}
      {nada && <div className="empty">No hay retiros pendientes de revisión.</div>}

      {consultables.length > 0 && (
        <div style={{ marginBottom: '1rem' }}>
          <h4>Enviados a Passport (con identificador de pago)</h4>
          <p style={{ fontSize: '0.8rem', color: '#718096' }}>El estado se obtiene consultando a Passport; nunca se decide a mano.</p>
          {consultables.map(r => (
            <div key={r.idBrebRetiro} style={{ display: 'flex', gap: '0.75rem', alignItems: 'center', marginBottom: '0.3rem' }}>
              <span className="mono">#{r.idBrebRetiro}</span><span>{fmtMoney(r.valor)}</span>
              <span className="mono">{fmtDate(r.fechaEnvioPassport ?? r.fechaSolicitud)}</span>
              <button type="button" className="btn-link" disabled={ocupado === r.idBrebRetiro}
                onClick={() => ejecutar(r.idBrebRetiro, `/api/breb/admin/retiros/${r.idBrebRetiro}/actualizar-estado`, {},
                  'Estado consultado en Passport.')}>
                Consultar estado en Passport #{r.idBrebRetiro}
              </button>
            </div>
          ))}
        </div>
      )}

      {huerfanos.length > 0 && (
        <div style={{ marginBottom: '1rem' }}>
          <h4>Pendientes de envío sin llegar a Passport (&gt; {UMBRAL_HUERFANO_MIN} min)</h4>
          {huerfanos.map(r => (
            <div key={r.idBrebRetiro} style={{ marginBottom: '0.5rem' }}>
              <span className="mono">#{r.idBrebRetiro}</span> · {fmtMoney(r.valor)} · solicitado {fmtDate(r.fechaSolicitud)}{' '}
              {accion?.tipo === 'liberar' && accion.id === r.idBrebRetiro ? (
                <span role="group" aria-label={`Confirmar liberación ${r.idBrebRetiro}`}>
                  <span style={{ fontSize: '0.8rem' }}> El retiro pasará a ERROR y el saldo retenido volverá al usuario. </span>
                  <button type="button" className="btn-link" disabled={ocupado === r.idBrebRetiro}
                    onClick={() => ejecutar(r.idBrebRetiro, `/api/breb/admin/retiros/${r.idBrebRetiro}/liberar-pendiente-envio`, {},
                      'Retiro huérfano liberado.')}>
                    Confirmar liberación
                  </button>{' '}
                  <button type="button" className="btn-link" onClick={() => setAccion(null)}>Volver</button>
                </span>
              ) : (
                <button type="button" className="btn-link" onClick={() => abrir({ tipo: 'liberar', id: r.idBrebRetiro })}>
                  Liberar retiro huérfano #{r.idBrebRetiro}
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      {inciertos.length > 0 && (
        <div>
          <h4>Envíos inciertos (&gt; 2 h sin identificador de pago)</h4>
          <p style={{ fontSize: '0.8rem', color: '#718096' }}>
            Los fondos siguen retenidos. Solo se liberan con evidencia firme de Passport de que el pago no existe
            y con la resolución vencida; si no tienes esa evidencia, no hagas nada y escala con Passport.
          </p>
          {inciertos.map(e => {
            const vencida = resolucionVencida(e, ahora);
            const abierta = accion?.tipo === 'rechazo' && accion.id === e.idBrebRetiro;
            return (
              <div key={e.idBrebRetiro} style={{ border: '1px solid #e2e8f0', borderRadius: '6px', padding: '0.6rem', marginBottom: '0.5rem' }}>
                <div>
                  <span className="mono">#{e.idBrebRetiro}</span> · {fmtMoney(e.valor)} · enviado {fmtDate(e.fechaEnvioPassport)} ·{' '}
                  {Math.floor(e.antiguedadMinutos / 60)} h · resolución {e.resolucionExpiraUtc ? `vence ${fmtDate(e.resolucionExpiraUtc)}` : 'sin vencimiento conocido'}
                </div>
                {!vencida && (
                  <p style={{ fontSize: '0.8rem', color: '#92400e', margin: '0.3rem 0 0' }}>
                    La resolución no ha vencido o se desconoce su vencimiento: el pago original aún podría ejecutarse. No se puede resolver todavía.
                  </p>
                )}
                {vencida && !abierta && (
                  <button type="button" className="btn-link" onClick={() => abrir({ tipo: 'rechazo', id: e.idBrebRetiro })}>
                    Resolver como rechazo confirmado #{e.idBrebRetiro}
                  </button>
                )}
                {vencida && abierta && (
                  <div role="group" aria-label={`Rechazo confirmado ${e.idBrebRetiro}`} style={{ marginTop: '0.5rem' }}>
                    <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 600 }}>
                      Evidencia de Passport (número de caso, fecha y respuesta escrita)
                      <textarea value={evidencia} onChange={ev => setEvidencia(ev.target.value)} maxLength={EVIDENCIA_MAX} rows={3}
                        style={{ display: 'block', width: '100%', marginTop: '0.2rem' }} />
                    </label>
                    <div style={{ fontSize: '0.75rem', color: '#718096' }}>{evidencia.trim().length}/{EVIDENCIA_MAX} (mínimo {EVIDENCIA_MIN})</div>
                    <label style={{ display: 'block', fontSize: '0.8rem', margin: '0.4rem 0' }}>
                      <input type="checkbox" checked={certifico} onChange={ev => setCertifico(ev.target.checked)} />{' '}
                      Certifico que Passport confirmó por escrito que este pago no fue creado ni ejecutado.
                    </label>
                    <button type="button" className="btn-search" disabled={ocupado === e.idBrebRetiro}
                      onClick={() => confirmarRechazo(e.idBrebRetiro)}>
                      Registrar rechazo confirmado
                    </button>{' '}
                    <button type="button" className="btn-link" onClick={() => setAccion(null)}>Volver</button>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}
