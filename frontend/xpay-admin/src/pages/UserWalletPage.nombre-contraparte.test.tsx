import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import type { AuthUser } from '../auth/AuthContext.tsx';
import { Layout } from '../components/Layout.tsx';
import { UserWalletPage } from './UserWalletPage.tsx';

// Integración offline del Layout, proveedor, campana y ambas listas reales.
// Todos los actores/nombres son ficticios. No hay red ni operaciones de dinero.
const state = vi.hoisted(() => ({ user: null as AuthUser | null, listeners: new Set<() => void>() }));
const mockGet = vi.fn();
const mockPost = vi.fn();
vi.mock('../api/client.ts', async () => ({
  ...await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts'),
  get: (...args: unknown[]) => mockGet(...args),
  post: (...args: unknown[]) => mockPost(...args),
}));
vi.mock('../auth/AuthContext.tsx', async () => {
  const { useSyncExternalStore } = await import('react');
  return {
    ...await vi.importActual<typeof import('../auth/AuthContext.tsx')>('../auth/AuthContext.tsx'),
    useAuth: () => ({ user: useSyncExternalStore(listener => {
      state.listeners.add(listener);
      return () => { state.listeners.delete(listener); };
    }, () => state.user), logout: vi.fn() }),
  };
});
vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn() } }));
vi.mock('html5-qrcode', () => ({ Html5Qrcode: class {} }));

interface MovimientoFixture {
  idMovimiento: number;
  fecha: string;
  tipoMovimiento: string;
  naturaleza: string;
  valor: number;
  saldoDespues: number;
  descripcion: string | null;
  referenciaTipo: string | null;
  referenciaId: number | null;
  nombreContraparte?: string | null;
}
interface CuentaFixture {
  idWallet: number;
  nombreWallet: string;
  estado: string;
  saldoDisponible: number;
  saldoRetenido: number;
  movimientos: MovimientoFixture[];
}
type CuentaResponse = { success: boolean; data: CuentaFixture };

function actor(id: number): AuthUser {
  return { idUsuario: id, idPersona: id + 100, usuario: `actor.ficticio.${id}`, estado: 'ACTIVO',
    roles: ['USUARIO_FINAL'], token: 'synthetic-local-only', requiereCambioClave: false };
}
function mov(id: number, overrides: Partial<MovimientoFixture> = {}): MovimientoFixture {
  return { idMovimiento: id, fecha: '2026-11-20T14:30:00Z', tipoMovimiento: 'TRANSFERENCIA_SALIDA',
    naturaleza: 'D', valor: 1234, saldoDespues: 8000, descripcion: 'Alias guardado no autorizado',
    referenciaTipo: 'wallets', referenciaId: 41, ...overrides };
}
function cuenta(idWallet: number, movimientos: MovimientoFixture[]): CuentaFixture {
  return { idWallet, nombreWallet: 'Wallet ficticia', estado: 'ACTIVA', saldoDisponible: 8000, saldoRetenido: 0, movimientos };
}
function response(data: CuentaFixture): CuentaResponse { return { success: true, data }; }
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(res => { resolve = res; });
  return { promise, resolve };
}

