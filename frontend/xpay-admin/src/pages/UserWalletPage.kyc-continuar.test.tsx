// API y sesión Veriff simuladas: ninguna prueba inicia verificaciones reales.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { UserWalletPage } from './UserWalletPage.tsx';
import { WalletLlaveBrebAviso } from '../components/wallet/WalletLlaveBrebAviso.tsx';
import type { AuthUser } from '../auth/AuthContext.tsx';

const mockGet = vi.fn();
const mockPost = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) };
});

const mockUser: AuthUser = {
  idUsuario: 3, idPersona: 3, usuario: 'piloto.prueba', estado: 'ACTIVO',
  roles: [], token: 'synthetic-test-token', requiereCambioClave: false,
};

vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual('../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: mockUser, login: vi.fn(), logout: vi.fn(), actualizarToken: vi.fn() }) };
});

vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn(() => Promise.resolve('data:image/mock;text,x')) } }));
vi.mock('html5-qrcode', () => ({ Html5Qrcode: class {} }));

const SESSION_URL = 'https://magic.veriff.me/v/synthetic-test-session';
let kyc: { estadoKyc: string; sessionUrl?: string | null };
let kycError: boolean;
let kycQuery: Promise<unknown> | null;
let llave: unknown;

function renderWallet(conAviso = false) {
  return render(<RouterProvider router={createMemoryRouter(
    [{ path: '/mi-wallet', element: <>{conAviso && <WalletLlaveBrebAviso />}<UserWalletPage /></> }],
    { initialEntries: ['/mi-wallet'] },
  )} />);
}

