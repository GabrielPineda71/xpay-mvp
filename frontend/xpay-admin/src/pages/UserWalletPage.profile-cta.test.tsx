// XPAY-392 — G/H/I/J: CTA "sin llave apta" en Retirar → Perfil, y
// confirmación de que el Resolve + los endpoints financieros de retiro
// siguen perteneciendo exclusivamente al flujo de Retirar (no se movieron
// a Perfil). Archivo NUEVO y separado de UserWalletPage.phase1-ux.test.tsx
// (XPAY-390) — no se modifica ese archivo. CERO red real.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { UserWalletPage } from './UserWalletPage.tsx';
import type { AuthUser } from '../auth/AuthContext.tsx';

const mockGet = vi.fn();
const mockPost = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return {
    ...actual,
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
  };
});

const mockUser: AuthUser = {
  idUsuario: 3, idPersona: 3, usuario: 'qa.usuario1', estado: 'ACTIVO',
  roles: [], token: 'synthetic-test-token', requiereCambioClave: false,
};

vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual('../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: mockUser, login: vi.fn(), logout: vi.fn(), actualizarToken: vi.fn() }) };
});

// qrcode/html5-qrcode no intervienen en el tab 'retirar-breb', pero
// UserWalletPage los importa a nivel de módulo — se mockean igual que en
// XPAY-390 para evitar cualquier intento real de canvas/cámara.
vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn(() => Promise.resolve('data:image/mock;text,x')) } }));
vi.mock('html5-qrcode', () => ({
  Html5Qrcode: class {
    start() { return new Promise(() => { /* never settles */ }); }
    stop() { return Promise.resolve(); }
    clear() {}
  },
}));

const CUENTA_BASE = {
  idWallet: 2, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA',
  saldoDisponible: 96112, saldoRetenido: 0, movimientos: [],
};

const LLAVE_BCODE = {
  idBrebLlave: 8, tipoSujeto: 'USUARIO', keyType: 'BCODE', keyValueMasked: '***0268',
  estado: 'VALIDADA', fechaValidacion: '2026-09-16T06:40:00Z',
  resolucionVerificadaPassport: true,
};

function makeApiRoutedGet(llave: unknown) {
  return (path: string) => {
    if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 3, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA' } });
    if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: CUENTA_BASE });
    if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
    if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: llave });
    if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
    return Promise.reject(new Error(`unmocked GET ${path}`));
  };
}

function renderWalletAt(initialTab: string, llave: unknown = null) {
  mockGet.mockImplementation(makeApiRoutedGet(llave));
  const router = createMemoryRouter(
    [
      { path: '/mi-wallet', element: <UserWalletPage /> },
      { path: '/mi-wallet/perfil', element: <div>PERFIL_PAGE_STUB</div> },
    ],
    { initialEntries: [`/mi-wallet?tab=${initialTab}`] },
  );
  const utils = render(<RouterProvider router={router} />);
  return { ...utils, router };
}

beforeEach(() => {
  mockGet.mockReset();
  mockPost.mockReset();
});

describe('UserWalletPage — Retirar sin llave apta → CTA a Perfil (XPAY-392 G/H)', () => {
  it('G: sin llave registrada muestra "Primero configura tu llave Bre-B."', async () => {
    renderWalletAt('retirar-breb', null);
    expect(await screen.findByText('Primero configura tu llave Bre-B.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Configurar mi llave Bre-B' })).toBeInTheDocument();
  });

  it('G: con una llave registrada pero NO BCODE, también muestra el CTA (regla existente sin cambios)', async () => {
    renderWalletAt('retirar-breb', { ...LLAVE_BCODE, keyType: 'ID', keyValueMasked: '***7890' });
    expect(await screen.findByText('Primero configura tu llave Bre-B.')).toBeInTheDocument();
  });

  it('H: el CTA navega a /mi-wallet/perfil', async () => {
    const user = userEvent.setup();
    const { router } = renderWalletAt('retirar-breb', null);
    await user.click(await screen.findByRole('button', { name: 'Configurar mi llave Bre-B' }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/mi-wallet/perfil'));
    expect(await screen.findByText('PERFIL_PAGE_STUB')).toBeInTheDocument();
  });
});

describe('UserWalletPage — Resolve y endpoints financieros permanecen en Retirar (XPAY-392 I/J)', () => {
  it('I: con llave BCODE apta, el formulario "Verificar mi llave" (Resolve) sigue presente en Retirar', async () => {
    renderWalletAt('retirar-breb', LLAVE_BCODE);
    expect(await screen.findByRole('button', { name: 'Verificar mi llave' })).toBeInTheDocument();
  });

  it('I: verificar la llave en Retirar llama a POST /api/breb/mi-llave/resolver (nunca se movió a Perfil)', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({
      success: true,
      data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'J*** P***' },
    });
    renderWalletAt('retirar-breb', LLAVE_BCODE);
    await user.type(await screen.findByPlaceholderText('Valor de tu llave BCODE'), '3001234567890');
    await user.click(screen.getByRole('button', { name: 'Verificar mi llave' }));
    await waitFor(() => expect(mockPost).toHaveBeenCalledWith(
      '/api/breb/mi-llave/resolver',
      { KeyValue: '3001234567890' },
    ));
  });

  it('J: el endpoint financiero POST /api/breb/retiros/real permanece sin cambios (monto→confirmar→retiro)', async () => {
    const user = userEvent.setup();
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({
          success: true,
          data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'J*** P***' },
        });
      }
      if (path === '/api/breb/retiros/real') {
        return Promise.resolve({
          success: true,
          data: { idBrebRetiro: 99, valor: 5000, moneda: 'COP', estado: 'LIQUIDADO', fechaSolicitud: '2026-09-18T00:00:00Z' },
        });
      }
      return Promise.reject(new Error(`unmocked POST ${path}`));
    });
    renderWalletAt('retirar-breb', LLAVE_BCODE);
    await user.type(await screen.findByPlaceholderText('Valor de tu llave BCODE'), '3001234567890');
    await user.click(screen.getByRole('button', { name: 'Verificar mi llave' }));
    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/breb/mi-llave/resolver', expect.anything()));

    await user.type(await screen.findByPlaceholderText('Ej: 5000'), '5000');
    await user.click(screen.getByRole('button', { name: 'Continuar' }));
    await user.click(await screen.findByRole('button', { name: 'Confirmar retiro' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith(
      '/api/breb/retiros/real',
      { Monto: 5000 },
      undefined,
      45_000,
    ));
  });
});
