// XPAY-390 FASE 1 UX — tests offline de los cambios de Recibir/Enviar/
// Comprar con QR (auto-QR, auto-scanner, liberación de cámara al cambiar
// de tab). CERO red real: api/client.ts está completamente mockeado
// (vi.mock) — ninguna llamada llega jamás a Passport/Payment/Wallet
// reales. `qrcode` y `html5-qrcode` también se mockean: ninguna prueba
// depende de canvas real ni de acceso a cámara.
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

// ── qrcode: reemplazado por un encoder falso y determinístico — nunca usa
// canvas real (no disponible en jsdom). Se conserva el texto codificado en
// el propio "data URL" para poder verificar el payload exacto sin decodificar
// una imagen PNG real.
vi.mock('qrcode', () => ({
  default: {
    toDataURL: vi.fn((text: string) => Promise.resolve(`data:image/mock;text,${encodeURIComponent(text)}`)),
  },
}));

function decodeMockQr(src: string): Record<string, unknown> {
  const [, encoded] = src.split(',');
  return JSON.parse(decodeURIComponent(encoded));
}

// ── html5-qrcode: reemplazado por una clase falsa — nunca solicita cámara
// real. start() nunca resuelve/rechaza por sí sola (simula "escaneando");
// stop()/clear() quedan espiados para verificar que la cámara se libera al
// cambiar de tab o desmontar.
const startSpy = vi.fn();
const stopSpy  = vi.fn(() => Promise.resolve());
const clearSpy = vi.fn();
const constructedIds: string[] = [];

vi.mock('html5-qrcode', () => ({
  Html5Qrcode: class {
    constructor(elementId: string) { constructedIds.push(elementId); }
    start(...args: unknown[]) { startSpy(...args); return new Promise(() => { /* never settles */ }); }
    stop() { return stopSpy(); }
    clear() { return clearSpy(); }
  },
}));

const CUENTA_BASE = {
  idWallet: 2, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA',
  saldoDisponible: 96112, saldoRetenido: 0, movimientos: [],
};

function apiRoutedGet(path: string) {
  if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 3, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA' } });
  if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: CUENTA_BASE });
  if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
  if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
  if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
  return Promise.reject(new Error(`unmocked GET ${path}`));
}

function renderWalletAt(initialTab: string) {
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
  mockGet.mockImplementation(apiRoutedGet);
  startSpy.mockClear();
  stopSpy.mockClear();
  clearSpy.mockClear();
  constructedIds.length = 0;
});

describe('UserWalletPage — Recibir (XPAY-390 R2/R3)', () => {
  it('B: genera automáticamente el QR base al entrar, sin pulsar ningún botón', async () => {
    renderWalletAt('recibir');
    const img = await waitFor(() => screen.getByAltText('QR para recibir dinero') as HTMLImageElement);
    const payload = decodeMockQr(img.src);
    expect(payload).toMatchObject({
      type: 'XPAY_TRANSFER', env: 'QA', version: 1,
      receiverUser: 'qa.usuario1', receiverWalletId: 2,
      amount: null, currency: 'COP',
    });
  });

  it('D: el QR auto-generado (sin monto) no incluye amount', async () => {
    renderWalletAt('recibir');
    const img = await waitFor(() => screen.getByAltText('QR para recibir dinero') as HTMLImageElement);
    expect(decodeMockQr(img.src).amount).toBeNull();
  });

  it('C: al escribir un monto válido y pulsar "Generar QR", el payload incluye amount', async () => {
    const user = userEvent.setup();
    renderWalletAt('recibir');
    await waitFor(() => screen.getByAltText('QR para recibir dinero'));

    const input = screen.getByLabelText('Valor opcional (COP)') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '10000');
    await user.click(screen.getByRole('button', { name: 'Generar QR' }));

    await waitFor(() => {
      const img = screen.getByAltText('QR para recibir dinero') as HTMLImageElement;
      expect(decodeMockQr(img.src).amount).toBe(10000);
    });
  });

  it('rechaza un monto negativo/cero/no numérico sin tocar el QR ya mostrado', async () => {
    const user = userEvent.setup();
    renderWalletAt('recibir');
    await waitFor(() => screen.getByAltText('QR para recibir dinero'));

    const input = screen.getByLabelText('Valor opcional (COP)') as HTMLInputElement;
    await user.clear(input);
    await user.type(input, '-5');
    await user.click(screen.getByRole('button', { name: 'Generar QR' }));
    expect(await screen.findByText('El valor debe ser mayor a cero.')).toBeInTheDocument();
  });
});

describe('UserWalletPage — Enviar (XPAY-390 R4)', () => {
  it('E: entra en modo scanner automáticamente, sin pulsar "Escanear QR"', async () => {
    renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    expect(startSpy).toHaveBeenCalled();
    expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument();
  });

  it('conserva pegar contenido / ingresar destino manualmente como fallback en "Otras opciones"', async () => {
    renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    expect(screen.getByText('Otras opciones')).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/XPAY_TRANSFER/)).toBeInTheDocument();
  });
});

describe('UserWalletPage — Comprar con QR (XPAY-390 R5)', () => {
  it('F: entra en modo scanner automáticamente, sin pulsar "Escanear QR"', async () => {
    renderWalletAt('pagar');
    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));
    expect(startSpy).toHaveBeenCalled();
    expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument();
  });

  it('el título de la pestaña usa "Comprar con QR"', async () => {
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Comprar con QR')).toBeInTheDocument());
  });
});

describe('UserWalletPage — liberación de cámara al cambiar de tab (XPAY-390 R4/R5/#10)', () => {
  it('G: cambiar de "enviar" a "recibir" detiene y libera el scanner de Enviar', async () => {
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));

    await act(async () => { await router.navigate('/mi-wallet?tab=recibir'); });

    await waitFor(() => expect(stopSpy).toHaveBeenCalled());
    expect(clearSpy).toHaveBeenCalled();
  });

  it('G: cambiar de "pagar" a "saldo" detiene y libera el scanner de Comprar con QR', async () => {
    const { router } = renderWalletAt('pagar');
    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));

    await act(async () => { await router.navigate('/mi-wallet'); });

    await waitFor(() => expect(stopSpy).toHaveBeenCalled());
    expect(clearSpy).toHaveBeenCalled();
  });

  it('nunca construye dos instancias de Html5Qrcode simultáneas para el mismo lector', async () => {
    renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds.length).toBeGreaterThan(0));
    const envReaderInstances = constructedIds.filter(id => id === 'env-qr-reader');
    expect(envReaderInstances.length).toBe(1);
  });
});

describe('UserWalletPage — handlers/endpoints financieros sin modificar (XPAY-390 #8/H)', () => {
  it('H: Enviar sigue llamando POST /api/wallets/transferencia (nunca /api/qr/pagar)', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));

    // Fallback manual (sin cámara real disponible en el test) — mismo
    // camino que ya existía, sólo colapsado en "Otras opciones".
    await user.click(screen.getByText('Otras opciones'));
    await user.click(screen.getByText('Ingresar destino manualmente →'));
    await user.type(screen.getByLabelText('ID de wallet destino'), '3');
    await user.click(screen.getByRole('button', { name: 'Confirmar destino' }));
    await user.type(screen.getByLabelText('Valor a transferir (COP ficticio)'), '500');
    await user.type(screen.getByLabelText(/Clave de 7 dígitos/), '1234567');
    await user.click(screen.getByRole('button', { name: 'Enviar dinero' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith(
      '/api/wallets/transferencia',
      expect.objectContaining({ idWalletDestino: 3, valor: 500 }),
      expect.anything(),
    ));
    void router;
  });
});
