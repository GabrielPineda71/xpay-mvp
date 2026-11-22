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
  await screen.findByText('Tu PIN de 6 dígitos quedó guardado.');
}

describe('WalletPrimerIngreso', () => {
  it('respeta la llave OMITIDA guardada en el servidor al entrar y tras actualizar PIN, sin pedirla otra vez', async () => {
    const original = mockGet.getMockImplementation()!;
    mockGet.mockImplementation((path: string) => path === '/api/usuarios/preparacion-wallet' ? Promise.resolve({ success: true, data: {
      habilitado: true, esRegistroPublico: true, emailVerificado: true, estadoKyc: 'APROBADO', identidadVerificada: true,
      perfilCompleto: true, walletHabilitada: true, puedeActivar: false, siguientePaso: 'WALLET',
      llave: { opcion: 'OMITIDA' }, pin: { configurado: false, bloqueado: false, longitudRequerida: 6 },
      condiciones: { version: '1.0', fecha: '2026-11-21', url: 'https://app.xpaycol.com/legal/wallet/v1.0/condiciones.pdf', sha256: 'a'.repeat(64) },
    } }) : original(path));
    const user = userEvent.setup(); mostrar();
    await user.type(await screen.findByLabelText('Contraseña de ingreso'), 'Clave-sintetica-1');
    await user.type(screen.getByLabelText('Nuevo PIN (6 dígitos)'), '025801');
    await user.type(screen.getByLabelText('Confirmar PIN'), '025801');
    await user.click(screen.getByRole('button', { name: 'Crear PIN' }));
    expect(await screen.findByText('WALLET_LISTA')).toBeInTheDocument();
    expect(screen.queryByLabelText('Valor de la llave')).not.toBeInTheDocument();
    expect(screen.queryByRole('complementary', { name: 'Llave Bre-B para retiros' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Registrar más tarde' })).not.toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/pin', expect.anything());
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
  });
  it('primer ingreso sin KYC: guarda el PIN y guía a identidad sin consultar ni registrar llaves', async () => {
    estadoKyc = 'NO_INICIADO';
    const user = userEvent.setup();
    mostrar(true);
    await crearPin(user);
    expect(await screen.findByText(/Antes de registrar tu llave, completa la verificación de identidad en Mi Wallet/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Valor de la llave')).not.toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
    await user.click(screen.getByRole('button', { name: 'Continuar a mi Wallet' }));
    expect(await screen.findByRole('button', { name: 'Iniciar verificación' })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Verificar identidad en Mi Wallet' })).toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mis-retiros');
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave', expect.anything());
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave/resolver', expect.anything());
  });

  it('con PIN6 guardado e identidad pendiente muestra guía inline sin consultar la llave', async () => {
    pin = { ...PIN_LISTO };
    estadoKyc = 'PENDIENTE';
    mostrar();
    expect(await screen.findByText('WALLET_LISTA')).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Verificar identidad en Mi Wallet' })).toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('antes de conocer el PIN no monta la Wallet ni permite saltar la preparación', async () => {
    mockGet.mockImplementation((path: string) => path === '/api/wallets/mi-wallet'
      ? Promise.resolve({ success: true, data: { idWallet: 2 } }) : new Promise(() => {}));
    mostrar();
    expect(await screen.findByRole('status')).toHaveTextContent('Consultando la preparación');
    expect(screen.queryByText('WALLET_LISTA')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Registrar más tarde' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('crea PIN6 con contraseña, confirma el estado y ofrece la llave sin guardar secretos', async () => {
    const user = userEvent.setup();
    mostrar();
    expect(await screen.findByLabelText('Contraseña de ingreso')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar' })).not.toBeInTheDocument();
    expect(screen.queryByText('WALLET_LISTA')).not.toBeInTheDocument();
    await crearPin(user);
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/pin', { claveActual: 'Clave-sintetica-1', pin: '025801', confirmacionPin: '025801' });
    expect(screen.getByText(/La llave no es necesaria para enviar entre Wallets ni pagar con QR/)).toBeInTheDocument();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(mockPost.mock.calls.some(([path]) => path === '/api/breb/mi-llave/resolver')).toBe(false);
  });

  it.each(['25801', '0258014'])('cambia el PIN anterior %s por seis, sin pedir la contraseña de ingreso', async anterior => {
    pin = { ...PIN_LISTO, requiereActualizacion: true };
    const user = userEvent.setup();
    mostrar();
    await user.type(await screen.findByLabelText('PIN actual (5, 6 o 7 dígitos)'), anterior);
    expect(screen.queryByLabelText('Contraseña de ingreso')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('PIN nuevo (6 dígitos)'), '369147');
    await user.type(screen.getByLabelText('Confirmar PIN'), '369147');
    await user.click(screen.getByRole('button', { name: 'Cambiar PIN' }));
    await screen.findByRole('heading', { name: 'Mi llave Bre-B' });
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/pin/cambiar', { pinActual: anterior, pinNuevo: '369147', confirmacionPinNuevo: '369147' });
  });

  it('con PIN6 existente entra directamente y recuerda la llave pendiente sin bloquear ni volver a registrar', async () => {
    pin = { ...PIN_LISTO };
    mostrar();
    expect(await screen.findByText('WALLET_LISTA')).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Registrar mi llave en Perfil' })).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('si se cerró después de guardar PIN6, el próximo ingreso mantiene un aviso de llave sin imponer otro formulario', async () => {
    const user = userEvent.setup();
    const first = mostrar();
    await crearPin(user);
    first.unmount();
    mostrar();
    expect(await screen.findByText('WALLET_LISTA')).toBeInTheDocument();
    expect(await screen.findByRole('complementary', { name: 'Llave Bre-B para retiros' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Valor de la llave')).not.toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1);
  });

  it('durante la preparación oculta acciones monetarias y QR, conservando Perfil y salir', async () => {
    const user = userEvent.setup();
    mostrar(false, true);
    await screen.findByLabelText('Contraseña de ingreso');
    expect(screen.queryByRole('group', { name: 'Acciones principales' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'QR' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Perfil' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Perfil' }));
    await user.click(screen.getByRole('button', { name: 'Mi perfil' }));
    expect(onProfile).toHaveBeenCalledOnce();
    await user.click(screen.getByRole('button', { name: 'Abrir perfil' }));
    await user.click(screen.getByRole('button', { name: 'Cerrar sesión' }));
    expect(onLogout).toHaveBeenCalledOnce();
    expect(onAction).not.toHaveBeenCalled();
  });

  it('Perfil sigue disponible aunque el PIN esté pendiente de configuración', async () => {
    mostrar(false, true, true);
    expect(await screen.findByText('PERFIL_LISTO')).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Acciones principales' })).not.toBeInTheDocument();
  });

  it('no avanza ni celebra una preparación lista si el PIN confirmado está bloqueado', async () => {
    mockPost.mockImplementationOnce(() => {
      pin = { ...PIN_LISTO, bloqueado: true, intentosRestantes: 0 };
      return Promise.resolve({ success: true, message: 'PIN guardado.' });
    });
    const user = userEvent.setup();
    mostrar();
    await user.type(await screen.findByLabelText('Contraseña de ingreso'), 'Clave-sintetica-1');
    await user.type(screen.getByLabelText('Nuevo PIN (6 dígitos)'), '025801');
    await user.type(screen.getByLabelText('Confirmar PIN'), '025801');
    await user.click(screen.getByRole('button', { name: 'Crear PIN' }));
    expect(await screen.findByText(/Tu PIN está bloqueado\. Solicita/)).toBeInTheDocument();
    expect(screen.queryByText('Tu PIN de 6 dígitos quedó guardado.')).not.toBeInTheDocument();
    expect(screen.queryByText('WALLET_LISTA')).not.toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
  });

  it.each(['PENDIENTE_VALIDACION', 'VALIDADA'])('con llave ya registrada %s muestra su estado y evita otro registro', async estado => {
    llave = { idBrebLlave: 4, keyType: 'ALPHA', keyValueMasked: '***0118', estado };
    const user = userEvent.setup();
    mostrar();
    await crearPin(user);
    expect(await screen.findByText(estado.replace(/_/g, ' '))).toBeInTheDocument();
    expect(screen.queryByLabelText('Valor de la llave')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Actualizar llave' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continuar a mi Wallet' }));
    expect(await screen.findByText('WALLET_LISTA')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave', expect.anything());
  });

  it('registrar la llave más tarde permite enviar entre Wallets usando PIN6', async () => {
    const user = userEvent.setup();
    mostrar(true);
    await crearPin(user);
    await user.click(screen.getByRole('button', { name: 'Registrar más tarde' }));
    await user.click(await screen.findByText('Otras opciones'));
    await user.click(screen.getByText('Ingresar destino manualmente →'));
    await user.type(screen.getByLabelText('ID de wallet destino'), '3');
    await user.click(screen.getByRole('button', { name: 'Confirmar destino' }));
    await screen.findByText('Destino verificado');
    await user.type(screen.getByLabelText(/Valor a transferir/), '500');
    await user.type(screen.getByLabelText(/PIN transaccional/), '025801');
    await user.click(screen.getByRole('button', { name: 'Enviar dinero' }));
    expect(await screen.findByRole('dialog', { name: 'Transferencia exitosa' })).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledWith('/api/wallets/transferencia', expect.objectContaining({ pin: '025801' }), expect.anything());
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave', expect.anything());
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave/resolver', expect.anything());
  });

  it('registrar la llave más tarde también permite pagar con QR usando PIN6', async () => {
    const user = userEvent.setup();
    mostrar(true, false, false, 'pagar');
    await crearPin(user);
    await user.click(screen.getByRole('button', { name: 'Registrar más tarde' }));
    await user.click(await screen.findByText('Otras opciones'));
    await user.type(screen.getByLabelText('Pegar código QR o contenido JSON'), 'QR-PRUEBA-001');
    await user.click(screen.getByRole('button', { name: 'Usar código pegado' }));
    await screen.findByText('Comercio de prueba');
    await user.click(screen.getByRole('button', { name: '💳 Pagar con Wallet' }));
    await user.type(screen.getByLabelText(/Valor a pagar/), '100');
    await user.type(screen.getByLabelText(/PIN transaccional/), '025801');
    await user.click(screen.getByRole('button', { name: 'Pagar QR con Wallet' }));
    expect(await screen.findByText('✓ Pago realizado')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledWith('/api/qr/pagar', expect.objectContaining({ pin: '025801' }), expect.anything());
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave', expect.anything());
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave/resolver', expect.anything());
  });

  it('un 404 al consultar PIN no permite pasar la puerta y puede reintentarse', async () => {
    const user = userEvent.setup();
    errorPin = true;
    mostrar();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText('WALLET_LISTA')).not.toBeInTheDocument();
    errorPin = false;
    await user.click(screen.getByRole('button', { name: 'Actualizar preparación' }));
    expect(await screen.findByLabelText('Contraseña de ingreso')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('la señal 428 vuelve a consultar y muestra el cambio sin repetir la operación', async () => {
    pin = { ...PIN_LISTO };
    mostrar();
    await screen.findByText('WALLET_LISTA');
    pin = { ...PIN_LISTO, requiereActualizacion: true };
    act(() => { window.dispatchEvent(new Event('xpay:pin-actualizacion-requerida')); });
    expect(await screen.findByLabelText('PIN actual (5, 6 o 7 dígitos)')).toBeInTheDocument();
    expect(screen.queryByText('WALLET_LISTA')).not.toBeInTheDocument();
    await waitFor(() => expect(mockPost).not.toHaveBeenCalled());
  });
});
