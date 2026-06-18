import { Link, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.tsx';

export function Layout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  function handleLogout() {
    logout();
    navigate('/login');
  }

  return (
    <div className="layout">
      <nav className="nav">
        <span className="nav-brand">XPAY Admin</span>
        <div className="nav-links">
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/wallets/listado">Wallets</Link>
          <Link to="/wallets">Buscar wallet</Link>
          <Link to="/comercios/listado">Comercios</Link>
          <Link to="/comercios">Buscar comercio</Link>
          <Link to="/ledger">Ledger</Link>
          <Link to="/retiros/listado">Retiros</Link>
          <Link to="/retiros">Buscar retiro</Link>
        </div>
        <div className="nav-user">
          {user && <span>{user.usuario}</span>}
          <button onClick={handleLogout}>Salir</button>
        </div>
      </nav>
      <main className="main-content">
        <Outlet />
      </main>
    </div>
  );
}
