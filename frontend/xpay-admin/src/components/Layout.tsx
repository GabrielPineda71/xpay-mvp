import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth, getViewForUser } from '../auth/AuthContext.tsx';
import { WalletShell, type WalletNavItem, type WalletPrimaryAction } from './wallet/WalletShell.tsx';

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
  const { pathname }      = useLocation();
  const view              = user ? getViewForUser(user) : 'wallet';

  function handleLogout() {
    logout();
    navigate('/login');
  }

  // ── Vista wallet: shell nuevo (hero verde + bottom nav), aislado de
  // admin/comercio/empresa, que siguen usando el <div className="layout">
  // de abajo sin ningún cambio. ────────────────────────────────────────
  if (view === 'wallet') {
    const handleAction = (action: WalletPrimaryAction) => {
      switch (action) {
        case 'receive':
        case 'send':
        case 'pay-qr':
          // UserWalletPage.tsx controla sus tabs con useState local, sin
          // leer parámetros de URL — no hay forma real de abrir un tab
          // específico desde aquí sin modificarla (fuera de alcance de
          // UI-2B). La activación exacta del tab queda pendiente para UI-3.
          navigate('/mi-wallet');
          break;
        case 'breb-key':
        case 'where-to-buy':
          // Sin ruta/backend real todavía. breb-key en particular NO debe
          // reutilizar el retiro Bre-B real (son funciones distintas).
          break;
      }
    };

    const handleNavigate = (item: WalletNavItem) => {
      switch (item) {
        case 'home':
        case 'movements':
        case 'qr':
          // Igual que en handleAction: sin mecanismo real para activar un
          // tab interno específico de UserWalletPage desde el router.
          navigate('/mi-wallet');
          break;
        case 'products':
          navigate('/mi-wallet/cartera');
          break;
        case 'profile':
          // WalletShell ya abre ProfileSheet internamente; nada que navegar.
          break;
      }
    };

    const activeNav: WalletNavItem | undefined =
      pathname === '/mi-wallet/cartera' ? 'products' :
      pathname === '/mi-wallet' ? 'home' :
      undefined;

    return (
      <WalletShell
        userName={user?.usuario}
        activeNav={activeNav}
        hasNotifications={false}
        onLogout={handleLogout}
        onAction={handleAction}
        onNavigate={handleNavigate}
      >
        <Outlet />
      </WalletShell>
    );
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
              <Link to="/retiros/listado">Retiros Manuales</Link>
              <Link to="/retiros">Buscar retiro</Link>
              <Link to="/admin/breb-llaves">Llaves Bre-B</Link>
              <Link to="/admin/breb-retiros">Retiros Bre-B</Link>
              <Link to="/admin/libranza-convenios">Convenios Libranza</Link>
              <Link to="/admin/comercios-aliados">Comercios Aliados</Link>
              <Link to="/admin/parametros-liquidacion-comercio">Parámetros Liquidación</Link>
              <Link to="/admin/cartera-ordinaria">Cartera Ordinaria</Link>
              <Link to="/admin/wallet-recaudos-comercio">Liquidación Recaudos Comercio</Link>
              <Link to="/admin/wallet-cierres-comercio">Cierres Diarios Comercio</Link>
            </>
          )}
          {view === 'comercio' && <Link to="/mi-comercio">Mi Comercio</Link>}
          {view === 'empresa'  && (
            <>
              <Link to="/mi-empresa">Mi Empresa</Link>
              <Link to="/mi-empresa/libranza">Empleados Libranza</Link>
            </>
          )}
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
