import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth, isAdminUser } from './auth/AuthContext.tsx';
import { PrivateRoute } from './router/PrivateRoute.tsx';
import { Layout } from './components/Layout.tsx';
import { LoginPage } from './pages/LoginPage.tsx';
import { DashboardPage } from './pages/DashboardPage.tsx';
import { WalletPage } from './pages/WalletPage.tsx';
import { ComercioPage } from './pages/ComercioPage.tsx';
import { LedgerPage } from './pages/LedgerPage.tsx';
import { RetiroPage } from './pages/RetiroPage.tsx';
import { RetirosListPage } from './pages/RetirosListPage.tsx';
import { WalletsListPage } from './pages/WalletsListPage.tsx';
import { ComerciosListPage } from './pages/ComerciosListPage.tsx';
import { VentasQrListPage } from './pages/VentasQrListPage.tsx';
import { LedgerTransaccionesListPage } from './pages/LedgerTransaccionesListPage.tsx';
import { UserWalletPage } from './pages/UserWalletPage.tsx';

// Redirects to dashboard (admin) or Mi Wallet (demo user) after login
function UserRedirect() {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  return isAdminUser(user)
    ? <Navigate to="/dashboard" replace />
    : <Navigate to="/mi-wallet" replace />;
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route element={<PrivateRoute />}>
            <Route element={<Layout />}>
              <Route index element={<UserRedirect />} />
              {/* Admin routes */}
              <Route path="dashboard" element={<DashboardPage />} />
              <Route path="wallets/listado" element={<WalletsListPage />} />
              <Route path="wallets" element={<WalletPage />} />
              <Route path="wallets/:idWallet" element={<WalletPage />} />
              <Route path="comercios/listado" element={<ComerciosListPage />} />
              <Route path="comercios" element={<ComercioPage />} />
              <Route path="comercios/:idComercio" element={<ComercioPage />} />
              <Route path="ventas-qr/listado" element={<VentasQrListPage />} />
              <Route path="ledger/listado" element={<LedgerTransaccionesListPage />} />
              <Route path="ledger" element={<LedgerPage />} />
              <Route path="ledger/:idTransaccion" element={<LedgerPage />} />
              <Route path="retiros/listado" element={<RetirosListPage />} />
              <Route path="retiros" element={<RetiroPage />} />
              <Route path="retiros/:idRetiro" element={<RetiroPage />} />
              {/* Demo user route */}
              <Route path="mi-wallet" element={<UserWalletPage />} />
              {/* Catch-all: smart redirect per role */}
              <Route path="*" element={<UserRedirect />} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
