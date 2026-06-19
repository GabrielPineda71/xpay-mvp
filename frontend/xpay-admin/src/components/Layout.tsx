import { Link, Outlet, useNavigate } from 'react-router-dom';
import { useAuth, getViewForUser } from '../auth/AuthContext.tsx';

function getApiLabel(): string {
  const url = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000';
  try {
    const { hostname } = new URL(url);
    return hostname === 'localhost' || hostname === '127.0.0.1' ? 'local' : hostname;
  } catch {
    return url;
  }
}

export function Layout() {
  const { user, logout } = useAuth();
  const navigate          = useNavigate();
  const view              = user ? getViewForUser(user) : 'wallet';

  function handleLogout() {
    logout();
    navigate('/login');
  }

  return (
    <div className="layout">
      <nav className="nav">
        <span className="nav-brand">{view === 'admin' ? 'XPAY Admin' : 'XPAY'}</span>

        <div className="nav-links">
          {view === 'admin' && (
            <>
              <Link to="/dashboard">Dashboard</Link>
              <Link to="/wallets/listado">Wallets</Link>
              <Link to="/wallets">Buscar wallet</Link>
              <Link to="/comercios/listado">Comercios</Link>
              <Link to="/comercios">Buscar comercio</Link>
              <Link to="/ventas-qr/listado">Ventas QR</Link>
              <Link to="/ledger/listado">Ledger</Link>
              <Link to="/ledger">Buscar ledger</Link>
              <Link to="/retiros/listado">Retiros</Link>
              <Link to="/retiros">Buscar retiro</Link>
            </>
          )}
          {view === 'wallet'   && <Link to="/mi-wallet">Mi Wallet</Link>}
          {view === 'comercio' && <Link to="/mi-comercio">Mi Comercio</Link>}
          {view === 'empresa'  && <Link to="/mi-empresa">Mi Empresa</Link>}
        </div>

        <div className="nav-user">
          {user && <span>{user.usuario}</span>}
          <span className="app-env">API: {getApiLabel()}</span>
          <button className="logout-button" onClick={handleLogout}>Cerrar sesión</button>
        </div>
      </nav>
      <main className="main-content">
        <Outlet />
      </main>
    </div>
  );
}
