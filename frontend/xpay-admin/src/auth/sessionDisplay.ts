import { getViewForUser, type AuthUser } from './AuthContext.tsx';

export function perfilEmpresaVisible(rol: string): string {
  return ({
    ADMIN_EMPRESA: 'Administrador de empresa',
    OPERADOR_EMPRESA: 'Operador de empresa',
    CONSULTA_EMPRESA: 'Consulta de empresa',
  } as Record<string, string>)[rol] ?? 'Usuario de empresa';
}

export function perfilSesionVisible(user: AuthUser, rolComercio?: string | null): string {
  const view = getViewForUser(user);
  if (view === 'admin') {
    if (user.roles.includes('SUPERUSUARIO')) return 'Superusuario XPAY';
    return user.roles.includes('ADMIN_XPAY') ? 'Administrador XPAY' : 'Operador XPAY';
  }
  if (view === 'empresa') return 'Usuario de empresa';
  if (view === 'comercio') return ({
    CAJERO: 'Cajero de comercio',
    ADMIN_SEDE_COMERCIO: 'Administrador de sede',
    ADMIN_COMERCIO: 'Administrador de comercio',
  } as Record<string, string>)[rolComercio ?? ''] ?? 'Usuario de comercio';
  return 'Usuario de Wallet';
}
