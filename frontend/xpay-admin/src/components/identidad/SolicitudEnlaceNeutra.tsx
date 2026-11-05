import { FormEvent, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { post } from '../../api/client.ts';
import { mensajeErrorIdentidad } from '../../utils/erroresIdentidad.ts';

// Únicos endpoints admitidos: ambos responden SIEMPRE 202 con el mismo
// cuerpo, exista o no el usuario (P0-B2/P0-B3).
type EndpointSolicitud = '/api/auth/recuperar-clave' | '/api/usuarios/reenviar-verificacion';

interface Props {
  subtitulo:     string;
  descripcion:   string;
  endpoint:      EndpointSolicitud;
  mensajeNeutro: string;
  textoBoton:    string;
}

const MENSAJE_SIN_USUARIO = 'Ingresa tu usuario.';
const MENSAJE_INCIERTO    = 'No pudimos confirmar si recibimos tu solicitud. Espera unos minutos y revisa tu correo antes de intentarlo de nuevo.';
const MENSAJE_GENERICO    = 'No fue posible procesar tu solicitud en este momento. Intenta nuevamente más tarde.';

// P0-B frontend — formulario público "usuario → enlace por correo" de
// /recuperar-clave y /reenviar-verificacion. No enumera: tras el 202 se
// muestra SIEMPRE el mismo texto fijo del frontend (nunca el del backend) y
// cualquier error distinto de 429 o timeout usa el mismo mensaje genérico,
// sin mostrar el detalle del backend.
export function SolicitudEnlaceNeutra({ subtitulo, descripcion, endpoint, mensajeNeutro, textoBoton }: Props) {
  const enviandoRef = useRef(false);
  const [usuario,  setUsuario]  = useState('');
  const [error,    setError]    = useState('');
  const [enviando, setEnviando] = useState(false);
  const [enviado,  setEnviado]  = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (enviandoRef.current) return;
    const valor = usuario.trim();
    if (!valor) {
      setError(MENSAJE_SIN_USUARIO);
      return;
    }
    enviandoRef.current = true;
    setEnviando(true);
    setError('');
    try {
      await post<unknown>(endpoint, { usuario: valor });
      setUsuario('');
      setEnviado(true);
    } catch (err) {
      setError(mensajeErrorIdentidad(err, {
        mostrar400: false,
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
        <p className="subtitle">{subtitulo}</p>
        {enviado ? (
          <p className="session-message" role="status">{mensajeNeutro}</p>
        ) : (
          <form onSubmit={e => void handleSubmit(e)} noValidate>
            <p className="results-meta">{descripcion}</p>
            <label>
              Usuario
              <input
                value={usuario}
                onChange={e => setUsuario(e.target.value)}
                autoComplete="username"
                autoFocus
                required
              />
            </label>
            {error && <p className="error" role="alert">{error}</p>}
            <button type="submit" disabled={enviando}>
              {enviando ? 'Enviando...' : textoBoton}
            </button>
          </form>
        )}
        <p className="results-meta">
          <Link to="/login">Volver a iniciar sesión</Link>
        </p>
      </div>
    </div>
  );
}
