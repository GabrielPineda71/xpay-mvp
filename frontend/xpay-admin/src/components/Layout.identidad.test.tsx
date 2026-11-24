import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { AuthUser } from '../auth/AuthContext.tsx';
import { Layout } from './Layout.tsx';

const state = vi.hoisted(() => ({ user: null as AuthUser | null, logout: vi.fn(), rolComercio: 'CAJERO' }));
vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual<typeof import('../auth/AuthContext.tsx')>('../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: state.user, logout: state.logout }) };
});
vi.mock('../auth/useComercioScope.ts', () => ({
  useComercioScope: () => ({ scope: { idComercioExistente: 1, rolComercio: state.rolComercio } }),
}));
vi.mock('./comercio/CommerceNotificationsContext.tsx', () => ({
  CommerceNotificationsProvider: ({ children }: { children: ReactNode }) => children,
}));
vi.mock('./comercio/CommerceNotificationBell.tsx', () => ({ CommerceNotificationBell: () => null }));

function panel() {
  return render(<MemoryRouter><Routes><Route element={<Layout />}><Route path="/" element={<h2>Panel de prueba</h2>} /></Route>
    <Route path="/login" element={<h2>Ingreso de prueba</h2>} /></Routes></MemoryRouter>);
}

beforeEach(() => {
  vi.stubEnv('VITE_XPAY_ENV', 'PROD');
  state.logout.mockReset();
  state.user = { idUsuario: 101, idPersona: 201, usuario: 'operadora.sesion', estado: 'ACTIVO', roles: ['USUARIO_FINAL'],
    token: 'synthetic-local-only', requiereCambioClave: false, esUsuarioEmpresa: true };
});
afterEach(() => vi.unstubAllEnvs());

describe('Identidad de la sesión de escritorio', () => {
  it('empresa muestra la sesión actual con su perfil y omite el diagnóstico API en PROD', () => {
    panel();
    const identidad = within(screen.getByRole('group', { name: 'Usuario activo' }));
    expect(identidad.getByText('operadora.sesion')).toBeInTheDocument();
    expect(identidad.getByText('Usuario de empresa')).toBeInTheDocument();
    expect(screen.queryByText(/^API:/)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Mi Empresa' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Ledger' })).not.toBeInTheDocument();
  });
  it.each([
    ['SUPERUSUARIO', 'Superusuario XPAY'], ['ADMIN_XPAY', 'Administrador XPAY'], ['OPERADOR_XPAY', 'Operador XPAY'],
  ])('el perfil %s corresponde al usuario autenticado', (rol, etiqueta) => {
    state.user = { ...state.user!, usuario: 'administracion.sesion', roles: [rol], esUsuarioEmpresa: false };
    panel();
    const identidad = within(screen.getByRole('group', { name: 'Usuario activo' }));
    expect(identidad.getByText('administracion.sesion')).toBeInTheDocument();
    expect(identidad.getByText(etiqueta)).toBeInTheDocument();
    if (rol === 'OPERADOR_XPAY') expect(screen.queryByRole('link', { name: 'Coincidencias de nómina' })).not.toBeInTheDocument();
    else expect(screen.getByRole('link', { name: 'Coincidencias de nómina' })).toHaveAttribute('href', '/admin/nomina-vinculaciones');
    if (rol === 'OPERADOR_XPAY') expect(screen.queryByRole('link', { name: 'Autorizaciones de anticipo' })).not.toBeInTheDocument();
    else expect(screen.getByRole('link', { name: 'Autorizaciones de anticipo' })).toHaveAttribute('href', '/admin/nomina-autorizaciones');
  });
  it('el comercio muestra el perfil operativo de quien usa la caja', () => {
    state.user = { ...state.user!, usuario: 'caja.sesion', roles: ['COMERCIO'], esUsuarioEmpresa: false };
    panel();
    expect(within(screen.getByRole('group', { name: 'Usuario activo' })).getByText('Cajero de comercio')).toBeInTheDocument();
  });
  it('conserva la salida de la sesión activa', async () => {
    panel();
    await userEvent.click(screen.getByRole('button', { name: 'Cerrar sesión' }));
    expect(state.logout).toHaveBeenCalledOnce();
    expect(screen.getByRole('heading', { name: 'Ingreso de prueba' })).toBeInTheDocument();
  });
});
