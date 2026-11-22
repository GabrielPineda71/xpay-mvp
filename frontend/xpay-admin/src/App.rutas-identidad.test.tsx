// P0-B frontend — las cuatro rutas de identidad son PÚBLICAS: con el App
// real (BrowserRouter + AuthProvider) y sin sesión, se renderizan en lugar de
// redirigir a /login. Las consultas se simulan y el token es sintético.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import App from './App.tsx';
import { ApiError } from './api/client.ts';

const mockPost = vi.fn();
const mockGet = vi.fn();

vi.mock('./api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('./api/client.ts')>('./api/client.ts');
  return {
    ...actual,
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
  };
});

const TOKEN = 'Ab3_-'.repeat(8) + 'Xy9';

beforeEach(() => {
  mockPost.mockReset();
  mockGet.mockReset();
  mockGet.mockImplementation((ruta: string) => Promise.reject(ruta === '/api/usuarios/preparacion-wallet'
    ? new ApiError('Recorrido cerrado en esta prueba.', 404) : new Error('Consulta no disponible en esta prueba.')));
  localStorage.clear();
  sessionStorage.clear();
});

afterEach(() => { vi.unstubAllEnvs(); });

it.each([
  '/',
  '/mi-wallet?tab=retirar-breb',
  '/mi-wallet?tab=recargar-breb',
  '/mi-wallet/libranza',
])('en PROD lleva %s a Perfil con una sesión de alta limitada', async ruta => {
  vi.stubEnv('VITE_XPAY_ENV', 'PROD');
  localStorage.setItem('xpay_user', JSON.stringify({
    idUsuario: 33, idPersona: 44, usuario: 'persona.nueva', estado: 'ACTIVO',
    roles: ['USUARIO_FINAL'], token: 'synthetic-test-token', requiereCambioClave: false, requierePerfil: true,
  }));
  window.history.pushState({}, '', ruta);
  render(<App />);

  expect(await screen.findByRole('heading', { name: 'Perfil' })).toBeInTheDocument();
  expect(window.location.pathname).toBe('/mi-wallet/perfil');
  expect(screen.getByRole('button', { name: 'Cerrar sesión' })).toBeInTheDocument();
  expect(screen.queryByText('Mi Wallet')).not.toBeInTheDocument();
  expect(mockGet).not.toHaveBeenCalledWith('/api/wallets/mi-wallet');
  expect(mockGet).not.toHaveBeenCalledWith('/api/pin/estado');
  expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
  expect(mockPost).not.toHaveBeenCalled();
});

describe('App — rutas públicas de identidad (P0-B frontend)', () => {
  it.each([
    ['/verificar-email#token=' + TOKEN, 'Verificación de correo'],
    ['/restablecer-clave#token=' + TOKEN, 'Restablecer contraseña'],
    ['/recuperar-clave', 'Recuperar contraseña'],
    ['/reenviar-verificacion', 'Reenviar verificación de correo'],
  ])('%s se renderiza sin sesión', async (ruta, subtitulo) => {
    window.history.pushState({}, '', ruta);
    render(<App />);
    expect(await screen.findByText(subtitulo, { selector: '.subtitle' })).toBeInTheDocument();
    expect(screen.queryByText('Panel administrativo')).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('con el App real, el fragmento #token desaparece de window.location', async () => {
    window.history.pushState({}, '', '/verificar-email#token=' + TOKEN);
    render(<App />);
    await waitFor(() => expect(window.location.hash).toBe(''));
    expect(window.location.pathname).toBe('/verificar-email');
    expect(window.location.href).not.toContain(TOKEN);
  });
});
