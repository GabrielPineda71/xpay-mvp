import { FormEvent, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { post } from '../api/client.ts';
import { useAuth } from '../auth/AuthContext.tsx';

interface CambiarClaveResp {
  success: boolean;
  message: string;
  data: { token: string; requiereCambioClave: boolean };
}

// Fase USUARIOS-ADMIN-5: pantalla obligatoria tras un restablecimiento de
// contraseña por un administrador. No forma parte de Layout — se llega aquí
// vía RequireClaveVigente antes de cualquier otra ruta protegida.
export function CambiarClaveObligatoriaPage() {
  const { actualizarToken, logout } = useAuth();
  const navigate = useNavigate();

  const [claveActual,    setClaveActual]    = useState('');
  const [claveNueva,     setClaveNueva]     = useState('');
  const [confirmacion,   setConfirmacion]   = useState('');
  const [error,          setError]          = useState('');
  const [loading,        setLoading]        = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const r = await post<CambiarClaveResp>('/api/auth/cambiar-clave-obligatoria', {
        claveActual,
        claveNueva,
        confirmacionClaveNueva: confirmacion,
      });
      actualizarToken(r.data.token, r.data.requiereCambioClave);
      navigate('/', { replace: true });
    } catch (err) {
      setError((err as Error).message ?? 'Error al cambiar la contraseña');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login-container">
      <div className="login-card">
        <h1>XPAY Admin</h1>
        <p className="subtitle">Cambio de contraseña obligatorio</p>
        <p className="session-message">
          Tu contraseña fue restablecida por un administrador. Debes definir una contraseña nueva antes de continuar.
        </p>
        <form onSubmit={handleSubmit}>
          <label>
            Contraseña actual (temporal)
            <input
              type="password"
              value={claveActual}
              onChange={e => setClaveActual(e.target.value)}
              autoComplete="current-password"
              autoFocus
              required
            />
          </label>
          <label>
            Contraseña nueva
            <input
              type="password"
              value={claveNueva}
              onChange={e => setClaveNueva(e.target.value)}
              autoComplete="new-password"
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
          {error && <p className="error">{error}</p>}
          <button type="submit" disabled={loading}>
            {loading ? 'Guardando...' : 'Cambiar contraseña'}
          </button>
        </form>
        <button type="button" className="btn-link" onClick={logout}>
          Cerrar sesión
        </button>
      </div>
    </div>
  );
}
