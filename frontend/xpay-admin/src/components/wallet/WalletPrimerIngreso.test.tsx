import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { WalletPrimerIngreso } from './WalletPrimerIngreso.tsx';
import { UserWalletPage } from '../../pages/UserWalletPage.tsx';
import type { EstadoPin } from '../../utils/pinTransaccional.ts';
import { ApiError } from '../../api/client.ts';
import { WalletShell } from './WalletShell.tsx';

const mockGet = vi.fn();
const mockPost = vi.fn();
const onAction = vi.fn();
const onNavigate = vi.fn();
const onLogout = vi.fn();
const onProfile = vi.fn();
const mockUser = { idUsuario: 5, idPersona: 5, usuario: 'piloto.prueba', roles: [], token: 'synthetic-test-token', requiereCambioClave: false };
vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) };
});
vi.mock('../../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual('../../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: mockUser, logout: vi.fn() }) };
});
vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn(() => Promise.resolve('data:image/mock;text,x')) } }));
vi.mock('html5-qrcode', () => ({ Html5Qrcode: class {
  start() { return new Promise(() => {}); }
  stop() { return Promise.resolve(); }
  clear() {}
} }));

const PIN_LISTO: EstadoPin = { configurado: true, bloqueado: false, restablecido: false, intentosRestantes: 5, longitudRequerida: 6, requiereActualizacion: false };
let pin: EstadoPin;
let llave: object | null;
let errorPin: boolean;
let estadoKyc: string;

function mostrar(conOperacion = false, conShell = false, perfil = false, tab = 'enviar') {
  const child = conOperacion ? <UserWalletPage /> : <h2>{perfil ? 'PERFIL_LISTO' : 'WALLET_LISTA'}</h2>;
  return render(<RouterProvider router={createMemoryRouter(
    [{ path: '/mi-wallet', element: <WalletPrimerIngreso perfil={perfil} envolver={conShell
      ? (contenido, preparando) => <WalletShell preparando={preparando} onAction={onAction} onNavigate={onNavigate} onLogout={onLogout} onOpenProfileDetail={onProfile}>{contenido}</WalletShell>
      : undefined}>{child}</WalletPrimerIngreso> }],
    { initialEntries: [`/mi-wallet?tab=${tab}`] },
  )} />);
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  pin = { ...PIN_LISTO, configurado: false };
  llave = null;
  errorPin = false;
  estadoKyc = 'APROBADO';
  onAction.mockReset(); onNavigate.mockReset(); onLogout.mockReset(); onProfile.mockReset();
  mockGet.mockReset().mockImplementation((path: string) => {
    if (path === '/api/usuarios/preparacion-wallet') return Promise.reject(new ApiError('Recorrido no disponible', 404));
    if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 5, nombreWallet: 'Wallet de prueba', estado: 'ACTIVA' } });
    if (path === '/api/pin/estado') return errorPin ? Promise.reject(new ApiError('Estado no disponible', 404)) : Promise.resolve({ success: true, data: pin });
    if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: llave });
    if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
    if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc } });
    if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: { idWallet: 2, nombreWallet: 'Wallet de prueba', estado: 'ACTIVA', saldoDisponible: 10000, saldoRetenido: 0, movimientos: [] } });
    if (path === '/api/wallets/destinatario/3') return Promise.resolve({ success: true, data: { idWallet: 3, usuario: 'destino.prueba', nombreVisible: 'Destino P.' } });
    if (path.startsWith('/api/qr/resolver')) return Promise.resolve({ success: true, data: { codigoQr: 'QR-PRUEBA-001', idComercio: 3, nombreComercio: 'Comercio de prueba', idTienda: 3, nombreTienda: 'Tienda de prueba' } });
    return Promise.reject(new Error('Consulta no prevista en la prueba.'));
  });
  mockPost.mockReset().mockImplementation((path: string) => {
    if (path === '/api/pin' || path === '/api/pin/cambiar') {
      pin = { ...PIN_LISTO };
      return Promise.resolve({ success: true, message: 'PIN guardado.' });
    }
    if (path === '/api/wallets/transferencia') return Promise.resolve({ success: true, data: { idTransaccion: 1, idWalletOrigen: 2, idWalletDestino: 3, valor: 500 } });
    if (path === '/api/qr/pagar') return Promise.resolve({ success: true, data: { idVentaQr: 4, idComercio: 3, idTienda: 3, valor: 100 } });
    return Promise.reject(new Error('Operación no prevista en la prueba.'));
  });
});

