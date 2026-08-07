import { Navigate, Outlet } from 'react-router-dom';
import { useComercioScope } from '../auth/useComercioScope.ts';

interface RequireRolComercioProps {
  allowedRoles: string[];
  fallback?:    string;
}

// Guard de UX por rol_comercio (CAJERO/ADMIN_SEDE_COMERCIO/ADMIN_COMERCIO) —
// complementa a RequireView (que solo distingue admin/comercio/empresa/wallet).
// El backend sigue siendo la autoridad real en cada endpoint; esto solo evita
// mostrar una pantalla que de todas formas fallaría en el primer request.
export function RequireRolComercio({ allowedRoles, fallback = '/mi-comercio' }: RequireRolComercioProps) {
  const { scope, loading, error } = useComercioScope();

  if (loading) return <div className="caja-page-loading">Verificando tu acceso...</div>;
  if (error)   return <div className="caja-error-banner">{error}</div>;
  if (!scope || !allowedRoles.includes(scope.rolComercio)) return <Navigate to={fallback} replace />;

  return <Outlet />;
}
