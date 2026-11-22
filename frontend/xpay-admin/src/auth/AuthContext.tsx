import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { post } from '../api/client.ts';

export interface AuthUser {
  idUsuario:           number;
  idPersona:           number;
  usuario:             string;
  estado:              string;
  roles:               string[];
  token:               string;
  requiereCambioClave: boolean;
  // D10 — asociación ACTIVA a un convenio de empresa (LoginResponse). Ausente
  // en sesiones guardadas antes del cambio: se trata como false.
  esUsuarioEmpresa?:   boolean;
  // El alta limitada de QA abre el perfil sin las pantallas de wallet.
  requierePerfil?:     boolean;
}

interface AuthCtx {
  user:            AuthUser | null;
  login:           (usuario: string, password: string) => Promise<void>;
  logout:          () => void;
  actualizarToken: (token: string, requiereCambioClave: boolean) => void;
  actualizarSesion: (sesion: unknown) => void;
}

// Una habilitación solo adopta la sesión completa devuelta por el servidor
// para el mismo titular. Nunca se cambia requierePerfil de forma aislada.
export function validarSesionActualizada(sesion: unknown, actual: AuthUser): AuthUser {
  if (!sesion || typeof sesion !== 'object') throw new Error('No pudimos confirmar tu nueva sesión. Vuelve a ingresar.');
  const s = sesion as Partial<AuthUser>;
  if (s.idUsuario !== actual.idUsuario || s.idPersona !== actual.idPersona || s.usuario !== actual.usuario
    || s.estado !== 'ACTIVO' || s.requierePerfil !== false || s.requiereCambioClave !== false
    || typeof s.token !== 'string' || !s.token.trim() || !Array.isArray(s.roles) || s.roles.length !== 1
    || s.roles.some(r => r !== 'USUARIO_FINAL') || s.esUsuarioEmpresa === true)
    throw new Error('No pudimos confirmar tu nueva sesión. Vuelve a ingresar.');
  return s as AuthUser;
}

interface LoginApiResp {
  success:  boolean;
  message?: string;
  data:     AuthUser;
}

const AuthContext = createContext<AuthCtx | null>(null);
const STORAGE_KEY = 'xpay_user';

function loadUser(): AuthUser | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as AuthUser) : null;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(loadUser);

  useEffect(() => {
    function handleUnauthorized() {
      // Mark session as expired so LoginPage can show the message
      sessionStorage.setItem('xpay_expired', '1');
      localStorage.removeItem(STORAGE_KEY);
      localStorage.removeItem('xpay_token');
      setUser(null); // PrivateRoute will redirect to /login
    }
    function handleCuentaLimitada() {
      // La API acaba de denegar operaciones por falta de habilitación. Solo
      // se restringe la vista personal; no se cambian token, actor ni roles.
      setUser(prev => {
        if (!prev || getViewForUser(prev) !== 'wallet' || prev.requierePerfil === true) return prev;
        const limitada = { ...prev, requierePerfil: true };
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(limitada)); }
        catch { localStorage.removeItem(STORAGE_KEY); localStorage.removeItem('xpay_token'); return null; }
        return limitada;
      });
    }
    window.addEventListener('xpay:unauthorized', handleUnauthorized);
    window.addEventListener('xpay:cuenta-limitada', handleCuentaLimitada);
    return () => {
      window.removeEventListener('xpay:unauthorized', handleUnauthorized);
      window.removeEventListener('xpay:cuenta-limitada', handleCuentaLimitada);
    };
  }, []);

  async function login(usuario: string, password: string): Promise<void> {
    const resp = await post<LoginApiResp>('/api/auth/login', { usuario, password });
    if (!resp.success) throw new Error(resp.message ?? 'Login fallido');
    const authUser = resp.data;
    localStorage.setItem(STORAGE_KEY, JSON.stringify(authUser));
    localStorage.setItem('xpay_token', authUser.token);
    setUser(authUser);
  }

  function logout(): void {
    localStorage.removeItem(STORAGE_KEY);
    localStorage.removeItem('xpay_token');
    setUser(null);
  }

  // Fase USUARIOS-ADMIN-5: tras un cambio de contraseña exitoso, el backend
  // emite un JWT nuevo con el claim requiereCambioClave actualizado — se
  // reemplaza aquí sin exigir un segundo login manual.
  function actualizarToken(token: string, requiereCambioClave: boolean): void {
    setUser(prev => {
      if (!prev) return prev;
      const actualizado: AuthUser = { ...prev, token, requiereCambioClave };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(actualizado));
      localStorage.setItem('xpay_token', token);
      return actualizado;
    });
  }

  function actualizarSesion(sesion: unknown): void {
    if (!user) throw new Error('Vuelve a ingresar para consultar tu Wallet.');
    const actualizada = validarSesionActualizada(sesion, user);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(actualizada));
      localStorage.setItem('xpay_token', actualizada.token);
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      localStorage.removeItem('xpay_token');
      throw new Error('No pudimos guardar tu sesión. Vuelve a ingresar.');
    }
    setUser(actualizada);
  }

  return (
    <AuthContext.Provider value={{ user, login, logout, actualizarToken, actualizarSesion }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthCtx {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}

// QA/Demo role detection
// Temporary demo rule documented in docs/QA_DEMO_BUSINESS_USERS.md
export type UserView = 'admin' | 'wallet' | 'comercio' | 'empresa';

export function isAdminUser(user: AuthUser): boolean {
  return user.roles.includes('ADMIN_XPAY') || user.roles.includes('OPERADOR_XPAY') || user.roles.includes('SUPERUSUARIO');
}

export function getViewForUser(user: AuthUser): UserView {
  if (user.roles.includes('ADMIN_XPAY') || user.roles.includes('OPERADOR_XPAY') || user.roles.includes('SUPERUSUARIO')) return 'admin';
  if (user.roles.includes('COMERCIO') || user.usuario === 'qa.comercio1') return 'comercio';
  // D10 — antes dependía del nombre fijo qa.empresa1, inexistente en PROD.
  if (user.esUsuarioEmpresa === true) return 'empresa';
  return 'wallet';
}