async function crearPin(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Contraseña de ingreso'), 'Clave-sintetica-1');
  await user.type(screen.getByLabelText('Nuevo PIN (6 dígitos)'), '025801');
  await user.type(screen.getByLabelText('Confirmar PIN'), '025801');
  await user.click(screen.getByRole('button', { name: 'Crear PIN' }));
  await waitFor(() => expect(screen.queryByLabelText('Nuevo PIN (6 dígitos)')).not.toBeInTheDocument());
}

describe('WalletPrimerIngreso — PIN sin llave obligatoria', () => {
  it('antes de conocer el PIN no monta Wallet ni permite saltar la preparación', async () => {
    mockGet.mockImplementation((path: string) => path === '/api/wallets/mi-wallet'
      ? Promise.resolve({ success: true, data: { idWallet: 2 } }) : new Promise(() => {}));
    mostrar();
    expect(await screen.findByRole('status')).toHaveTextContent('Consultando la preparación');
    expect(screen.queryByText('WALLET_LISTA')).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('crea PIN6 y entra a Wallet sin pedir, consultar ni registrar llave y sin guardar secretos', async () => {
    const user = userEvent.setup(); mostrar(); await crearPin(user);
    expect(await screen.findByText('WALLET_LISTA')).toBeInTheDocument();
    expect(screen.queryByLabelText('Valor de la llave')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Registrar más tarde' })).not.toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
    expect(mockGet).not.toHaveBeenCalledWith('/api/usuarios/preparacion-wallet');
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/pin', { claveActual: 'Clave-sintetica-1', pin: '025801', confirmacionPin: '025801' });
    expect(localStorage.length).toBe(0); expect(sessionStorage.length).toBe(0);
  });
  it.each(['25801', '0258014'])('actualiza PIN anterior %s y sigue directamente a Wallet', async anterior => {
    pin = { ...PIN_LISTO, requiereActualizacion: true }; const user = userEvent.setup(); mostrar();
    await user.type(await screen.findByLabelText('PIN actual (5, 6 o 7 dígitos)'), anterior);
    await user.type(screen.getByLabelText('PIN nuevo (6 dígitos)'), '369147');
    await user.type(screen.getByLabelText('Confirmar PIN'), '369147'); await user.click(screen.getByRole('button', { name: 'Cambiar PIN' }));
    expect(await screen.findByText('WALLET_LISTA')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/pin/cambiar', { pinActual: anterior, pinNuevo: '369147', confirmacionPinNuevo: '369147' });
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
  });
  it('con PIN6 existente entra sin recordatorio ni formulario de llave', async () => {
    pin = { ...PIN_LISTO }; mostrar(); expect(await screen.findByText('WALLET_LISTA')).toBeInTheDocument();
    expect(screen.queryByRole('complementary', { name: 'Llave Bre-B para retiros' })).not.toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave'); expect(mockPost).not.toHaveBeenCalled();
  });
  it('con identidad pendiente conserva el control KYC de las operaciones, sin registrar llave', async () => {
    pin = { ...PIN_LISTO }; estadoKyc = 'NO_INICIADO'; mostrar(true);
    expect(await screen.findByRole('button', { name: 'Iniciar verificación' })).toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave'); expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mis-retiros');
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('durante la preparación oculta acciones monetarias y QR y conserva Perfil y salir', async () => {
    const user = userEvent.setup(); mostrar(false, true); await screen.findByLabelText('Contraseña de ingreso');
    expect(screen.queryByRole('group', { name: 'Acciones principales' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'QR' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Perfil' })); await user.click(screen.getByRole('button', { name: 'Mi perfil' }));
    expect(onProfile).toHaveBeenCalledOnce(); await user.click(screen.getByRole('button', { name: 'Abrir perfil' }));
    await user.click(screen.getByRole('button', { name: 'Cerrar sesión' })); expect(onLogout).toHaveBeenCalledOnce(); expect(onAction).not.toHaveBeenCalled();
  });
  it('Perfil sigue disponible con PIN pendiente', async () => {
    mostrar(false, true, true); expect(await screen.findByText('PERFIL_LISTO')).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Acciones principales' })).not.toBeInTheDocument();
  });
  it('no avanza si el PIN confirmado quedó bloqueado', async () => {
    mockPost.mockImplementationOnce(() => { pin = { ...PIN_LISTO, bloqueado: true, intentosRestantes: 0 }; return Promise.resolve({ success: true }); });
    const user = userEvent.setup(); mostrar(); await crearPin(user);
    expect(await screen.findByText(/Tu PIN está bloqueado. Solicita/)).toBeInTheDocument();
    expect(screen.queryByText('WALLET_LISTA')).not.toBeInTheDocument(); expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
  });
  it('sin llave permite enviar entre Wallets con PIN6, sin resolver ni registrar una llave', async () => {
    const user = userEvent.setup(); mostrar(true); await crearPin(user);
    await user.click(await screen.findByText('Otras opciones')); await user.click(screen.getByText('Ingresar destino manualmente →'));
    await user.type(screen.getByLabelText('ID de wallet destino'), '3'); await user.click(screen.getByRole('button', { name: 'Confirmar destino' }));
    await screen.findByText('Destino verificado'); await user.type(screen.getByLabelText(/Valor a transferir/), '500');
    await user.type(screen.getByLabelText(/PIN transaccional/), '025801'); await user.click(screen.getByRole('button', { name: 'Enviar dinero' }));
    expect(await screen.findByRole('dialog', { name: 'Transferencia exitosa' })).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledWith('/api/wallets/transferencia', expect.objectContaining({ pin: '025801' }), expect.anything());
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave/resolver', expect.anything());
  });
  it('sin llave permite pagar QR con PIN6', async () => {
    const user = userEvent.setup(); mostrar(true, false, false, 'pagar'); await crearPin(user);
    await user.click(await screen.findByText('Otras opciones')); await user.type(screen.getByLabelText('Pegar código QR o contenido JSON'), 'QR-PRUEBA-001');
    await user.click(screen.getByRole('button', { name: 'Usar código pegado' })); await screen.findByText('Comercio de prueba');
    await user.click(screen.getByRole('button', { name: '💳 Pagar con Wallet' })); await user.type(screen.getByLabelText(/Valor a pagar/), '100');
    await user.type(screen.getByLabelText(/PIN transaccional/), '025801'); await user.click(screen.getByRole('button', { name: 'Pagar QR con Wallet' }));
    expect(await screen.findByText('✓ Pago realizado')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledWith('/api/qr/pagar', expect.objectContaining({ pin: '025801' }), expect.anything());
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave/resolver', expect.anything());
  });
  it('404 de PIN no permite pasar la puerta y puede consultarse de nuevo', async () => {
    errorPin = true; const user = userEvent.setup(); mostrar(); expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText('WALLET_LISTA')).not.toBeInTheDocument(); errorPin = false;
    await user.click(screen.getByRole('button', { name: 'Actualizar preparación' })); expect(await screen.findByLabelText('Contraseña de ingreso')).toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('señal 428 vuelve a consultar y pide actualización sin repetir operación', async () => {
    pin = { ...PIN_LISTO }; mostrar(); await screen.findByText('WALLET_LISTA'); pin = { ...PIN_LISTO, requiereActualizacion: true };
    act(() => { window.dispatchEvent(new Event('xpay:pin-actualizacion-requerida')); });
    expect(await screen.findByLabelText('PIN actual (5, 6 o 7 dígitos)')).toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
});
