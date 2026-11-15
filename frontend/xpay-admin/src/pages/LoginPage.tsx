import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.tsx';
import { BrandMark } from '../components/BrandMark.tsx';
import { SoloQa } from '../config/SoloQa.tsx';

export function LoginPage() {
  const { login } = useAuth();
  const navigate   = useNavigate();

  const [usuario,  setUsuario]  = useState('');
  const [password, setPassword] = useState('');
  const [passwordVisible, setPasswordVisible] = useState(false);
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
    <main className="login-container xpay-login">
      <section className="login-card xpay-login-card" aria-labelledby="login-title">
        <header className="xpay-login-brand">
          <BrandMark variant="logo" theme="white" />
        </header>
        <div className="xpay-login-body">
          <h1 id="login-title">Inicia sesión</h1>
          {sessionMsg && <p className="session-message" role="status">{sessionMsg}</p>}
          <form onSubmit={handleSubmit}>
            <label>
              Usuario
              <input
                value={usuario}
                onChange={e => setUsuario(e.target.value)}
                autoComplete="username"
                autoCapitalize="none"
                spellCheck={false}
                required
              />
            </label>
            <div className="xpay-login-password">
              <label htmlFor="login-password">Contraseña</label>
              <div className="xpay-login-password-control">
                <input
                  id="login-password"
                  type={passwordVisible ? 'text' : 'password'}
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  autoComplete="current-password"
                  autoCapitalize="none"
                  spellCheck={false}
                  required
                />
                <button
                  type="button"
                  aria-controls="login-password"
                  aria-label={passwordVisible ? 'Ocultar contraseña' : 'Mostrar contraseña'}
                  onClick={() => setPasswordVisible(visible => !visible)}
                >
                  {passwordVisible ? 'Ocultar' : 'Mostrar'}
                </button>
              </div>
            </div>
            {error && <p className="error" role="alert">{error}</p>}
            <button type="submit" disabled={loading}>
              {loading ? 'Ingresando...' : 'Ingresar'}
            </button>
          </form>
          <nav className="xpay-login-links" aria-label="Acceso a la cuenta">
            <Link to="/recuperar-clave">¿Olvidaste tu contraseña?</Link>
            <Link to="/reenviar-verificacion">¿No recibiste el correo de verificación?</Link>
            <SoloQa>
              <Link to="/registro" className="xpay-login-register">Crear cuenta</Link>
            </SoloQa>
          </nav>
        </div>
      </section>
    </main>
  );
}
