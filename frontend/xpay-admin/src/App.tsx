import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext.tsx';
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

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route element={<PrivateRoute />}>
            <Route element={<Layout />}>
              <Route index element={<Navigate to="/dashboard" replace />} />
              <Route path="dashboard" element={<DashboardPage />} />
              <Route path="wallets/listado" element={<WalletsListPage />} />
              <Route path="wallets" element={<WalletPage />} />
              <Route path="wallets/:idWallet" element={<WalletPage />} />
              <Route path="comercios/listado" element={<ComerciosListPage />} />
              <Route path="comercios" element={<ComercioPage />} />
              <Route path="comercios/:idComercio" element={<ComercioPage />} />
              <Route path="ledger" element={<LedgerPage />} />
              <Route path="ledger/:idTransaccion" element={<LedgerPage />} />
              <Route path="retiros/listado" element={<RetirosListPage />} />
              <Route path="retiros" element={<RetiroPage />} />
              <Route path="retiros/:idRetiro" element={<RetiroPage />} />
              <Route path="*" element={<Navigate to="/dashboard" replace />} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
