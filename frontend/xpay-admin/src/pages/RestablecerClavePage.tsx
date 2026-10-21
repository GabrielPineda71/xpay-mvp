import { FormEvent, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { post } from '../api/client.ts';
import { useAuth } from '../auth/AuthContext.tsx';
import { useTokenDeFragmento } from '../hooks/useTokenDeFragmento.ts';
import { mensajeErrorIdentidad } from '../utils/erroresIdentidad.ts';
import { MENSAJE_CONFIRMACION_DISTINTA, validarPoliticaClave } from '../utils/politicaClave.ts';

interface RespuestaIdentidad {
  success:  boolean;
  message?: string;
}

const MENSAJE_SIN_TOKEN = 'El enlace de recuperación no es válido o está incompleto.';
const MENSAJE_EXITO     = 'Contraseña restablecida. Ya puedes iniciar sesión.';
const MENSAJE_INCOMPLETO = 'Completa ambos campos.';
const MENSAJE_INCIERTO  = 'No pudimos confirmar el resultado. Intenta iniciar sesión con tu nueva contraseña; si no puedes, solicita un nuevo enlace.';
const MENSAJE_GENERICO  = 'No fue posible restablecer tu contraseña en este momento. Intenta nuevamente más tarde.';

// P0-B frontend — /restablecer-clave (pública). Mismo tratamiento del token
// que /verificar-email (useTokenDeFragmento). El token se conserva en
// memoria durante toda la vida de la página porque el backend NO lo consume
// cuando rechaza la clave (confirmación, política, clave repetida): el
// usuario puede corregir y reintentar. Decisión J2: los 400 no se
// distinguen por texto — ApiError.message se muestra como texto React y el
// formulario sigue disponible. Tras el éxito el backend revoca los JWT
// existentes (VersionSeguridad), así que también se cierra la sesión local.
export function RestablecerClavePage() {
  const token = useTokenDeFragmento();
  const { logout } = useAuth();
  const enviandoRef = useRef(false);
  const [claveNueva,   setClaveNueva]   = useState('');
  const [confirmacion, setConfirmacion] = useState('');
  const [error,        setError]        = useState('');
  const [enviando,     setEnviando]     = useState(false);
  const [exito,        setExito]        = useState('');

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (token === null || enviandoRef.current) return;

    // Mismo orden que el backend: confirmación primero, luego política.
    const local = !claveNueva || !confirmacion
      ? MENSAJE_INCOMPLETO
      : claveNueva !== confirmacion
        ? MENSAJE_CONFIRMACION_DISTINTA
        : validarPoliticaClave(claveNueva);
    if (local) {
      setError(local);
      return;
    }

    enviandoRef.current = true;
    setEnviando(true);
    setError('');
    try {
      const r = await post<RespuestaIdentidad>('/api/auth/restablecer-clave', {
        token,
        claveNueva,
        confirmacionClaveNueva: confirmacion,
      });
      setClaveNueva('');
      setConfirmacion('');
      logout();
      setExito(r.message ?? MENSAJE_EXITO);
    } catch (err) {
      setClaveNueva('');
      setConfirmacion('');
      setError(mensajeErrorIdentidad(err, {
        mostrar400: true,
        incierto:   MENSAJE_INCIERTO,
        generico:   MENSAJE_GENERICO,
      }));
    } finally {
      enviandoRef.current = false;
      setEnviando(false);
    }
  }

  return (
    <div className="login-container">
      <div className="login-card">
        <h1>XPAY</h1>
        <p className="subtitle">Restablecer contraseña</p>
        {token === null ? (
          <p className="error" role="alert">{MENSAJE_SIN_TOKEN}</p>
        ) : exito ? (
          <p className="session-message" role="status">{exito}</p>
        ) : (
          <form onSubmit={e => void handleSubmit(e)} autoComplete="off" noValidate>
            <label>
              Contraseña nueva
              <input
                type="password"
                value={claveNueva}
                onChange={e => setClaveNueva(e.target.value)}
                autoComplete="new-password"
                autoFocus
                required
              />
            </label>
            <label>
              Confirmar contraseña nueva
              <input
                type="password"
                value={confirmacion}
                onChange={e => setConfirmacion(e.target.value)}
                autoComplete="new-password"
                required
              />
            </label>
            <p className="results-meta">
              Mínimo 8 caracteres, con al menos una mayúscula, una minúscula, un dígito y un carácter especial (!@#$%^&amp;*()-_=+). No puede ser igual a la contraseña actual ni contener tu usuario.
            </p>
            {error && <p className="error" role="alert">{error}</p>}
            <button type="submit" disabled={enviando}>
              {enviando ? 'Guardando...' : 'Restablecer contraseña'}
            </button>
          </form>
        )}
        {!exito && (
          <p className="results-meta">
            <Link to="/recuperar-clave">Solicitar un nuevo enlace</Link>
          </p>
        )}
        <p className="results-meta">
          <Link to="/login">Ir a iniciar sesión</Link>
        </p>
      </div>
    </div>
  );
}
