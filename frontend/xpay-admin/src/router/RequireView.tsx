import { Navigate, Outlet } from 'react-router-dom';
import { useAuth, getViewForUser, type UserView } from '../auth/AuthContext.tsx';

// Ruta principal de cada vista — misma resolución que UserRedirect en
// App.tsx, aquí como destino de "acceso no autorizado" en vez de
// "sin ruta específica". Una sola fuente de verdad para este mapeo:
// si cambia, actualizar también UserRedirect.
const HOME_BY_VIEW: Record<UserView, string> = {
  admin:    '/dashboard',
  comercio: '/mi-comercio',
  empresa:  '/mi-empresa',
  wallet:   '/mi-wallet',
};

interface RequireViewProps {
  allowedViews: UserView[];
}

// Guard de autorización por vista — reutiliza getViewForUser (AuthContext),
// sin duplicar sus reglas. No hace llamadas API ni usa datos simulados.
// Si el usuario autenticado no pertenece a ninguna de las vistas permitidas
// para esta rama de rutas, redirige a su propia ruta principal (replace,
// para no dejar la ruta inválida en el historial) en vez de mostrar un
// error 403 en frontend.
export function RequireView({ allowedViews }: RequireViewProps) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;

  const view = getViewForUser(user);
  if (!allowedViews.includes(view)) return <Navigate to={HOME_BY_VIEW[view]} replace />;

  return <Outlet />;
}
