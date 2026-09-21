// XPAY-451 — fixes de XPAY-450 sobre "Comprar con QR": preview read-only
// del receptor ANTES del pago (P4) y estado de éxito dedicado y persistente
// DESPUÉS del pago (P1). CERO red real: api/client.ts está completamente
// mockeado. Usa exclusivamente el flujo de "pegar código" — NUNCA la
// cámara/scanner real (mismo criterio ya establecido en
// UserWalletPage.xpay426-envio-exitoso.test.tsx) — XPAY-422..425 (scanner)
// permanece intacto y sin ejercitarse aquí.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { UserWalletPage } from './UserWalletPage.tsx';
import type { AuthUser } from '../auth/AuthContext.tsx';

const mockGet  = vi.fn();
const mockPost = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return {
    ...actual,
    get:  (...args: unknown[]) => mockGet(...args),
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
// Nunca se interactúa con la cámara real en estos tests (flujo de "pegar
// código") — mismo criterio que UserWalletPage.xpay426-envio-exitoso.test.tsx.
vi.mock('html5-qrcode', () => ({
  Html5Qrcode: class {
    start() { return new Promise(() => { /* never settles */ }); }
    stop() { return Promise.resolve(); }
    clear() {}
  },
}));

const CUENTA_BASE = {
  idWallet: 2, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA',
  saldoDisponible: 400, saldoRetenido: 0, movimientos: [],
};

const PREVIEW_VALIDO = {
  codigoQr: 'QR-UAT-XPAY-001', idComercio: 3, nombreComercio: 'QA UAT Comercio 1 Operativo',
  idTienda: 3, nombreTienda: 'QA UAT Tienda 1',
};

// Controlable por test: 'ok' | 'not-found' | 'network-error' | 'pending'.
// XPAY-453 — 'pending' se agrega para representar un preview que nunca
// resuelve (F1). Se resuelve DENTRO de apiRoutedGet, el mismo router que
// renderWalletAt() ya instala vía mockGet.mockImplementation(apiRoutedGet)
// — antes, F1 llamaba mockGet.mockImplementation(...) con un router propio
// ANTES de renderWalletAt(), pero renderWalletAt() lo sobrescribía
// internamente con apiRoutedGet, perdiendo el override sin que el test lo
// notara (falla confirmada en CI de XPAY-452). Usar el mismo mecanismo de
// resolverMode que el resto de la suite evita ese problema de raíz: no hay
// ningún override que pueda perderse.
let resolverMode: 'ok' | 'not-found' | 'network-error' | 'pending' = 'ok';

function apiRoutedGet(path: string) {
  if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 3, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA' } });
  if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: CUENTA_BASE });
  if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
  if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
  if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
  if (path.startsWith('/api/qr/resolver')) {
    if (resolverMode === 'ok') return Promise.resolve({ success: true, data: PREVIEW_VALIDO });
    if (resolverMode === 'not-found') return Promise.reject(new Error('QR no disponible para pago.'));
    if (resolverMode === 'pending') return new Promise(() => { /* nunca resuelve, deliberado (F1) */ });
    return Promise.reject(new Error('No fue posible conectar con el backend XPAY. Verifica la URL del API o la conexión.'));
  }
  return Promise.reject(new Error(`unmocked GET ${path}`));
}

function renderWalletAt(initialTab = 'pagar') {
  mockGet.mockImplementation(apiRoutedGet);
  const router = createMemoryRouter(
    [{ path: '/mi-wallet', element: <UserWalletPage /> }],
    { initialEntries: [`/mi-wallet?tab=${initialTab}`] },
  );
  const utils = render(<RouterProvider router={router} />);
  return { ...utils, router };
}

beforeEach(() => {
  resolverMode = 'ok';
  mockGet.mockReset();
  mockPost.mockReset();
});

// Llega hasta pagQrCode poblado usando el flujo de "pegar código" — NUNCA
// la cámara. Deliberadamente un código plano (no JSON): parseMerchantQr lo
// trata como qrCode directo, sin monto embebido (mismo camino que
// "Otras opciones" ya prueba en producción para códigos impresos/QA).
async function pegarQr(user: ReturnType<typeof userEvent.setup>, codigoQr = 'QR-UAT-XPAY-001') {
  await user.click(screen.getByText('Otras opciones'));
  await user.type(screen.getByLabelText('Pegar código QR o contenido JSON'), codigoQr);
  await user.click(screen.getByRole('button', { name: 'Usar código pegado' }));
}

async function completarPagoExitoso(user: ReturnType<typeof userEvent.setup>, valor = '100') {
  await waitFor(() => expect(screen.getByText('QA UAT Comercio 1 Operativo')).toBeInTheDocument());
  await user.click(screen.getByRole('button', { name: '💳 Pagar con Wallet' }));
  await user.type(screen.getByLabelText('Valor a pagar (COP ficticio)'), valor);
  await user.type(screen.getByLabelText(/Clave de 7 dígitos/), '1234567');
  await user.click(screen.getByRole('button', { name: 'Pagar QR con Wallet' }));
}

