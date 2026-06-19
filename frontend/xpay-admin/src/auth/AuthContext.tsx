import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { post } from '../api/client.ts';

export interface AuthUser {
  idUsuario: number;
  idPersona: number;
  usuario:   string;
  estado:    string;
  roles:     string[];
  token:     string;
}

interface AuthCtx {
  user:   AuthUser | null;
  login:  (usuario: string, password: string) => Promise<void>;
  logout: () => void;
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
    window.addEventListener('xpay:unauthorized', handleUnauthorized);
    return () => window.removeEventListener('xpay:unauthorized', handleUnauthorized);
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

  return (
    <AuthContext.Provider value={{ user, login, logout }}>
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
  return user.roles.includes('ADMIN_XPAY') || user.roles.includes('OPERADOR_XPAY');
}

export function getViewForUser(user: AuthUser): UserView {
  if (user.roles.includes('ADMIN_XPAY') || user.roles.includes('OPERADOR_XPAY')) return 'admin';
  if (user.roles.includes('COMERCIO') || user.usuario === 'qa.comercio1') return 'comercio';
  if (user.usuario === 'qa.empresa1') return 'empresa';
  return 'wallet';
}
