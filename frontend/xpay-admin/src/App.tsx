import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth, getViewForUser } from './auth/AuthContext.tsx';
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
import { MiComercioPage } from './pages/MiComercioPage.tsx';
import { MiEmpresaPage } from './pages/MiEmpresaPage.tsx';
import { BrebLlavesAdminPage } from './pages/BrebLlavesAdminPage.tsx';
import { BrebRetirosAdminPage } from './pages/BrebRetirosAdminPage.tsx';
import { LibranzaConveniosAdminPage } from './pages/LibranzaConveniosAdminPage.tsx';
import { MiEmpresaLibranzaPage } from './pages/MiEmpresaLibranzaPage.tsx';
import { MiWalletLibranzaPage } from './pages/MiWalletLibranzaPage.tsx';
import { ComerciosAliadosPage } from './pages/ComerciosAliadosPage.tsx';
import { ParametrosLiquidacionPage } from './pages/ParametrosLiquidacionPage.tsx';

// Smart redirect based on user role/view
function UserRedirect() {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  const view = getViewForUser(user);
  if (view === 'admin')    return <Navigate to="/dashboard" replace />;
  if (view === 'comercio') return <Navigate to="/mi-comercio" replace />;
  if (view === 'empresa')  return <Navigate to="/mi-empresa" replace />;
  return <Navigate to="/mi-wallet" replace />;
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
              <Route path="admin/breb-llaves"   element={<BrebLlavesAdminPage />} />
              <Route path="admin/breb-retiros"        element={<BrebRetirosAdminPage />} />
              <Route path="admin/libranza-convenios" element={<LibranzaConveniosAdminPage />} />
              <Route path="admin/comercios-aliados" element={<ComerciosAliadosPage />} />
              <Route path="admin/parametros-liquidacion-comercio" element={<ParametrosLiquidacionPage />} />
              {/* Demo user routes */}
              <Route path="mi-wallet"   element={<UserWalletPage />} />
              <Route path="mi-comercio" element={<MiComercioPage />} />
              <Route path="mi-empresa"  element={<MiEmpresaPage />} />
              <Route path="mi-empresa/libranza" element={<MiEmpresaLibranzaPage />} />
              <Route path="mi-wallet/libranza" element={<MiWalletLibranzaPage />} />
              {/* Catch-all: smart redirect per role */}
              <Route path="*" element={<UserRedirect />} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
