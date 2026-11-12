// XPAY-426 (Block B) — confirmación explícita y persistente para el EMISOR
// tras una transferencia interna exitosa. Reemplaza el mensaje inline
// QA-WALLET-7A (desaparecía solo a los ~1800ms y navegaba automáticamente a
// Movimientos — insuficiente en móvil, auditado en XPAY-421) por un modal
// que solo se cierra con el botón "Cerrar" del usuario. CERO red real:
// api/client.ts está completamente mockeado.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
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

vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn(() => Promise.resolve('data:image/mock;text,x')) } }));
// Nunca se interactúa con la cámara real en estos tests (se usa el flujo de
// "pegar QR" / destino manual) — el mock nunca resuelve start(), consistente
// con el resto de la suite para archivos que no ejercitan el scanner.
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

// GET /api/wallets/destinatario/{id}: por defecto resuelve el titular; cada
// test puede sustituirlo (pendiente, 404...).
const titularPorDefecto = (id: number) =>
  Promise.resolve({ success: true, data: { idWallet: id, usuario: 'qa.usuario2', nombreVisible: 'Usuario D.' } });
let destinatarioGet: (id: number) => Promise<unknown> = titularPorDefecto;

function apiRoutedGet(path: string) {
  const destinatario = /^\/api\/wallets\/destinatario\/(\d+)$/.exec(path);
  if (destinatario) return destinatarioGet(Number(destinatario[1]));
  if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 3, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA' } });
  if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: CUENTA_BASE });
  if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
  if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
  if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
  return Promise.reject(new Error(`unmocked GET ${path}`));
}

function renderWalletAt(initialTab = 'enviar') {
  mockGet.mockImplementation(apiRoutedGet);
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
  destinatarioGet = titularPorDefecto;
});

// XPAY-426A — red de seguridad global: los tests D/E activan fake timers
// SOLO después de que el modal ya está visible (evita el conflicto conocido
// entre userEvent, que depende de timers reales para sus delays internos, y
// fake timers). Este afterEach garantiza que, incluso si una aserción de
// esos tests falla antes de su propio vi.useRealTimers(), el siguiente test
// del archivo arranca con timers reales — vi.useRealTimers() sobre timers ya
// reales es un no-op inofensivo.
afterEach(() => {
  vi.useRealTimers();
});

// Llega hasta el formulario de envío (destino verificado por el backend)
// usando destino MANUAL — no requiere cámara ni QR real.
async function llegarAFormularioManual(user: ReturnType<typeof userEvent.setup>, destId = 3) {
  await escribirDestinoManual(user, destId);
  await screen.findByText('Destino verificado');
}

async function escribirDestinoManual(user: ReturnType<typeof userEvent.setup>, destId = 3) {
  await user.click(screen.getByText('Otras opciones'));
  await user.click(screen.getByText('Ingresar destino manualmente →'));
  await user.type(screen.getByLabelText('ID de wallet destino'), String(destId));
  await user.click(screen.getByRole('button', { name: 'Confirmar destino' }));
}

async function pegarQr(user: ReturnType<typeof userEvent.setup>, contenido: object) {
  await user.click(screen.getByText('Otras opciones'));
  // fireEvent.change (no user.type): el JSON contiene "{"/"}" que
  // userEvent.type interpretaría como sintaxis de teclas especiales.
  fireEvent.change(screen.getByPlaceholderText(/XPAY_TRANSFER/), { target: { value: JSON.stringify(contenido) } });
  await user.click(screen.getByRole('button', { name: 'Usar QR pegado' }));
}

async function completarYEnviar(user: ReturnType<typeof userEvent.setup>, valor = '500') {
  await user.type(screen.getByLabelText('Valor a transferir (COP ficticio)'), valor);
  await user.type(screen.getByLabelText(/PIN transaccional/), '1234567');
  await user.click(screen.getByRole('button', { name: 'Enviar dinero' }));
}

