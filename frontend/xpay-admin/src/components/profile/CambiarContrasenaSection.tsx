import { useState, type FormEvent } from 'react';
import { post, ApiError } from '../../api/client.ts';

interface Msg { ok: boolean; text: string; }

const FORM_VACIO = { claveActual: '', claveNueva: '', confirmacion: '' };

// XPAY-402 — detección de 429 por status HTTP estructurado (ApiError.status),
// ya NO por coincidencia de texto con el mensaje del backend (esa
// dependencia frágil, introducida en XPAY-401, queda eliminada aquí — ver
// api/client.ts, que ahora preserva `status` en todo error HTTP no exitoso).
function esRateLimit(err: unknown): boolean {
  return err instanceof ApiError && err.status === 429;
}

// XPAY-401 — sección "Seguridad" de Perfil: cambio VOLUNTARIO de
// contraseña. Consume EXCLUSIVAMENTE POST /api/auth/cambiar-clave (XPAY-400,
// sin cambios de backend). "confirmación" es SOLO validación de UX local —
// el backend nunca la recibe (ver handleSubmit: el body enviado tiene
// únicamente claveActual/claveNueva).
export function CambiarContrasenaSection() {
  const [abierto, setAbierto] = useState(false);
  const [form, setForm] = useState(FORM_VACIO);
  const [errorLocal, setErrorLocal] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [mensaje, setMensaje] = useState<Msg | null>(null);

  function abrir() {
    setForm(FORM_VACIO);
    setErrorLocal(null);
    setMensaje(null);
    setAbierto(true);
  }

  // Limpieza explícita de los 3 campos — nunca quedan en memoria de React
  // más tiempo del necesario, y nunca se persisten en localStorage/
  // sessionStorage/URL (XPAY-401 PASO 9) — este componente no usa ninguno
  // de esos mecanismos para el formulario en ningún momento.
  function cerrarYLimpiar() {
    setForm(FORM_VACIO);
    setAbierto(false);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();

    if (!form.claveActual || !form.claveNueva || !form.confirmacion) {
      setErrorLocal('Completa los tres campos.');
      return;
    }
    if (form.claveNueva !== form.confirmacion) {
      setErrorLocal('La nueva contraseña y su confirmación no coinciden.');
      return;
    }
    setErrorLocal(null);
    setMensaje(null);
    setEnviando(true);
    try {
      // Body EXACTO del contrato XPAY-400 — nunca "confirmacion", nunca
      // idUsuario/idPersona/username (la identidad sale del JWT en el
      // backend, no de este body).
      const r = await post<{ success: boolean; message?: string }>('/api/auth/cambiar-clave', {
        claveActual: form.claveActual,
        claveNueva: form.claveNueva,
      });
      if (r.success) {
        setForm(FORM_VACIO);
        setAbierto(false);
        setMensaje({ ok: true, text: r.message ?? 'Contraseña actualizada correctamente.' });
      } else {
        setMensaje({ ok: false, text: r.message ?? 'No se pudo actualizar la contraseña.' });
      }
    } catch (err) {
      const texto = err instanceof Error ? err.message : '';
      setMensaje({
        ok: false,
        text: esRateLimit(err)
          ? 'Has realizado varios intentos. Intenta nuevamente más tarde.'
          // XPAY-401/402 PASO 10 — el mensaje ya viene saneado desde el
          // backend (BadRequest con InvalidOperationException.Message: "La
          // contraseña actual no coincide.", política incumplida, etc.) o,
          // para un 500, api/client.ts ya normaliza a "Error HTTP 500" — en
          // ningún caso se expone un stack trace ni detalle interno aquí.
          : (texto || 'No se pudo actualizar la contraseña. Intenta nuevamente.'),
      });
    } finally {
      setEnviando(false);
    }
  }

  return (
    <section className="profile-section" aria-label="Seguridad">
      <h3 className="profile-section-title">Seguridad</h3>

      {!abierto ? (
        <button type="button" className="btn-breb" onClick={abrir}>
          Cambiar contraseña
        </button>
      ) : (
        <form className="profile-edit-form" onSubmit={e => void handleSubmit(e)} autoComplete="off">
          <label>
            Contraseña actual
            <input
              type="password"
              value={form.claveActual}
              onChange={e => setForm(f => ({ ...f, claveActual: e.target.value }))}
              autoComplete="current-password"
              required
            />
          </label>
          <label>
            Nueva contraseña
            <input
              type="password"
              value={form.claveNueva}
              onChange={e => setForm(f => ({ ...f, claveNueva: e.target.value }))}
              autoComplete="new-password"
              required
            />
          </label>
          <label>
            Confirmar nueva contraseña
            <input
              type="password"
              value={form.confirmacion}
              onChange={e => setForm(f => ({ ...f, confirmacion: e.target.value }))}
              autoComplete="new-password"
              required
            />
          </label>

          {/* Ayuda de UX únicamente — la política autoritativa vive en el
              backend (AuthService.ValidarPoliticaClave, XPAY-398/400). Si el
              backend rechaza igual, se muestra su mensaje real (arriba). */}
          <p className="profile-placeholder-note">
            Mínimo 8 caracteres, con al menos una mayúscula, una minúscula, un número
            y un carácter especial. No puede contener tu usuario.
          </p>

          {errorLocal && <span className="breb-msg-err">{errorLocal}</span>}
          {mensaje && !mensaje.ok && <span className="breb-msg-err">{mensaje.text}</span>}

          <div className="profile-edit-actions">
            <button type="submit" className="btn-breb" disabled={enviando}>
              {enviando ? 'Actualizando...' : 'Actualizar contraseña'}
            </button>
            <button type="button" className="wallet-send-link-btn" onClick={cerrarYLimpiar} disabled={enviando}>
              Cancelar
            </button>
          </div>
        </form>
      )}

      {mensaje && mensaje.ok && (
        <p className="profile-save-feedback" role="status">{mensaje.text}</p>
      )}
    </section>
  );
}