beforeEach(() => {
  window.history.replaceState({}, '', '/mi-wallet');
  kyc = { estadoKyc: 'PENDIENTE', sessionUrl: SESSION_URL };
  kycError = false;
  kycQuery = null;
  llave = null;
  mockGet.mockReset().mockImplementation((path: string) => {
    if (path === '/api/kyc/mi-estado') {
      if (kycQuery) return kycQuery;
      return kycError ? Promise.reject(new Error('Fallo de consulta simulado')) : Promise.resolve({ success: true, data: kyc });
    }
    if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 3, nombreWallet: 'Wallet de prueba', estado: 'ACTIVA' } });
    if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: { idWallet: 2, nombreWallet: 'Wallet de prueba', estado: 'ACTIVA', saldoDisponible: 0, saldoRetenido: 0, movimientos: [] } });
    if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: llave });
    if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
    return Promise.reject(new Error(`GET no simulado: ${path}`));
  });
  mockPost.mockReset();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('UserWalletPage — continuar una sesión Veriff pendiente', () => {
  it('mientras la primera consulta no responde no inventa un estado ni ofrece iniciar otra sesión', async () => {
    kycQuery = new Promise(() => {});
    renderWallet();
    expect(await screen.findByText('Consultando tu verificación...')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Actualizando...' })).toBeDisabled();
    expect(screen.queryByText('No iniciado')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('si falla la consulta inicial permite actualizar y recuperar la misma sesión, sin iniciar otra', async () => {
    const user = userEvent.setup();
    kycError = true;
    renderWallet();
    expect(await screen.findByText(/No pudimos actualizar tu verificación/)).toBeInTheDocument();
    expect(screen.queryByText('No iniciado')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
    kycError = false;

    await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', SESSION_URL);
    expect(screen.queryByRole('button', { name: 'Iniciar verificación' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('recupera el mismo enlace al volver a abrir la Wallet sin crear otra sesión', async () => {
    const user = userEvent.setup();
    const first = renderWallet();
    const link = await screen.findByRole('link', { name: 'Continuar verificación' });
    expect(link).toHaveAttribute('href', SESSION_URL);
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    await user.click(link);
    expect(mockPost).not.toHaveBeenCalled();
    first.unmount();

    renderWallet();
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', SESSION_URL);
    expect(mockGet.mock.calls.filter(([path]) => path === '/api/kyc/mi-estado')).toHaveLength(2);
    expect(screen.queryByRole('button', { name: 'Iniciar verificación' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('regresar de Veriff no afirma que la información fue enviada', async () => {
    window.history.replaceState({}, '', '/mi-wallet?kyc=return');
    renderWallet();
    await screen.findByRole('link', { name: 'Continuar verificación' });
    expect(screen.getByText(/Regresaste de Veriff/)).toHaveTextContent('Puedes continuar si no terminaste. Si ya enviaste la información');
    expect(screen.queryByText(/La verificación fue enviada/)).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('al confirmar APROBADO desaparece el enlace aunque el servidor aún envíe la URL', async () => {
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole('link', { name: 'Continuar verificación' });
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mis-retiros');
    kyc = { estadoKyc: 'APROBADO', sessionUrl: SESSION_URL };

    await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Iniciar verificación' })).not.toBeInTheDocument();
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/breb/mi-llave'));
    expect(mockGet).toHaveBeenCalledWith('/api/breb/mis-retiros');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('una aprobación detectada por polling carga la llave y sus retiros sin iniciar otra sesión', async () => {
    vi.useFakeTimers();
    await act(async () => { renderWallet(); });
    expect(screen.getByRole('link', { name: 'Continuar verificación' })).toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
    kyc = { estadoKyc: 'APROBADO' };
    await act(async () => { await vi.advanceTimersByTimeAsync(12000); });
    expect(mockGet).toHaveBeenCalledWith('/api/breb/mi-llave');
    expect(mockGet).toHaveBeenCalledWith('/api/breb/mis-retiros');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('actualizar a APROBADO renueva el aviso montado y ofrece registrar la llave solo tras consultarla', async () => {
    const user = userEvent.setup();
    const cambios = vi.fn();
    window.addEventListener('xpay:kyc-estado-actualizado', cambios);
    try {
      renderWallet(true);
      expect(await screen.findByRole('link', { name: 'Verificar identidad en Mi Wallet' })).toBeInTheDocument();
      await screen.findByRole('link', { name: 'Continuar verificación' });
      expect(cambios).toHaveBeenCalledTimes(1);
      await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
      expect(cambios).toHaveBeenCalledTimes(1);
      kyc = { estadoKyc: 'APROBADO' };
      await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
      expect(await screen.findByRole('link', { name: 'Registrar mi llave en Perfil' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Verificar identidad en Mi Wallet' })).not.toBeInTheDocument();
      expect(cambios).toHaveBeenCalledTimes(2);
      expect(mockPost).not.toHaveBeenCalled();
    } finally {
      window.removeEventListener('xpay:kyc-estado-actualizado', cambios);
    }
  });

  it('la aprobación por polling retira el aviso montado cuando la llave ya está validada', async () => {
    vi.useFakeTimers();
    await act(async () => { renderWallet(true); });
    expect(screen.getByRole('link', { name: 'Verificar identidad en Mi Wallet' })).toBeInTheDocument();
    kyc = { estadoKyc: 'APROBADO' };
    llave = { idBrebLlave: 8, keyType: 'ALPHA', keyValueMasked: '***0118', estado: 'VALIDADA' };
    await act(async () => { await vi.advanceTimersByTimeAsync(12000); });
    expect(mockGet).toHaveBeenCalledWith('/api/breb/mi-llave');
    expect(screen.queryByRole('complementary', { name: 'Llave Bre-B para retiros' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('EN_REVISION no ofrece continuar ni iniciar otra sesión', async () => {
    kyc = { estadoKyc: 'EN_REVISION', sessionUrl: SESSION_URL };
    renderWallet();
    expect(await screen.findByText('Tu verificación está en revisión.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('sin enlace permite volver a consultarlo y recupera la sesión existente', async () => {
    const user = userEvent.setup();
    kyc = { estadoKyc: 'PENDIENTE', sessionUrl: null };
    renderWallet();
    expect(await screen.findByText(/El enlace para continuar no está disponible/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Iniciar verificación' })).not.toBeInTheDocument();
    kyc = { estadoKyc: 'PENDIENTE', sessionUrl: SESSION_URL };

    await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', SESSION_URL);
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('un error de actualización conserva la sesión y permite consultar de nuevo', async () => {
    const user = userEvent.setup();
    renderWallet();
    await screen.findByRole('link', { name: 'Continuar verificación' });
    kycError = true;
    await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
    expect(await screen.findByText(/No pudimos actualizar tu verificación/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', SESSION_URL);
    expect(screen.getByRole('button', { name: 'Actualizar estado' })).toBeEnabled();
    kycError = false;
    await user.click(screen.getByRole('button', { name: 'Actualizar estado' }));
    await waitFor(() => expect(screen.queryByText(/No pudimos actualizar tu verificación/)).not.toBeInTheDocument());
    expect(mockPost).not.toHaveBeenCalled();
  });

  it.each([
    'javascript:alert(1)',
    'data:text/html,synthetic-test',
    'http://magic.veriff.me/v/synthetic-test-session',
    '/v/synthetic-test-session',
    'https://synthetic-user@magic.veriff.me/v/synthetic-test-session',
    'https://magic.veriff.me:444/v/synthetic-test-session',
    'https://magic.veriff.me.evil.com/v/synthetic-test-session',
    'https://saas.veriff.com.evil.com/v/synthetic-test-session',
    'https://localhost/v/synthetic-test-session',
    'https://magic.veriff.me/otra-ruta',
  ])('no presenta un enlace de sesión inseguro: %s', async sessionUrl => {
    kyc = { estadoKyc: 'PENDIENTE', sessionUrl };
    renderWallet();
    expect(await screen.findByText(/El enlace para continuar no está disponible/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Continuar verificación' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Actualizar estado' })).toBeEnabled();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('admite el otro origen oficial de sesión y el puerto HTTPS estándar', async () => {
    kyc = { estadoKyc: 'PENDIENTE', sessionUrl: 'https://alchemy.veriff.com:443/v/synthetic-test-session' };
    renderWallet();
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', 'https://alchemy.veriff.com/v/synthetic-test-session');
  });

  it('recupera la sesión pendiente del dominio exacto utilizado en producción', async () => {
    kyc = { estadoKyc: 'PENDIENTE', sessionUrl: 'https://saas.veriff.com/v/synthetic-test-session' };
    renderWallet();
    expect(await screen.findByRole('link', { name: 'Continuar verificación' })).toHaveAttribute('href', 'https://saas.veriff.com/v/synthetic-test-session');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('una respuesta de inicio con URL insegura no abre una ventana ni vuelve a iniciar', async () => {
    const user = userEvent.setup();
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    kyc = { estadoKyc: 'NO_INICIADO' };
    mockPost.mockResolvedValue({ success: true, data: { estadoKyc: 'PENDIENTE', sessionId: 'synthetic-test-session', sessionUrl: 'javascript:alert(1)' } });
    renderWallet();
    await user.click(await screen.findByRole('button', { name: 'Iniciar verificación' }));
    expect(await screen.findByText(/El enlace para continuar no está disponible/)).toBeInTheDocument();
    expect(open).not.toHaveBeenCalled();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/kyc/veriff/session', {});
    expect(screen.queryByRole('button', { name: 'Iniciar verificación' })).not.toBeInTheDocument();
  });
});
