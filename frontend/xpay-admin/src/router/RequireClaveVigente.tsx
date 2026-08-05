import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.tsx';

// Fase USUARIOS-ADMIN-5: guard de UX — si el usuario necesita cambiar su
// contraseña, se redirige antes de mostrar cualquier otra ruta protegida.
// Esto NO es el control de seguridad real (ese vive en el backend, vía
// ClaveVigenteAuthorizationHandler, que rechaza con 403 aunque se elida este
// guard llamando directamente a la API) — es solo para no exponer pantallas
// que de todas formas fallarían al primer request.
export function RequireClaveVigente() {
  const { user } = useAuth();
  if (user?.requiereCambioClave) return <Navigate to="/cambiar-clave-obligatoria" replace />;
  return <Outlet />;
}
