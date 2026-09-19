// XPAY-415 — simplificación visual de Mi Wallet: limpieza de textos técnicos/
// QA repetitivos, tarjeta de saldo sin nombre técnico interno, y nuevo bloque
// "Últimos movimientos" (máximo 10, mismos datos/orden que ya entrega
// GET /api/reportes/mi-estado-cuenta — sin endpoint nuevo). CERO red real:
// api/client.ts está completamente mockeado.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
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

// UserWalletPage importa qrcode/html5-qrcode a nivel de módulo (Recibir/
// Enviar/Comprar con QR) — se mockean igual que en el resto de la suite
// aunque estos tests no ejerciten esos tabs, para que el módulo cargue sin
// intentar acceso real a canvas/cámara.
vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn(() => Promise.resolve('data:image/mock;text,x')) } }));
vi.mock('html5-qrcode', () => ({
  Html5Qrcode: class {
    start() { return new Promise(() => { /* never settles */ }); }
    stop() { return Promise.resolve(); }
    clear() {}
  },
}));

function movimiento(n: number) {
  return {
    idMovimiento: n,
    fecha: `2026-09-${String(20 - n).padStart(2, '0')}T10:00:00`,
    tipoMovimiento: 'TRANSFERENCIA_SALIDA',
    naturaleza: n % 2 === 0 ? 'C' : 'D',
    valor: 1000 * n,
    saldoDespues: 96112 - n,
    descripcion: `Movimiento sintético ${n}`,
    referenciaTipo: null,
    referenciaId: null,
  };
}

// 15 movimientos — más de los 10 que deben mostrarse en el bloque de Mi
// Wallet (el tab "Movimientos" completo sigue mostrando todos, sin cambios).
const QUINCE_MOVIMIENTOS = Array.from({ length: 15 }, (_, i) => movimiento(i + 1));

const CUENTA_BASE = {
  idWallet: 2, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA',
  saldoDisponible: 96112, saldoRetenido: 0, movimientos: QUINCE_MOVIMIENTOS,
};

function apiRoutedGet(path: string) {
  if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 3, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA' } });
  if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: CUENTA_BASE });
  if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
  if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
  if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
  return Promise.reject(new Error(`unmocked GET ${path}`));
}

function renderWalletAt(initialTab = 'saldo', getImpl: (path: string) => Promise<unknown> = apiRoutedGet) {
  mockGet.mockImplementation(getImpl);
  const router = createMemoryRouter(
    [{ path: '/mi-wallet', element: <UserWalletPage /> }],
    { initialEntries: [`/mi-wallet?tab=${initialTab}`] },
  );
  const utils = render(<RouterProvider router={router} />);
  return { ...utils, router };
}

beforeEach(() => {
  mockGet.mockReset();
  mockPost.mockReset();
});

