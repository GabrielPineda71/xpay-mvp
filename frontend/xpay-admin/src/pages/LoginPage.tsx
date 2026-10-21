import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.tsx';

const API_HINT = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000';

export function LoginPage() {
  const { login } = useAuth();
  const navigate   = useNavigate();

  const [usuario,  setUsuario]  = useState('');
  const [password, setPassword] = useState('');
  const [error,    setError]    = useState('');
  const [loading,  setLoading]  = useState(false);

  // Read and immediately clear the session-expired flag written by AuthContext
  const [sessionMsg] = useState<string>(() => {
    const flag = sessionStorage.getItem('xpay_expired');
    sessionStorage.removeItem('xpay_expired');
    return flag ? 'Tu sesión ha expirado. Inicia sesión nuevamente.' : '';
  });

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login(usuario.trim(), password);
      // Deja que UserRedirect (App.tsx) resuelva el destino real vía
      // getViewForUser — sin duplicar aquí la regla de qué ruta
      // corresponde a cada vista.
      navigate('/', { replace: true });
    } catch (err) {
      setError((err as Error).message ?? 'Error al iniciar sesión');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login-container">
      <div className="login-card">
        <h1>XPAY Admin</h1>
        <p className="subtitle">Panel administrativo</p>
        {sessionMsg && <p className="session-message">{sessionMsg}</p>}
        <form onSubmit={handleSubmit}>
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
          <label>
            Contraseña
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
          </label>
          {error && <p className="error">{error}</p>}
          <button type="submit" disabled={loading}>
            {loading ? 'Ingresando...' : 'Ingresar'}
          </button>
        </form>
        {/* P0-B frontend — accesos públicos de identidad (rutas fijas). El
            error de login sigue siendo genérico: no se intenta detectar
            PENDIENTE_VERIFICACION. */}
        <p className="results-meta">
          <Link to="/recuperar-clave">¿Olvidaste tu contraseña?</Link>
        </p>
        <p className="results-meta">
          <Link to="/reenviar-verificacion">¿No recibiste el correo de verificación?</Link>
        </p>
        <p className="api-hint">API: {API_HINT}</p>
      </div>
    </div>
  );
}
