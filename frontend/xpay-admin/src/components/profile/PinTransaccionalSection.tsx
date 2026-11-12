import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { get, post, ApiError } from '../../api/client.ts';

interface EstadoPin {
  configurado: boolean;
  bloqueado: boolean;
  restablecido: boolean;
  intentosRestantes: number;
}
interface Msg { ok: boolean; text: string; }

const VACIO = { claveActual: '', pinActual: '', pin: '', confirmacion: '' };
const soloDigitos = (v: string) => v.replace(/\D/g, '').slice(0, 7);

// PIN transaccional — sección de Perfil: crear el PIN (con la contraseña de
// ingreso) o cambiarlo (con el PIN actual). Consume GET /api/pin/estado,
// POST /api/pin y POST /api/pin/cambiar; el PIN se verifica siempre en el
// backend. Los campos se limpian al terminar y nunca se guardan en storage.
export function PinTransaccionalSection() {
  const [estado, setEstado] = useState<EstadoPin | null>(null);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);
  const [abierto, setAbierto] = useState(false);
  const [form, setForm] = useState(VACIO);
  const [errorLocal, setErrorLocal] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [mensaje, setMensaje] = useState<Msg | null>(null);

  const cargar = useCallback(async () => {
    try {
      const r = await get<{ success: boolean; data: EstadoPin }>('/api/pin/estado');
      setEstado(r.data);
      setErrorCarga(null);
    } catch (err) {
      setErrorCarga(err instanceof Error ? err.message : 'No se pudo consultar tu PIN.');
    }
  }, []);

  useEffect(() => { void cargar(); }, [cargar]);

  const crear = !estado?.configurado;

  function abrir() {
    setForm(VACIO);
    setErrorLocal(null);
    setMensaje(null);
    setAbierto(true);
  }

  function cerrar() {
    setForm(VACIO);
    setAbierto(false);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (crear ? !form.claveActual : form.pinActual.length !== 7) {
      setErrorLocal(crear ? 'Ingresa tu contraseña de ingreso.' : 'Ingresa tu PIN actual de 7 dígitos.');
      return;
    }
    if (form.pin.length !== 7) { setErrorLocal('El PIN debe tener exactamente 7 dígitos.'); return; }
    if (form.pin !== form.confirmacion) { setErrorLocal('El PIN y su confirmación no coinciden.'); return; }
    setErrorLocal(null);
    setMensaje(null);
    setEnviando(true);
    try {
      const r = crear
        ? await post<{ success: boolean; message?: string }>('/api/pin', {
            claveActual: form.claveActual, pin: form.pin, confirmacionPin: form.confirmacion,
          })
        : await post<{ success: boolean; message?: string }>('/api/pin/cambiar', {
            pinActual: form.pinActual, pinNuevo: form.pin, confirmacionPinNuevo: form.confirmacion,
          });
      setForm(VACIO);
      setAbierto(false);
      setMensaje({ ok: true, text: r.message ?? 'PIN actualizado.' });
    } catch (err) {
      setMensaje({
        ok: false,
        text: err instanceof ApiError && err.status === 429
          ? 'Has realizado varios intentos. Intenta nuevamente más tarde.'
          : (err instanceof Error && err.message) || 'No se pudo guardar el PIN. Intenta nuevamente.',
      });
    } finally {
      setEnviando(false);
      await cargar();
    }
  }

  return (
    <section className="profile-section" aria-label="PIN transaccional">
      <h3 className="profile-section-title">PIN transaccional</h3>
      <p className="profile-placeholder-note">
        Lo pedimos para enviar dinero, pagar con QR, solicitar anticipos y retirar a tu llave Bre-B.
        Tras 5 intentos fallidos se bloquea y debes pedir a XPAY su restablecimiento.
      </p>

      {errorCarga && <span className="breb-msg-err">{errorCarga}</span>}
      {estado && (
        <p className="profile-placeholder-note" data-testid="pin-estado">
          {estado.bloqueado
            ? 'Tu PIN está bloqueado por intentos fallidos. Solicita a XPAY su restablecimiento.'
            : estado.configurado
              ? 'Tienes un PIN transaccional activo.'
              : estado.restablecido
                ? 'Tu PIN fue restablecido por XPAY. Crea uno nuevo para volver a operar.'
                : 'Aún no tienes PIN transaccional. Créalo para poder operar.'}
        </p>
      )}

      {estado && !estado.bloqueado && (!abierto ? (
        <button type="button" className="btn-breb" onClick={abrir}>
          {crear ? 'Crear PIN' : 'Cambiar PIN'}
        </button>
      ) : (
        <form className="profile-edit-form" onSubmit={e => void handleSubmit(e)} autoComplete="off" noValidate>
          {crear ? (
            <label>
              Contraseña de ingreso
              <input
                type="password"
                value={form.claveActual}
                onChange={e => setForm(f => ({ ...f, claveActual: e.target.value }))}
                autoComplete="current-password"
              />
            </label>
          ) : (
            <label>
              PIN actual
              <input
                type="password" inputMode="numeric" maxLength={7} autoComplete="off"
                value={form.pinActual}
                onChange={e => setForm(f => ({ ...f, pinActual: soloDigitos(e.target.value) }))}
              />
            </label>
          )}
          <label>
            {crear ? 'Nuevo PIN (7 dígitos)' : 'PIN nuevo (7 dígitos)'}
            <input
              type="password" inputMode="numeric" maxLength={7} autoComplete="off"
              value={form.pin}
              onChange={e => setForm(f => ({ ...f, pin: soloDigitos(e.target.value) }))}
            />
          </label>
          <label>
            Confirmar PIN
            <input
              type="password" inputMode="numeric" maxLength={7} autoComplete="off"
              value={form.confirmacion}
              onChange={e => setForm(f => ({ ...f, confirmacion: soloDigitos(e.target.value) }))}
            />
          </label>
          <p className="profile-placeholder-note">No uses dígitos repetidos ni consecutivos (por ejemplo 1111111 o 1234567).</p>

          {errorLocal && <span className="breb-msg-err">{errorLocal}</span>}
          {mensaje && !mensaje.ok && <span className="breb-msg-err">{mensaje.text}</span>}

          <div className="profile-edit-actions">
            <button type="submit" className="btn-breb" disabled={enviando}>
              {enviando ? 'Guardando...' : crear ? 'Crear PIN' : 'Cambiar PIN'}
            </button>
            <button type="button" className="wallet-send-link-btn" onClick={cerrar} disabled={enviando}>
              Cancelar
            </button>
          </div>
        </form>
      ))}

      {mensaje && mensaje.ok && <p className="profile-save-feedback" role="status">{mensaje.text}</p>}
      {mensaje && !mensaje.ok && !abierto && <span className="breb-msg-err">{mensaje.text}</span>}
    </section>
  );
}