describe('UserWalletPage — Comprar con QR: preview del receptor + confirmación de éxito (XPAY-451)', () => {
  // F1 — QR leído → preview loading → no se ofrece pagar todavía.
  it('F1: mientras el preview está cargando, no se muestra el selector de método de pago', async () => {
    const user = userEvent.setup();
    resolverMode = 'pending';
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());

    await pegarQr(user);

    expect(screen.getByText('Validando QR...')).toBeInTheDocument();
    expect(screen.queryByText('¿Cómo quieres pagar?')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Pagar QR con Wallet' })).toBeNull();
  });

  // F2 — preview válido muestra nombre de comercio + tienda.
  it('F2: preview válido muestra el nombre real de comercio y tienda', async () => {
    const user = userEvent.setup();
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());

    await pegarQr(user);

    await waitFor(() => expect(screen.getByText('QA UAT Comercio 1 Operativo')).toBeInTheDocument());
    expect(screen.getByText('QA UAT Tienda 1')).toBeInTheDocument();
  });

  // F3 — preview inválido (QR no encontrado/inactivo) no permite pagar.
  it('F3: preview inválido (QR no disponible) no permite pagar', async () => {
    const user = userEvent.setup();
    resolverMode = 'not-found';
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());

    await pegarQr(user);

    await waitFor(() => expect(screen.getByText('QR no disponible para pago.')).toBeInTheDocument());
    expect(screen.queryByText('¿Cómo quieres pagar?')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Pagar QR con Wallet' })).toBeNull();
  });

  // F4 — error de red/preview también bloquea el pago.
  it('F4: un error del preview (red/servidor) tampoco permite pagar', async () => {
    const user = userEvent.setup();
    resolverMode = 'network-error';
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());

    await pegarQr(user);

    await waitFor(() => expect(screen.getByText(/No fue posible conectar/)).toBeInTheDocument());
    expect(screen.queryByText('¿Cómo quieres pagar?')).toBeNull();
  });

  // F5/F6/F7/F8 — éxito muestra un estado dedicado con monto real, Venta #
  // real (de response.data), y comercio/tienda del preview ya validado.
  it('F5-F8: el éxito muestra un estado dedicado con monto, Venta # real, comercio y tienda', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({
      success: true, message: 'Pago QR realizado exitosamente.',
      data: { idVentaQr: 34, idTransaccion: 169, idComercio: 3, idTienda: 3, idWalletUsuario: 13, valor: 100, estado: 'CONTINGENCIA' },
    });
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());
    await pegarQr(user);
    await completarPagoExitoso(user);

    await waitFor(() => expect(screen.getByText('✓ Pago realizado')).toBeInTheDocument());
    expect(screen.getByText('$ 100')).toBeInTheDocument();
    expect(screen.getByText('#34')).toBeInTheDocument();
    expect(screen.getByText('QA UAT Comercio 1 Operativo')).toBeInTheDocument();
    expect(screen.getByText('QA UAT Tienda 1')).toBeInTheDocument();
  });

  // F9/F10 — tras el éxito, el formulario (PIN + botón activo) deja de
  // existir por completo: no hay nada que "reescribir" para reenviar.
  it('F9-F10: después del éxito no queda ningún botón/campo de pago activo', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({
      success: true, message: 'Pago QR realizado exitosamente.',
      data: { idVentaQr: 34, idTransaccion: 169, idComercio: 3, idTienda: 3, idWalletUsuario: 13, valor: 100, estado: 'CONTINGENCIA' },
    });
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());
    await pegarQr(user);
    await completarPagoExitoso(user);
    await waitFor(() => expect(screen.getByText('✓ Pago realizado')).toBeInTheDocument());

    expect(screen.queryByRole('button', { name: 'Pagar QR con Wallet' })).toBeNull();
    expect(screen.queryByLabelText(/Clave de 7 dígitos/)).toBeNull();
    expect(mockPost).toHaveBeenCalledTimes(1); // ninguna llamada adicional se disparó sola.
  });

  // F11 — "Hacer otro pago" es la única acción que resetea el flujo.
  it('F11: "Hacer otro pago" resetea explícitamente el flujo completo', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({
      success: true, message: 'Pago QR realizado exitosamente.',
      data: { idVentaQr: 34, idTransaccion: 169, idComercio: 3, idTienda: 3, idWalletUsuario: 13, valor: 100, estado: 'CONTINGENCIA' },
    });
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());
    await pegarQr(user);
    await completarPagoExitoso(user);
    await waitFor(() => expect(screen.getByText('✓ Pago realizado')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Hacer otro pago' }));

    expect(screen.queryByText('✓ Pago realizado')).toBeNull();
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());
  });

  // Cero POST financiero fuera del único pago deliberado de cada test —
  // confirma que ningún efecto secundario (preview, carga de cuenta, etc.)
  // dispara jamás POST /api/qr/pagar por su cuenta.
  it('el preview (GET) nunca dispara ningún POST financiero', async () => {
    const user = userEvent.setup();
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument());
    await pegarQr(user);
    await waitFor(() => expect(screen.getByText('QA UAT Comercio 1 Operativo')).toBeInTheDocument());

    expect(mockPost).not.toHaveBeenCalled();
  });
});