const cuentas = new Map<number, CuentaFixture>();
const pendientes = new Map<number, Promise<CuentaResponse>>();
const lecturasCuenta: number[] = [];
function seedCursor(idWallet: number) {
  localStorage.setItem(`xpay.wallet.${idWallet}.notifications.lastSeenMovementId`, '0');
}
function apiGet(path: string) {
  const idActor = state.user!.idUsuario;
  const actual = cuentas.get(idActor)!;
  if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true,
    data: { idWallet: actual.idWallet, idPersona: state.user!.idPersona, nombreWallet: actual.nombreWallet, estado: 'ACTIVA' } });
  if (path === '/api/pin/estado') return Promise.resolve({ success: true,
    data: { configurado: true, bloqueado: false, restablecido: false, intentosRestantes: 3, longitudRequerida: 6, requiereActualizacion: false } });
  if (path === '/api/reportes/mi-estado-cuenta') {
    lecturasCuenta.push(idActor);
    return pendientes.get(idActor) ?? Promise.resolve(response(actual));
  }
  if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
  if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
  if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
  return Promise.reject(new Error(`GET inesperado: ${path}`));
}
function harness(tab = 'saldo') {
  const router = createMemoryRouter([{ element: <Layout />, children: [
    { path: '/mi-wallet', element: <UserWalletPage /> },
    { path: '/mi-wallet/otra', element: <h2>Otra vista ficticia</h2> },
  ] }], { initialEntries: [`/mi-wallet?tab=${tab}`] });
  const element = <RouterProvider router={router} />;
  return { ...render(element), router, element };
}
function panel() { return within(screen.getByRole('dialog', { name: 'Notificaciones' })); }

beforeEach(() => {
  vi.stubEnv('VITE_XPAY_ENV', 'PROD');
  state.user = actor(101);
  cuentas.clear(); pendientes.clear(); lecturasCuenta.length = 0;
  cuentas.set(101, cuenta(21, []));
  localStorage.clear();
  mockGet.mockReset().mockImplementation(apiGet);
  mockPost.mockReset();
});
afterEach(() => {
  const permitidos = new Set(['/api/wallets/mi-wallet', '/api/pin/estado', '/api/reportes/mi-estado-cuenta',
    '/api/kyc/mi-estado', '/api/breb/mi-llave', '/api/breb/mis-retiros']);
  expect(mockGet.mock.calls.every(([path]) => permitidos.has(path))).toBe(true);
  expect(mockPost).not.toHaveBeenCalled();
  vi.useRealTimers(); vi.unstubAllEnvs();
});

