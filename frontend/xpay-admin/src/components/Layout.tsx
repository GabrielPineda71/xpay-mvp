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
          <Link to="/wallets">Wallet</Link>
          <Link to="/comercios">Comercio</Link>
          <Link to="/ledger">Ledger</Link>
          <Link to="/retiros">Retiros</Link>
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
