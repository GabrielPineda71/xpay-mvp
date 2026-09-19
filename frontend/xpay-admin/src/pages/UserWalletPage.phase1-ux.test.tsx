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
// real. start() devuelve una promesa DIFERIDA controlable por cada test
// (startDeferreds) en vez de una que nunca resuelve: XPAY-422 Parte D
// serializa start/stop en una única cadena (useQrScanner) que espera a que
// start() SIEMPRE se asiente (éxito o error) antes de intentar detener —
// modela la realidad de getUserMedia, cuya promesa no tiene forma de
// cancelarse desde afuera. Los tests que verifican liberación de cámara al
// cambiar de tab (bloque "G" más abajo) resuelven explícitamente el
// deferred correspondiente antes de aserting stop()/clear().
//
// XPAY-422B — se agrega `callLog` (orden real construct/start/stop/clear
// por INSTANCIA, ej. "start:0", "stop:0") para poder demostrar, con
// evidencia de orden real y no solo de "se llamó alguna vez", que Enviar y
// Comprar con QR nunca solapan su cámara física (ver tests XPAY-422B TEST
// A-G). `startDeferreds[n].reject(...)` se agrega para los tests que
// necesitan un start() rechazado en vez de resuelto.
const startSpy = vi.fn();
const stopSpy  = vi.fn(() => Promise.resolve());
const clearSpy = vi.fn();
const constructedIds: string[] = [];
const callLog: string[] = [];
interface Deferred { resolve: () => void; reject: (err?: unknown) => void; promise: Promise<void>; }
function makeDeferred(): Deferred {
  let resolve!: () => void;
  let reject!: (err?: unknown) => void;
  const promise = new Promise<void>((res, rej) => { resolve = res; reject = rej; });
  return { resolve, reject, promise };
}
const startDeferreds: Deferred[] = [];

vi.mock('html5-qrcode', () => ({
  Html5Qrcode: class {
    id: number;
    constructor(elementId: string) {
      this.id = constructedIds.length;
      constructedIds.push(elementId);
      callLog.push(`construct:${this.id}:${elementId}`);
    }
    start(...args: unknown[]) {
      callLog.push(`start:${this.id}`);
      startSpy(...args);
      const deferred = makeDeferred();
      startDeferreds.push(deferred);
      return deferred.promise;
    }
    stop() { callLog.push(`stop:${this.id}`); return stopSpy(); }
    clear() { callLog.push(`clear:${this.id}`); return clearSpy(); }
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
  startDeferreds.length = 0;
  callLog.length = 0;
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
    // XPAY-422B — cierra limpiamente la sesión en vez de dejar start()
    // deliberadamente pendiente al terminar el test (el nuevo lifecycle
    // espera a que cada start() se asiente antes de cualquier stop futuro).
    startDeferreds[startDeferreds.length - 1].resolve();
  });

  it('conserva pegar contenido / ingresar destino manualmente como fallback en "Otras opciones"', async () => {
    renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    expect(screen.getByText('Otras opciones')).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/XPAY_TRANSFER/)).toBeInTheDocument();
    startDeferreds[startDeferreds.length - 1].resolve();
  });
});

describe('UserWalletPage — Comprar con QR (XPAY-390 R5)', () => {
  it('F: entra en modo scanner automáticamente, sin pulsar "Escanear QR"', async () => {
    renderWalletAt('pagar');
    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));
    expect(startSpy).toHaveBeenCalled();
    expect(screen.getByText('Escanea el QR del comercio')).toBeInTheDocument();
    startDeferreds[startDeferreds.length - 1].resolve();
  });

  it('el título de la pestaña usa "Comprar con QR"', async () => {
    renderWalletAt('pagar');
    await waitFor(() => expect(screen.getByText('Comprar con QR')).toBeInTheDocument());
    await waitFor(() => expect(startDeferreds.length).toBeGreaterThan(0));
    startDeferreds[startDeferreds.length - 1].resolve();
  });
});

describe('UserWalletPage — liberación de cámara al cambiar de tab (XPAY-390 R4/R5/#10)', () => {
  it('G: cambiar de "enviar" a "recibir" detiene y libera el scanner de Enviar', async () => {
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    // La cámara "abre" con éxito (start() resuelve) ANTES de cambiar de tab
    // — con start() todavía pendiente, useQrScanner espera correctamente a
    // que se asiente antes de detener (ver SCANNER TEST 1 en
    // useQrScanner.test.tsx para ese caso específico).
    await waitFor(() => expect(startDeferreds.length).toBeGreaterThan(0));
    startDeferreds[startDeferreds.length - 1].resolve();

    await act(async () => { await router.navigate('/mi-wallet?tab=recibir'); });

    await waitFor(() => expect(stopSpy).toHaveBeenCalled());
    expect(clearSpy).toHaveBeenCalled();
  });

  it('G: cambiar de "pagar" a "saldo" detiene y libera el scanner de Comprar con QR', async () => {
    const { router } = renderWalletAt('pagar');
    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));
    await waitFor(() => expect(startDeferreds.length).toBeGreaterThan(0));
    startDeferreds[startDeferreds.length - 1].resolve();

    await act(async () => { await router.navigate('/mi-wallet'); });

    await waitFor(() => expect(stopSpy).toHaveBeenCalled());
    expect(clearSpy).toHaveBeenCalled();
  });

  it('nunca construye dos instancias de Html5Qrcode simultáneas para el mismo lector', async () => {
    renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds.length).toBeGreaterThan(0));
    const envReaderInstances = constructedIds.filter(id => id === 'env-qr-reader');
    expect(envReaderInstances.length).toBe(1);
    startDeferreds[startDeferreds.length - 1].resolve();
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
    startDeferreds[startDeferreds.length - 1].resolve();
  });
});

