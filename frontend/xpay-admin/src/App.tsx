import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth, getViewForUser } from './auth/AuthContext.tsx';
import { PrivateRoute } from './router/PrivateRoute.tsx';
import { RequireView } from './router/RequireView.tsx';
import { RequireClaveVigente } from './router/RequireClaveVigente.tsx';
import { RequireRolComercio } from './router/RequireRolComercio.tsx';
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
import { MiCajaPage } from './pages/comercio/MiCajaPage.tsx';
import { CajasListaPage } from './pages/comercio/CajasListaPage.tsx';
import { CajaDetallePage } from './pages/comercio/CajaDetallePage.tsx';
import { MiEmpresaPage } from './pages/MiEmpresaPage.tsx';
import { BrebLlavesAdminPage } from './pages/BrebLlavesAdminPage.tsx';
import { BrebRetirosAdminPage } from './pages/BrebRetirosAdminPage.tsx';
import { LibranzaConveniosAdminPage } from './pages/LibranzaConveniosAdminPage.tsx';
import { MiEmpresaLibranzaPage } from './pages/MiEmpresaLibranzaPage.tsx';
import { MiWalletLibranzaPage } from './pages/MiWalletLibranzaPage.tsx';
import { ComerciosAliadosPage } from './pages/ComerciosAliadosPage.tsx';
import { ParametrosLiquidacionPage } from './pages/ParametrosLiquidacionPage.tsx';
import { CarteraOrdinariaAdminPage } from './pages/CarteraOrdinariaAdminPage.tsx';
import { MiCarteraOrdinariaPage } from './pages/MiCarteraOrdinariaPage.tsx';
import { AdminWalletRecaudosComercioPage } from './pages/AdminWalletRecaudosComercioPage.tsx';
import { AdminWalletCierresDiariosComercioPage } from './pages/AdminWalletCierresDiariosComercioPage.tsx';
import { AdminUsuariosListPage } from './pages/AdminUsuariosListPage.tsx';
import { AdminUsuarioDetallePage } from './pages/AdminUsuarioDetallePage.tsx';
import { CambiarClaveObligatoriaPage } from './pages/CambiarClaveObligatoriaPage.tsx';

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
            {/* Fase USUARIOS-ADMIN-5: fuera de RequireClaveVigente a propósito
                — es la única ruta accesible mientras requiereCambioClave=true. */}
            <Route path="cambiar-clave-obligatoria" element={<CambiarClaveObligatoriaPage />} />
            <Route element={<RequireClaveVigente />}>
            <Route element={<Layout />}>
              <Route index element={<UserRedirect />} />

              {/* Admin routes */}
              <Route element={<RequireView allowedViews={['admin']} />}>
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
                <Route path="admin/cartera-ordinaria" element={<CarteraOrdinariaAdminPage />} />
                <Route path="admin/wallet-recaudos-comercio" element={<AdminWalletRecaudosComercioPage />} />
                <Route path="admin/wallet-cierres-comercio" element={<AdminWalletCierresDiariosComercioPage />} />
                <Route path="admin/usuarios" element={<AdminUsuariosListPage />} />
                <Route path="admin/usuarios/:id" element={<AdminUsuarioDetallePage />} />
              </Route>

              {/* Wallet (usuario final) routes */}
              <Route element={<RequireView allowedViews={['wallet']} />}>
                <Route path="mi-wallet"   element={<UserWalletPage />} />
                <Route path="mi-wallet/libranza" element={<MiWalletLibranzaPage />} />
                <Route path="mi-wallet/cartera" element={<MiCarteraOrdinariaPage />} />
              </Route>

              {/* Comercio routes */}
              <Route element={<RequireView allowedViews={['comercio']} />}>
                <Route path="mi-comercio" element={<MiComercioPage />} />

                {/* Fase 70.4-E: Caja/Cuadre — acotado además por rol_comercio,
                    ya que RequireView solo distingue admin/comercio/empresa/wallet. */}
                <Route element={<RequireRolComercio allowedRoles={['CAJERO', 'ADMIN_SEDE_COMERCIO']} />}>
                  <Route path="comercio/mi-caja" element={<MiCajaPage />} />
                </Route>
                <Route element={<RequireRolComercio allowedRoles={['ADMIN_SEDE_COMERCIO', 'ADMIN_COMERCIO']} />}>
                  <Route path="comercio/cajas" element={<CajasListaPage />} />
                  <Route path="comercio/cajas/:id" element={<CajaDetallePage />} />
                </Route>
              </Route>

              {/* Empresa routes */}
              <Route element={<RequireView allowedViews={['empresa']} />}>
                <Route path="mi-empresa"  element={<MiEmpresaPage />} />
                <Route path="mi-empresa/libranza" element={<MiEmpresaLibranzaPage />} />
              </Route>

              {/* Catch-all: smart redirect per role */}
              <Route path="*" element={<UserRedirect />} />
            </Route>
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}