describe('UserWalletPage — Enviar: modal de transferencia exitosa (XPAY-426 Block B)', () => {
  it('A: respuesta success=true muestra el modal "Transferencia exitosa"', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user);
    await completarYEnviar(user);

    await waitFor(() => expect(screen.getByText('Transferencia exitosa')).toBeInTheDocument());
    expect(screen.getByRole('dialog', { name: 'Transferencia exitosa' })).toBeInTheDocument();
  });

  it('B: el modal muestra el monto correcto formateado en COP', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user);
    await completarYEnviar(user, '500');

    await waitFor(() => expect(screen.getByText('$ 500')).toBeInTheDocument());
  });

  it('C: destino manual — el modal muestra el titular verificado por el backend, no el ID técnico', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });

    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());
    await llegarAFormularioManual(user, 3);
    await completarYEnviar(user, '500');
    await waitFor(() => expect(screen.getByText('Transferencia exitosa')).toBeInTheDocument());
    expect(screen.getByText('qa.usuario2 (Usuario D.)', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.queryByText('Wallet #3', { selector: 'strong' })).not.toBeInTheDocument();
  });

  it('C2: destino por QR — el modal usa el titular verificado por el backend', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 700 } });
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    // Flujo "pegar contenido del QR" — misma ruta de parseTransferQr que usa
    // el scanner real, sin depender de cámara.
    await pegarQr(user, { type: 'XPAY_TRANSFER', env: 'QA', receiverWalletId: 3, receiverUser: 'qa.usuario2', amount: 700 });
    await screen.findByText('Destino verificado');

    await user.type(screen.getByLabelText(/PIN transaccional/), '1234567');
    await user.click(screen.getByRole('button', { name: 'Enviar dinero' }));

    await waitFor(() => expect(screen.getByText('Transferencia exitosa')).toBeInTheDocument());
    expect(screen.getByText('qa.usuario2 (Usuario D.)', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.queryByText('Wallet #3', { selector: 'strong' })).not.toBeInTheDocument();
  });

  // D y E (XPAY-426A) — usan fake timers, pero SOLO desde después de que el
  // modal ya está visible: toda la interacción previa (llegarAFormularioManual,
  // completarYEnviar, el primer waitFor) corre con timers reales, exactamente
  // como en el resto de la suite — userEvent depende internamente de timers
  // reales para sus delays entre eventos, y mezclarlos desde el inicio del
  // test con fake timers es una fuente conocida de tests colgados/frágiles.
  // Una vez visible el modal ya no hay más interacción de userEvent
  // pendiente, así que activar el reloj simulado en ese punto es seguro y
  // permite demostrar la ausencia de timeout sin esperar tiempo real.
  it('D: el modal NO desaparece al avanzar el reloj simulado por encima del antiguo timeout (sin auto-cierre)', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user);
    await completarYEnviar(user);
    await waitFor(() => expect(screen.getByText('Transferencia exitosa')).toBeInTheDocument());

    // Solo se fake-ifican setTimeout/clearTimeout — lo mínimo necesario para
    // esta verificación; no toca Date/requestAnimationFrame/microtareas.
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    await vi.advanceTimersByTimeAsync(5000); // muy por encima del antiguo 1800ms
    expect(screen.getByText('Transferencia exitosa')).toBeInTheDocument();
    vi.useRealTimers();
  });

  it('E: NO cambia automáticamente al tab Movimientos tras el éxito, ni siquiera tras avanzar el reloj simulado', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user);
    await completarYEnviar(user);
    await waitFor(() => expect(screen.getByText('Transferencia exitosa')).toBeInTheDocument());

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    await vi.advanceTimersByTimeAsync(5000);
    // La URL nunca debió cambiar de tab por sí sola — seguimos en "enviar",
    // con el modal encima.
    expect(router.state.location.search).toContain('tab=enviar');
    expect(screen.queryByText(/^Movimientos \(/)).not.toBeInTheDocument();
    vi.useRealTimers();
  });

  it('F: al pulsar "Cerrar" el modal desaparece y vuelve a Mi Wallet (tab saldo)', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    const { router } = renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user);
    await completarYEnviar(user);
    await waitFor(() => expect(screen.getByText('Transferencia exitosa')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Cerrar' }));

    expect(screen.queryByText('Transferencia exitosa')).not.toBeInTheDocument();
    // XPAY-428A — corrección: "saldo" es el tab por defecto, y setTab()
    // (contrato preexistente, no tocado por XPAY-426/426A) lo representa
    // con query string VACÍO — `if (t === 'saldo') setSearchParams({})` —
    // en vez de escribir `?tab=saldo` explícitamente. La aserción anterior
    // (`toContain('tab=saldo')`) asumía incorrectamente lo segundo; el
    // fallo de CI (XPAY-428) confirmó, vía el DOM adjunto, que la
    // navegación real a Mi Wallet/saldo sí ocurría correctamente.
    await waitFor(() => expect(router.state.location.search).toBe(''));
    // La vista de saldo (Mi Wallet) muestra el saldo ya refrescado por
    // loadCuenta() — mismo mecanismo existente, no uno nuevo.
    await waitFor(() => expect(screen.getByText('Disponible')).toBeInTheDocument());
  });

  it('G: respuesta success=false NO muestra el modal de éxito', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: false, message: 'Fondos insuficientes.' });
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user);
    await completarYEnviar(user);

    await waitFor(() => expect(screen.getByText('Fondos insuficientes.')).toBeInTheDocument());
    expect(screen.queryByText('Transferencia exitosa')).not.toBeInTheDocument();
  });

  it('G2: un error de red (excepción) tampoco muestra el modal de éxito', async () => {
    const user = userEvent.setup();
    mockPost.mockRejectedValue(new Error('Network error'));
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user);
    await completarYEnviar(user);

    await waitFor(() => expect(screen.getByText('Network error')).toBeInTheDocument());
    expect(screen.queryByText('Transferencia exitosa')).not.toBeInTheDocument();
  });

  it('H: una sola acción de envío produce una sola llamada al endpoint (protección de doble clic preservada)', async () => {
    const user = userEvent.setup();
    let resolvePost!: (value: unknown) => void;
    mockPost.mockReturnValue(new Promise((resolve) => { resolvePost = resolve; }));
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user);
    await user.type(screen.getByLabelText('Valor a transferir (COP ficticio)'), '500');
    await user.type(screen.getByLabelText(/PIN transaccional/), '1234567');

    const btn = screen.getByRole('button', { name: 'Enviar dinero' });
    await user.click(btn);
    // El botón queda deshabilitado ("Procesando...") mientras la promesa del
    // POST sigue pendiente — un segundo clic no debe producir una segunda
    // llamada (protección existente vía envBusy, no modificada aquí).
    expect(screen.getByRole('button', { name: 'Procesando...' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Procesando...' }));

    expect(mockPost).toHaveBeenCalledTimes(1);

    resolvePost({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    await waitFor(() => expect(screen.getByText('Transferencia exitosa')).toBeInTheDocument());
    expect(mockPost).toHaveBeenCalledTimes(1);
  });
});

// El destinatario lo resuelve el backend (GET /api/wallets/destinatario) antes
// de mostrar valor y PIN; el nombre que trae el QR nunca es autoridad, y la
// transferencia envía el usuario verificado como usuarioDestinoEsperado.
describe('UserWalletPage — Enviar: verificación del destinatario', () => {
  it('sin verificación del backend no se muestran valor ni PIN', async () => {
    const user = userEvent.setup();
    destinatarioGet = () => new Promise(() => { /* nunca responde */ });
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await escribirDestinoManual(user, 3);

    expect(await screen.findByText('Verificando destinatario…')).toBeInTheDocument();
    expect(screen.queryByLabelText('Valor a transferir (COP ficticio)')).toBeNull();
    expect(screen.queryByLabelText(/PIN transaccional/)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Enviar dinero' })).toBeNull();
  });

  it('destino no disponible (404): error y sin formulario', async () => {
    const user = userEvent.setup();
    destinatarioGet = () => Promise.reject(Object.assign(new Error('Wallet destino no disponible.'), { status: 404 }));
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await escribirDestinoManual(user, 99);

    expect(await screen.findByText('Wallet destino no disponible.')).toBeInTheDocument();
    expect(screen.queryByText('Destino verificado')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Enviar dinero' })).toBeNull();
  });

  it('QR cuyo usuario no es el titular real: se rechaza y no se puede enviar', async () => {
    const user = userEvent.setup();
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await pegarQr(user, { type: 'XPAY_TRANSFER', env: 'QA', receiverWalletId: 3, receiverUser: 'tienda.oficial' });

    expect(await screen.findByText(/El QR no coincide con el titular de la wallet/)).toBeInTheDocument();
    expect(screen.queryByText('Destino verificado')).toBeNull();
    expect(screen.queryByText('tienda.oficial', { exact: false })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Enviar dinero' })).toBeNull();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('la transferencia envía el usuario verificado como usuarioDestinoEsperado', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, message: 'Transferencia realizada exitosamente.', data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user, 3);
    await completarYEnviar(user, '500');

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith(
      '/api/wallets/transferencia',
      {
        idWalletDestino: 3, valor: 500,
        descripcion: 'Enviado a qa.usuario2 — Wallet #3',
        usuarioDestinoEsperado: 'qa.usuario2',
        pin: '1234567',
      },
      expect.objectContaining({ 'Idempotency-Key': expect.any(String) }),
    ));
  });

  it('409 del destinatario: no hay modal de éxito y el destino se descarta para verificarlo de nuevo', async () => {
    const user = userEvent.setup();
    mockPost.mockRejectedValue(Object.assign(
      new Error('El destinatario cambió o no coincide. Vuelve a confirmarlo.'), { status: 409 }));
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user, 3);
    await completarYEnviar(user, '500');

    expect(await screen.findByText('El destinatario cambió o no coincide. Vuelve a confirmarlo.')).toBeInTheDocument();
    expect(screen.queryByText('Destino verificado')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Enviar dinero' })).toBeNull();
    expect(screen.queryByText('Transferencia exitosa')).toBeNull();
    expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument();
  });

  it('cualquier 409, sea cual sea su texto, obliga a volver a verificar el destino', async () => {
    const user = userEvent.setup();
    mockPost.mockRejectedValue(Object.assign(new Error('Conflicto.'), { status: 409 }));
    renderWalletAt('enviar');
    await waitFor(() => expect(screen.getByText('Escanea el QR del receptor')).toBeInTheDocument());

    await llegarAFormularioManual(user, 3);
    await completarYEnviar(user, '500');
    expect(await screen.findByText('Conflicto.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Enviar dinero' })).toBeNull();

    // Al volver a fijar el destino se consulta otra vez al backend.
    const consultas = () => mockGet.mock.calls.filter(([p]) => String(p).startsWith('/api/wallets/destinatario/')).length;
    expect(consultas()).toBe(1);
    await llegarAFormularioManual(user, 3);
    expect(consultas()).toBe(2);
  });
});