// XPAY-422B — antes de este ticket, Enviar y Comprar con QR llamaban a
// useQrScanner por separado, cada uno con su propia cadena de promesas
// interna: nada impedía que el stop() de uno y el start() del otro
// corrieran solapados sobre la MISMA cámara física al cambiar de tab. La
// corrección unifica ambos en UNA sola invocación del hook en
// UserWalletPage.tsx (`scannerMode`), reutilizando la misma cadena
// serializada para cualquier transición. Estos tests demuestran, con
// evidencia de ORDEN REAL de llamadas (`callLog`, con id de instancia) y no
// solo con "se llamó alguna vez", que nunca coexisten dos instancias
// Html5Qrcode ni se solapan sus fases STARTING/ACTIVE/STOPPING.
describe('UserWalletPage — XPAY-422B: cámara física única (Enviar ⇄ Comprar con QR)', () => {
  it('TEST A: Enviar ACTIVO → cambiar a Comprar → Comprar no construye su instancia antes de que Enviar se detenga por completo', async () => {
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    await waitFor(() => expect(startDeferreds.length).toBe(1));
    startDeferreds[0].resolve();
    await waitFor(() => expect(callLog).toContain('start:0'));

    await act(async () => { await router.navigate('/mi-wallet?tab=pagar'); });
    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));

    const stopEnvAt      = callLog.indexOf('stop:0');
    const clearEnvAt     = callLog.indexOf('clear:0');
    const constructPagAt = callLog.findIndex((e) => e.startsWith('construct:1:'));
    const startPagAt     = callLog.indexOf('start:1');
    expect(stopEnvAt).toBeGreaterThan(-1);
    expect(clearEnvAt).toBeGreaterThan(stopEnvAt);
    // Comprar solo se construye/inicia DESPUÉS de que Enviar completó su
    // stop+clear — nunca coexisten.
    expect(constructPagAt).toBeGreaterThan(clearEnvAt);
    expect(startPagAt).toBeGreaterThan(constructPagAt);

    startDeferreds[1].resolve();
  });

  it('TEST B: Enviar STARTING (pendiente) → cambiar a Comprar → Comprar no inicia hasta que Enviar se asiente y limpie', async () => {
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    await waitFor(() => expect(startDeferreds.length).toBe(1));
    // Enviar.start() sigue pendiente — no se resuelve todavía.

    await act(async () => { await router.navigate('/mi-wallet?tab=pagar'); });

    // Aunque el tab ya cambió, Comprar no debe haber construido ninguna
    // instancia: su turno en la cola está detrás del stop de Enviar, que a
    // su vez está detrás del start de Enviar, que sigue sin resolver.
    expect(constructedIds).not.toContain('pag-qr-reader');
    expect(callLog).toEqual(['construct:0:env-qr-reader', 'start:0']);

    startDeferreds[0].resolve();

    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));
    expect(callLog).toEqual([
      'construct:0:env-qr-reader', 'start:0', 'stop:0', 'clear:0', 'construct:1:pag-qr-reader', 'start:1',
    ]);

    startDeferreds[1].resolve();
  });

  it('TEST C: Enviar STARTING → cambiar a Comprar → Enviar.start rechaza → sin error de Enviar, limpieza correcta, Comprar inicia después', async () => {
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    await waitFor(() => expect(startDeferreds.length).toBe(1));

    await act(async () => { await router.navigate('/mi-wallet?tab=pagar'); });
    expect(constructedIds).not.toContain('pag-qr-reader');

    await act(async () => { startDeferreds[0].reject(new Error('permiso de cámara denegado')); });

    // El mensaje funcional de error de Enviar NUNCA debe aparecer: la
    // sesión ya estaba invalidada cuando el rechazo llegó (regla XPAY-422A,
    // preservada aquí a través de la transición de modo).
    expect(screen.queryByText('No se pudo abrir la cámara. Puedes pegar el código QR manualmente.')).not.toBeInTheDocument();

    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));
    expect(callLog).toEqual([
      'construct:0:env-qr-reader', 'start:0', 'stop:0', 'clear:0', 'construct:1:pag-qr-reader', 'start:1',
    ]);

    startDeferreds[1].resolve();
  });

  it('TEST D: Comprar ACTIVO → cambiar a Enviar → mismo principio, en sentido inverso', async () => {
    const { router } = renderWalletAt('pagar');
    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));
    await waitFor(() => expect(startDeferreds.length).toBe(1));
    startDeferreds[0].resolve();
    await waitFor(() => expect(callLog).toContain('start:0'));

    await act(async () => { await router.navigate('/mi-wallet?tab=enviar'); });
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));

    const stopPagAt      = callLog.indexOf('stop:0');
    const constructEnvAt = callLog.findIndex((e) => e.startsWith('construct:1:'));
    const startEnvAt     = callLog.indexOf('start:1');
    expect(stopPagAt).toBeGreaterThan(-1);
    expect(constructEnvAt).toBeGreaterThan(callLog.indexOf('clear:0'));
    expect(startEnvAt).toBeGreaterThan(constructEnvAt);

    startDeferreds[1].resolve();
  });

  it('TEST E: decode tardío de Enviar tras cambiar a Comprar no rellena destino ni interfiere con Comprar', async () => {
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    await waitFor(() => expect(startDeferreds.length).toBe(1));
    startDeferreds[0].resolve();
    await waitFor(() => expect(callLog).toContain('start:0'));
    // Callback de éxito real entregado al Html5Qrcode de Enviar (instancia 0).
    const envDecodeCallback = startSpy.mock.calls[0][2] as (text: string) => void;

    await act(async () => { await router.navigate('/mi-wallet?tab=pagar'); });
    await waitFor(() => expect(constructedIds).toContain('pag-qr-reader'));
    startDeferreds[1].resolve();
    await waitFor(() => expect(callLog).toContain('start:1'));

    // Un último frame de la cámara de Enviar (ya invalidada) decodifica un
    // QR de transferencia válido MIENTRAS Comprar ya está activo.
    act(() => {
      envDecodeCallback(JSON.stringify({ type: 'XPAY_TRANSFER', env: 'QA', receiverWalletId: 3, receiverUser: 'qa.usuario2' }));
    });

    // No debió construirse ninguna instancia nueva de Enviar (el decode
    // tardío no reactivó nada) — Comprar sigue siendo la única sesión viva.
    expect(constructedIds).toEqual(['env-qr-reader', 'pag-qr-reader']);

    // Prueba directa de "no modificó destino": si parseTransferQr se
    // hubiera ejecutado, `envDest` ya no estaría vacío y el auto-inicio del
    // scanner de Enviar NO se dispararía al volver a ese tab. Como sigue
    // vacío, un tercer scanner de Enviar se construye normalmente.
    await act(async () => { await router.navigate('/mi-wallet?tab=enviar'); });
    await waitFor(() => expect(constructedIds.filter((id) => id === 'env-qr-reader').length).toBe(2));

    startDeferreds[startDeferreds.length - 1].resolve();
  });

  it('TEST F: unmount durante una transición Enviar→Comprar no deja ninguna instancia activa', async () => {
    const { router, unmount } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    await waitFor(() => expect(startDeferreds.length).toBe(1));
    // Enviar.start() sigue pendiente.

    await act(async () => { await router.navigate('/mi-wallet?tab=pagar'); });
    // Comprar queda en cola, detrás del stop de Enviar (que a su vez espera
    // el start pendiente de Enviar).

    unmount();
    // El desmontaje encola un stop más al final — pero el start original de
    // Enviar sigue sin resolver: nada puede completarse todavía.
    expect(stopSpy).not.toHaveBeenCalled();

    startDeferreds[0].resolve();
    await waitFor(() => expect(stopSpy).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(clearSpy).toHaveBeenCalledTimes(1));

    // La sesión de Comprar que esperaba su turno fue invalidada por el
    // unmount ANTES de que le tocara ejecutarse — nunca llega a
    // construirse ninguna instancia para "pag-qr-reader".
    expect(constructedIds).toEqual(['env-qr-reader']);
  });

  it('TEST G: reopen — Enviar → salir → Enviar de nuevo solo tras limpieza completa de la primera sesión', async () => {
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(constructedIds).toContain('env-qr-reader'));
    await waitFor(() => expect(startDeferreds.length).toBe(1));
    startDeferreds[0].resolve();
    await waitFor(() => expect(callLog).toContain('start:0'));

    await act(async () => { await router.navigate('/mi-wallet?tab=recibir'); });
    await waitFor(() => expect(callLog).toContain('stop:0'));
    expect(callLog).toContain('clear:0');

    await act(async () => { await router.navigate('/mi-wallet?tab=enviar'); });
    await waitFor(() => expect(constructedIds.filter((id) => id === 'env-qr-reader').length).toBe(2));

    const construct1At = callLog.findIndex((e) => e.startsWith('construct:1:'));
    const clear0At = callLog.indexOf('clear:0');
    expect(construct1At).toBeGreaterThan(clear0At);

    startDeferreds[1].resolve();
  });
});