describe('Nombre de la persona en movimientos', () => {
  it.each(['saldo', 'movimientos'])('%s: usa el nombre de la API en envíos/recepciones y conserva Wallet # si falta', async tab => {
    cuentas.set(101, cuenta(21, [
      mov(7, { nombreContraparte: '  Persona Ficticia Laurel  ' }),
      mov(6, { tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', referenciaId: 42, nombreContraparte: 'Persona Ficticia Cedro' }),
      mov(5, { referenciaId: 3 }),
      mov(4, { tipoMovimiento: 'TRANSFERENCIA_ENTRADA', referenciaId: 2, nombreContraparte: null }),
      mov(3, { tipoMovimiento: 'TRANSFERENCIA_ENTRADA', referenciaId: 43, nombreContraparte: undefined }),
      mov(2, { referenciaId: 44, nombreContraparte: ' \t ' }),
      mov(1, { tipoMovimiento: 'TRANSFERENCIA_ENTRADA', referenciaId: 45, nombreContraparte: '' }),
    ]));
    const { container } = harness(tab);
    expect(await screen.findByText('Env. a Persona Ficticia Laurel')).toBeInTheDocument();
    expect(screen.getByText('Rec de Persona Ficticia Cedro')).toBeInTheDocument();
    expect(screen.getByText('Enviado a Wallet #3')).toBeInTheDocument();
    expect(screen.getByText('Recibido de Wallet #2')).toBeInTheDocument();
    expect(screen.getByText('Recibido de Wallet #43')).toBeInTheDocument();
    expect(screen.getByText('Enviado a Wallet #44')).toBeInTheDocument();
    expect(screen.getByText('Recibido de Wallet #45')).toBeInTheDocument();
    expect(container.querySelectorAll('.wallet-movement-item')).toHaveLength(7);
    const envio = screen.getByText('Env. a Persona Ficticia Laurel').closest('li')!;
    const recibo = screen.getByText('Rec de Persona Ficticia Cedro').closest('li')!;
    expect(within(envio).getByText('−$ 1.234')).toBeInTheDocument();
    expect(within(recibo).getByText('+$ 1.234')).toBeInTheDocument();
    expect(screen.queryByText(/qa\.usuario|Alias guardado no autorizado/)).not.toBeInTheDocument();
    expect(mockGet.mock.calls.filter(([path]) => path === '/api/reportes/mi-estado-cuenta')).toHaveLength(1);
  });

  it.each(['saldo', 'movimientos'])('%s: no aplica nombres a QR, Bre-B, otra referencia ni IDs inválidos', async tab => {
    cuentas.set(101, cuenta(21, [
      mov(8, { referenciaTipo: 'retiros', descripcion: 'Otra transferencia conservada', nombreContraparte: 'Persona Ficticia Ignorada' }),
      mov(7, { referenciaId: 0, descripcion: 'Referencia cero conservada', nombreContraparte: 'Persona Ficticia Ignorada' }),
      mov(6, { referenciaId: -7, descripcion: 'Referencia negativa conservada', nombreContraparte: 'Persona Ficticia Ignorada' }),
      mov(5, { referenciaId: null, descripcion: 'Referencia ausente conservada', nombreContraparte: 'Persona Ficticia Ignorada' }),
      mov(4, { tipoMovimiento: 'PAGO_QR', descripcion: 'QR guardado', nombreContraparte: 'Persona Ficticia Ignorada' }),
      mov(3, { tipoMovimiento: 'BREB_RETIRO', descripcion: 'Retiro Bre-B conservado', nombreContraparte: 'Persona Ficticia Ignorada' }),
      mov(2, { tipoMovimiento: 'RECARGA', descripcion: 'Recarga conservada', nombreContraparte: 'Persona Ficticia Ignorada' }),
      mov(1, { referenciaId: 46, nombreContraparte: 123 as unknown as string }),
    ]));
    harness(tab);
    expect(await screen.findByText('Otra transferencia conservada')).toBeInTheDocument();
    for (const texto of ['Referencia cero conservada', 'Referencia negativa conservada', 'Referencia ausente conservada',
      'Pago a Comercio Demo XPAY QA', 'Retiro Bre-B conservado', 'Recarga conservada', 'Enviado a Wallet #46']) {
      expect(screen.getByText(texto)).toBeInTheDocument();
    }
    expect(screen.queryByText(/Persona Ficticia Ignorada/)).not.toBeInTheDocument();
  });

  it.each(['saldo', 'movimientos'])('%s: renderiza nombres largos y caracteres HTML como texto', async tab => {
    const nombre = 'Persona Ficticia <img src=x onerror="alert(1)"> & ' + 'ApellidoFicticio'.repeat(25);
    cuentas.set(101, cuenta(21, [mov(1, { nombreContraparte: nombre })]));
    const { container } = harness(tab);
    const descripcion = await screen.findByText(`Env. a ${nombre}`);
    expect(descripcion.textContent).toBe(`Env. a ${nombre}`);
    expect(descripcion.querySelector('img')).toBeNull();
    expect(container.querySelector('[onerror]')).toBeNull();
  });

  it('polling y recarga reemplazan los nombres del lote anterior, incluso cuando desaparecen', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const usuario = userEvent.setup({ delay: null });
    seedCursor(21);
    const recibido = mov(1, { tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', nombreContraparte: 'Persona Ficticia Inicial' });
    cuentas.set(101, cuenta(21, [recibido]));
    const first = harness();
    await screen.findByText('Rec de Persona Ficticia Inicial');
    await usuario.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(panel().getByText('Persona Ficticia Inicial', { selector: 'strong' })).toBeInTheDocument();

    cuentas.set(101, cuenta(21, [{ ...recibido, nombreContraparte: undefined }]));
    await act(async () => { await vi.advanceTimersByTimeAsync(7000); });
    expect(screen.getByText('Recibido de Wallet #41')).toBeInTheDocument();
    expect(panel().getByText('Wallet #41', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.queryByText(/Persona Ficticia Inicial/)).not.toBeInTheDocument();

    cuentas.set(101, cuenta(21, [{ ...recibido, nombreContraparte: 'Persona Ficticia Actualizada' }]));
    await act(async () => { await vi.advanceTimersByTimeAsync(7000); });
    expect(screen.getByText('Rec de Persona Ficticia Actualizada')).toBeInTheDocument();
    expect(panel().getByText('Persona Ficticia Actualizada', { selector: 'strong' })).toBeInTheDocument();

    first.unmount();
    cuentas.set(101, cuenta(21, [{ ...recibido, nombreContraparte: null }]));
    harness();
    await screen.findByText('Recibido de Wallet #41');
    await usuario.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(panel().getByText('Wallet #41', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.queryByText(/Persona Ficticia (Inicial|Actualizada)/)).not.toBeInTheDocument();
    expect(localStorage.getItem('xpay.wallet.21.notifications.lastSeenMovementId')).toBe('0');
  });

  it('el cambio de actor limpia nombres/notificaciones antes de cargar y descarta el poll anterior tardío', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const usuario = userEvent.setup({ delay: null });
    seedCursor(21); seedCursor(22);
    const recibido = mov(1, { tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', nombreContraparte: 'Persona Ficticia Anterior' });
    cuentas.set(101, cuenta(21, [recibido]));
    const vista = harness();
    await screen.findByText('Rec de Persona Ficticia Anterior');
    await usuario.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(panel().getByText('Persona Ficticia Anterior', { selector: 'strong' })).toBeInTheDocument();

    const viejo = deferred<CuentaResponse>();
    pendientes.set(101, viejo.promise);
    await act(async () => { await vi.advanceTimersByTimeAsync(7000); });
    const nuevo = deferred<CuentaResponse>();
    pendientes.set(102, nuevo.promise);
    cuentas.set(102, cuenta(22, [{ ...recibido, nombreContraparte: undefined }]));
    await act(async () => {
      state.user = actor(102);
      state.listeners.forEach(listener => listener());
    });
    await waitFor(() => expect(lecturasCuenta).toContain(102));
    expect(screen.queryByText(/Persona Ficticia Anterior/)).not.toBeInTheDocument();
    expect(document.querySelector('.wh-notif-dot')).toBeNull();
    await usuario.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(panel().getByText('No tienes notificaciones nuevas.')).toBeInTheDocument();

    await act(async () => { viejo.resolve(response(cuenta(21, [recibido]))); });
    expect(screen.queryByText(/Persona Ficticia Anterior/)).not.toBeInTheDocument();
    expect(panel().getByText('No tienes notificaciones nuevas.')).toBeInTheDocument();
    await act(async () => { nuevo.resolve(response(cuentas.get(102)!)); });
    expect(screen.getByText('Recibido de Wallet #41')).toBeInTheDocument();
    expect(panel().getByText('Wallet #41', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.queryByText(/Persona Ficticia Anterior/)).not.toBeInTheDocument();
  });

  it('una lectura inicial tardía no publica nombres después de salir de la página', async () => {
    const usuario = userEvent.setup();
    seedCursor(21);
    const tardia = deferred<CuentaResponse>();
    pendientes.set(101, tardia.promise);
    const vista = harness();
    await waitFor(() => expect(lecturasCuenta).toHaveLength(1));
    await act(async () => { await vista.router.navigate('/mi-wallet/otra'); });
    await act(async () => { tardia.resolve(response(cuenta(21, [mov(1, {
      tipoMovimiento: 'TRANSFERENCIA_ENTRADA', naturaleza: 'C', nombreContraparte: 'Persona Ficticia Tardía',
    })]))); });
    expect(screen.getByRole('heading', { name: 'Otra vista ficticia' })).toBeInTheDocument();
    await usuario.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(panel().getByText('No tienes notificaciones nuevas.')).toBeInTheDocument();
    expect(screen.queryByText(/Persona Ficticia Tardía/)).not.toBeInTheDocument();
  });
});