describe('UserWalletPage — Mi Wallet simplificado (XPAY-415)', () => {
  it('A: ya no muestra el encabezado técnico QA (Usuario:/Wallet #/QA-Demo, auto-refresh, verificación de identidad repetitiva)', async () => {
    renderWalletAt('saldo');
    await waitFor(() => expect(screen.getByText('Últimos movimientos')).toBeInTheDocument());

    expect(screen.queryByText(/^Usuario:/)).not.toBeInTheDocument();
    expect(screen.queryByText(/QA \/ Demo/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Actualización automática activa/i)).not.toBeInTheDocument();
    expect(screen.queryByText('Actualizar ahora')).not.toBeInTheDocument();
    expect(screen.queryByText('Verificación de identidad:')).not.toBeInTheDocument();
    // "Identidad verificada." (nota redundante con Perfil) se elimina.
    expect(screen.queryByText('Identidad verificada.')).not.toBeInTheDocument();
    // XPAY-415A — corrección de alcance: el bloque KYC (badge de estado)
    // sigue renderizándose exactamente en las mismas condiciones que antes
    // de XPAY-415, sin ninguna política nueva de visibilidad por estado —
    // solo se retiraron esos dos textos puntuales.
    expect(screen.getByText('Aprobado')).toBeInTheDocument();
  });

  it('B: la tarjeta de saldo ya no muestra el nombre técnico interno de la wallet', async () => {
    renderWalletAt('saldo');
    await waitFor(() => expect(screen.getByText('Últimos movimientos')).toBeInTheDocument());
    expect(screen.queryByText('Wallet qa.usuario1')).not.toBeInTheDocument();
    // En su lugar, la tarjeta muestra el usuario (apropiado para cliente).
    expect(screen.getByText('qa.usuario1')).toBeInTheDocument();
    expect(screen.getByText('Disponible')).toBeInTheDocument();
  });

  it('B: muestra "Últimos movimientos" con como máximo 10 filas aunque existan más (15 en el fixture)', async () => {
    renderWalletAt('saldo');
    await waitFor(() => expect(screen.getByText('Últimos movimientos')).toBeInTheDocument());

    // Reutiliza la misma clase .wallet-movement-item que el tab Movimientos.
    const filas = document.querySelectorAll('.wallet-movement-item');
    expect(filas.length).toBe(10);
    // Orden ya entregado por el backend (más reciente primero) — el primer
    // movimiento del fixture es el más reciente.
    expect(screen.getByText('Movimiento sintético 1')).toBeInTheDocument();
    expect(screen.queryByText('Movimiento sintético 11')).not.toBeInTheDocument();
  });

  it('B: "Ver todos los movimientos" navega al tab Movimientos completo (con los 15)', async () => {
    const { router } = renderWalletAt('saldo');
    await waitFor(() => expect(screen.getByText('Últimos movimientos')).toBeInTheDocument());

    screen.getByRole('button', { name: 'Ver todos los movimientos' }).click();
    await waitFor(() => expect(router.state.location.search).toContain('tab=movimientos'));
    await waitFor(() => expect(screen.getByText('Movimientos (15)')).toBeInTheDocument());
  });

  it('B: estado vacío razonable cuando no hay movimientos', async () => {
    renderWalletAt('saldo', (path: string) =>
      path === '/api/reportes/mi-estado-cuenta'
        ? Promise.resolve({ success: true, data: { ...CUENTA_BASE, movimientos: [] } })
        : apiRoutedGet(path));
    await waitFor(() => expect(screen.getByText('Últimos movimientos')).toBeInTheDocument());
    expect(screen.getByText('Sin movimientos registrados.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Ver todos los movimientos' })).not.toBeInTheDocument();
  });

  // Nota: las 4 acciones principales (franja verde) viven en
  // WalletShell.tsx/UserPrimaryActions.tsx — NO en UserWalletPage.tsx — y
  // ya están cubiertas exactamente por UserPrimaryActions.test.tsx
  // (XPAY-390), no modificado por XPAY-415. No se duplica aquí para no
  // montar un componente ajeno a este archivo con un doble de props falso.

  it('C: Recibir ya no muestra el texto técnico "QA/Demo · el QR contiene..."', async () => {
    renderWalletAt('recibir');
    await waitFor(() => expect(screen.getByAltText('QR para recibir dinero')).toBeInTheDocument());
    expect(screen.queryByText(/QA\/Demo · el QR contiene/i)).not.toBeInTheDocument();
  });

  it('D: Enviar ya no muestra el texto técnico "QA/Demo · transferencia ficticia..."', async () => {
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());
    expect(screen.queryByText(/QA\/Demo · transferencia ficticia/i)).not.toBeInTheDocument();
  });

  it('E: Comprar con QR ya no muestra el texto técnico "QA/Demo · pago ficticio..."', async () => {
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());
    expect(screen.queryByText(/QA\/Demo · pago ficticio/i)).not.toBeInTheDocument();
  });
});
