import { FormEvent, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, post } from '../api/client.ts';
import { useTokenDeFragmento } from '../hooks/useTokenDeFragmento.ts';
import { mensajeErrorIdentidad } from '../utils/erroresIdentidad.ts';

interface RespuestaIdentidad {
  success:  boolean;
  message?: string;
}

// reintentable: 429, timeout, 500 o red — el token no se consumió (o no se
// sabe), así que se ofrece un nuevo intento explícito. invalido: 400 de
// enlace no válido o ya usado — definitivo para este token.
type Estado = 'listo' | 'procesando' | 'exito' | 'invalido' | 'reintentable';

const MENSAJE_SIN_TOKEN = 'El enlace de verificación no es válido o está incompleto.';
const MENSAJE_EXITO     = 'Correo verificado. Ya puedes iniciar sesión.';
const MENSAJE_INCIERTO  = 'No pudimos confirmar el resultado. Intenta iniciar sesión; si no puedes, solicita un nuevo enlace.';
const MENSAJE_GENERICO  = 'No fue posible verificar tu correo en este momento. Intenta nuevamente en unos minutos.';

// P0-B frontend — /verificar-email (pública). El token llega en el
// fragmento y se captura/borra de la URL al montar (useTokenDeFragmento).
// Decisión J1: NO hay POST automático al cargar — un escáner de correo que
// ejecute JS podría consumir el enlace de un solo uso. Solo una acción
// explícita del usuario ("Confirmar mi correo" o, tras un fallo
// transitorio, "Intentar nuevamente") hace el POST. enviandoRef impide dos
// POST simultáneos (doble clic, StrictMode); cerradoRef impide cualquier POST
// tras el éxito o tras un 400. El token sigue solo en memoria entre
// intentos. Los mensajes del backend se muestran como texto React.
export function VerificarEmailPage() {
  const token = useTokenDeFragmento();
  const enviandoRef = useRef(false);
  const cerradoRef  = useRef(false);
  const [estado,  setEstado]  = useState<Estado>('listo');
  const [mensaje, setMensaje] = useState('');

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (token === null || enviandoRef.current || cerradoRef.current) return;
    enviandoRef.current = true;
    setEstado('procesando');
    try {
      const r = await post<RespuestaIdentidad>('/api/usuarios/verificar-email', { token });
      cerradoRef.current = true;
      setMensaje(r.message ?? MENSAJE_EXITO);
      setEstado('exito');
    } catch (err) {
      const definitivo = err instanceof ApiError && err.status === 400;
      if (definitivo) cerradoRef.current = true;
      setMensaje(mensajeErrorIdentidad(err, {
        mostrar400: true,
        incierto:   MENSAJE_INCIERTO,
        generico:   MENSAJE_GENERICO,
      }));
      setEstado(definitivo ? 'invalido' : 'reintentable');
    } finally {
      enviandoRef.current = false;
    }
  }

  const formularioVisible = token !== null && (estado === 'listo' || estado === 'procesando' || estado === 'reintentable');

  return (
    <div className="login-container">
      <div className="login-card">
        <h1>XPAY</h1>
        <p className="subtitle">Verificación de correo</p>
        {token === null && <p className="error" role="alert">{MENSAJE_SIN_TOKEN}</p>}
        {estado === 'exito' && <p className="session-message" role="status">{mensaje}</p>}
        {(estado === 'invalido' || estado === 'reintentable') && (
          <p className="error" role="alert">{mensaje}</p>
        )}
        {formularioVisible && (
          <form onSubmit={e => void handleSubmit(e)}>
            {estado === 'listo' && (
              <p className="results-meta">Confirma tu correo para activar tu cuenta XPAY.</p>
            )}
            <button type="submit" disabled={estado === 'procesando'}>
              {estado === 'procesando'
                ? 'Verificando...'
                : estado === 'reintentable' ? 'Intentar nuevamente' : 'Confirmar mi correo'}
            </button>
          </form>
        )}
        {(token === null || estado === 'invalido' || estado === 'reintentable') && (
          <p className="results-meta">
            <Link to="/reenviar-verificacion">Solicitar un nuevo enlace</Link>
          </p>
        )}
        <p className="results-meta">
          <Link to="/login">Ir a iniciar sesión</Link>
        </p>
      </div>
    </div>
  );
}
