// XPAY-431 (Block C v1) — notificación persistente al RECEPTOR de una
// transferencia interna Wallet-a-Wallet. Monta WalletHero (la campana) y
// UserWalletPage juntos bajo UN solo WalletNotificationsProvider — la misma
// relación de hermanos bajo un ancestro común que existe en producción
// (Layout.tsx → WalletShell(WalletHero) + <Outlet/>(UserWalletPage)),
// probando así la pieza de estado compartido real, no una simulación
// aislada. CERO red real: api/client.ts está completamente mockeado.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { UserWalletPage } from './UserWalletPage.tsx';
import { WalletHero } from '../components/wallet/WalletHero.tsx';
import { WalletNotificationsProvider } from '../components/wallet/WalletNotificationsContext.tsx';
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

vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn(() => Promise.resolve('data:image/mock;text,x')) } }));
vi.mock('html5-qrcode', () => ({
  Html5Qrcode: class {
    start() { return new Promise(() => { /* never settles — no se ejercita el scanner en este archivo */ }); }
    stop() { return Promise.resolve(); }
    clear() {}
  },
}));

const ID_WALLET = 2;

interface MovFixture {
  idMovimiento: number;
  fecha: string;
  tipoMovimiento: string;
  naturaleza: string;
  valor: number;
  saldoDespues: number;
  descripcion: string | null;
  referenciaTipo: string | null;
  referenciaId: number | null;
}

function mov(overrides: Partial<MovFixture> & { idMovimiento: number }): MovFixture {
  return {
    fecha: '2026-09-20T10:00:00Z',
    tipoMovimiento: 'RECARGA',
    naturaleza: 'C',
    valor: 1000,
    saldoDespues: 10000,
    descripcion: 'Movimiento de prueba',
    referenciaTipo: null,
    referenciaId: null,
    ...overrides,
  };
}

// Historial "ya existente" antes de que Block C se instale por primera vez —
// incluye una TRANSFERENCIA_ENTRADA histórica (id=1) que NUNCA debe aparecer
// como no-leída tras el bootstrap (XPAY-431 §3).
function baseHistorial(): MovFixture[] {
  return [
    mov({ idMovimiento: 3, tipoMovimiento: 'TRANSFERENCIA_SALIDA', naturaleza: 'D', valor: 1000, referenciaTipo: 'wallets', referenciaId: 3 }),
    mov({ idMovimiento: 2, tipoMovimiento: 'RECARGA', naturaleza: 'C', valor: 5000 }),
    mov({ idMovimiento: 1, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 2000, referenciaTipo: 'wallets', referenciaId: 3 }),
  ];
}

let movimientosState: MovFixture[] = [];

function apiRoutedGet(path: string) {
  if (path === '/api/wallets/mi-wallet') {
    return Promise.resolve({ success: true, data: { idWallet: ID_WALLET, idPersona: 3, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA' } });
  }
  if (path === '/api/reportes/mi-estado-cuenta') {
    return Promise.resolve({
      success: true,
      data: {
        idWallet: ID_WALLET, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA',
        saldoDisponible: 90000, saldoRetenido: 0,
        movimientos: [...movimientosState].sort((a, b) => b.idMovimiento - a.idMovimiento),
      },
    });
  }
  if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
  if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
  if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
  return Promise.reject(new Error(`unmocked GET ${path}`));
}

function lastSeenKey(): string {
  return `xpay.wallet.${ID_WALLET}.notifications.lastSeenMovementId`;
}

function seedLastSeen(idMovimiento: number): void {
  window.localStorage.setItem(lastSeenKey(), String(idMovimiento));
}

function renderHarness() {
  mockGet.mockImplementation(apiRoutedGet);
  const router = createMemoryRouter(
    [{ path: '/mi-wallet', element: <UserWalletPage /> }],
    { initialEntries: ['/mi-wallet?tab=saldo'] },
  );
  const utils = render(
    <WalletNotificationsProvider>
      <WalletHero onAction={() => {}} onOpenProfile={() => {}} />
      <RouterProvider router={router} />
    </WalletNotificationsProvider>,
  );
  return { ...utils, router };
}

async function waitForInitialLoad() {
  await waitFor(() => expect(screen.getByText('Disponible')).toBeInTheDocument());
}

function notifDot(): Element | null {
  return document.querySelector('.wh-notif-dot');
}

// La página completa también muestra "Últimos movimientos" (mismos datos,
// formato "+$ 300"/"-$ 300") — todas las aserciones sobre el CONTENIDO de
// una notificación deben quedar acotadas al panel (within), nunca a
// screen.getByText a secas, para no colisionar con esa lista.
function getPanel() {
  return screen.getByRole('dialog', { name: 'Notificaciones' });
}

beforeEach(() => {
  mockGet.mockReset();
  mockPost.mockReset();
  window.localStorage.clear();
  movimientosState = baseHistorial();
  vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval'] });
});

afterEach(() => {
  vi.useRealTimers();
});

describe('Block C v1 — notificación persistente al receptor (XPAY-431)', () => {
  it('A: primer uso sin cursor — el historial existente crea baseline y NO aparece como unread', async () => {
    renderHarness();
    await waitForInitialLoad();

    expect(notifDot()).toBeNull();
    expect(window.localStorage.getItem(lastSeenKey())).toBe('3'); // baseline = movimiento más reciente ya cargado
  });

  it('B: nueva TRANSFERENCIA_ENTRADA entre polls → aparece el badge/unread', async () => {
    seedLastSeen(3); // simula "ya se usó antes" — al día con el historial base
    renderHarness();
    await waitForInitialLoad();
    expect(notifDot()).toBeNull();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);

    await waitFor(() => expect(notifDot()).not.toBeNull());
  });

  it('C: el contenido de la notificación muestra el monto correcto', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    // Acotado al panel: la página también muestra "Últimos movimientos" con
    // el mismo monto ("+$ 300"), un screen.getByText sin acotar colisionaría.
    expect(within(getPanel()).getByText('$ 300', { exact: false })).toBeInTheDocument();
  });

  it('D: contraparte conocida (mapeada) muestra el nombre legible, no el ID técnico', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(screen.getByText('qa.usuario2', { selector: 'strong' })).toBeInTheDocument();
  });

  it('E: contraparte desconocida (sin mapear) cae al fallback "Wallet #N" — nunca inventa un nombre', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 999 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(screen.getByText('Wallet #999', { selector: 'strong' })).toBeInTheDocument();
  });

  it('F: múltiples recepciones entre dos polls — TODAS aparecen, no solo la última', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(
      mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 100, referenciaTipo: 'wallets', referenciaId: 3 }),
      mov({ idMovimiento: 5, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 200, referenciaTipo: 'wallets', referenciaId: 3 }),
    );
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    const panel = within(getPanel());
    expect(panel.getByText('$ 100', { exact: false })).toBeInTheDocument();
    expect(panel.getByText('$ 200', { exact: false })).toBeInTheDocument();
  });

  it('G: polls sucesivos sin novedad NO duplican las notificaciones ya detectadas', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    // Segundo (y tercer) poll, sin agregar nada nuevo.
    await vi.advanceTimersByTimeAsync(7000);
    await vi.advanceTimersByTimeAsync(7000);

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(within(getPanel()).getAllByText('$ 300', { exact: false })).toHaveLength(1);
  });

  it('H: refresh/remount — el unread persiste (se reconstruye desde localStorage + movimientos)', async () => {
    seedLastSeen(3);
    const first = renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    // Simula refresh/reapertura: se desmonta TODO el árbol (WalletNotificationsProvider
    // incluido — su estado en memoria se pierde, igual que en un reload real)
    // y se monta uno nuevo. El movimiento id=4 sigue disponible vía el mock
    // GET (equivalente al backend, que nunca pierde el dato).
    first.unmount();
    renderHarness();
    await waitForInitialLoad();

    await waitFor(() => expect(notifDot()).not.toBeNull());
  });

  it('I: abrir la campana NO marca automáticamente como visto', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    // El panel está abierto y muestra la notificación — el badge sigue ahí,
    // abrir no la descarta por sí solo.
    expect(within(getPanel()).getByText('$ 300', { exact: false })).toBeInTheDocument();
    expect(notifDot()).not.toBeNull();
  });

  it('J: "Marcar como vistas" limpia el unread y el badge desaparece', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    await user.click(screen.getByRole('button', { name: 'Marcar como vistas' }));

    expect(notifDot()).toBeNull();
    expect(screen.getByText('No tienes notificaciones nuevas.')).toBeInTheDocument();
    expect(window.localStorage.getItem(lastSeenKey())).toBe('4');
  });

  it('K: remount después de marcar como vistas — la misma notificación NO reaparece', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    const first = renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 300, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    await user.click(screen.getByRole('button', { name: 'Marcar como vistas' }));
    expect(notifDot()).toBeNull();

    first.unmount();
    renderHarness();
    await waitForInitialLoad();

    expect(notifDot()).toBeNull();
  });

  it('L: un movimiento que NO es TRANSFERENCIA_ENTRADA no genera notificación de recepción', async () => {
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_SALIDA', naturaleza: 'D', valor: 500, referenciaTipo: 'wallets', referenciaId: 3 }));
    await vi.advanceTimersByTimeAsync(7000);

    // Deja correr un poll extra para estar seguro de que el estado ya se asentó.
    await vi.advanceTimersByTimeAsync(7000);
    expect(notifDot()).toBeNull();
  });

  it('M: Block C nunca dispara ninguna operación financiera (0 POST) durante todo el flujo', async () => {
    const user = userEvent.setup({ delay: null });
    seedLastSeen(3);
    renderHarness();
    await waitForInitialLoad();

    movimientosState.push(
      mov({ idMovimiento: 4, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 100, referenciaTipo: 'wallets', referenciaId: 3 }),
      mov({ idMovimiento: 5, tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', valor: 200, referenciaTipo: 'wallets', referenciaId: 999 }),
    );
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await user.click(screen.getByRole('button', { name: 'Notificaciones' }));
    await user.click(screen.getByRole('button', { name: 'Marcar como vistas' }));

    expect(mockPost).not.toHaveBeenCalled();
  });
});
