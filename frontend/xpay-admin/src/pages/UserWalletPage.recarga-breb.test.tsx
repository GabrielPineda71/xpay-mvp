import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { UserWalletPage } from './UserWalletPage.tsx';
import { WalletRecargaBreb } from '../components/wallet/WalletRecargaBreb.tsx';
import { HttpUncertainError } from '../api/client.ts';

const mockGet = vi.fn();
const mockPost = vi.fn();
const mockUser = { idUsuario: 3, idPersona: 3, usuario: 'usuario-prueba', estado: 'ACTIVO', roles: [], token: 'prueba' };
const mockToDataURL = vi.hoisted(() => vi.fn());

vi.mock('../api/client.ts', async () => ({
  ...await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts'),
  get: (...args: unknown[]) => mockGet(...args),
  post: (...args: unknown[]) => mockPost(...args),
}));
vi.mock('../auth/AuthContext.tsx', () => ({
  useAuth: () => ({
    user: mockUser,
  }),
}));
vi.mock('qrcode', () => ({ default: { toDataURL: mockToDataURL } }));
vi.mock('html5-qrcode', () => ({ Html5Qrcode: class {} }));

const ENDPOINT = '/api/wallets/mi-wallet/recarga-breb';
const QR = {
  idRecargaQr: 9,
  referencia: 'W1234567890123456',
  qrCodeData: 'contenido-breb-estatico-confirmado-por-api',
  estado: 'ACTIVO',
  recargas: [] as { idRecarga: number; valor: number; fecha: string; estado: string }[],
};

function routedGet(path: string) {
  if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 3, nombreWallet: 'Wallet', estado: 'ACTIVA' } });
  if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: { idWallet: 2, nombreWallet: 'Wallet', estado: 'ACTIVA', saldoDisponible: 10000, saldoRetenido: 0, movimientos: [] } });
  if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
  if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
  if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
  if (path === ENDPOINT) return Promise.resolve({ success: true, data: null });
  return Promise.reject(new Error(`GET inesperado ${path}`));
}

async function abrirDesdeSaldo() {
  render(<MemoryRouter initialEntries={['/mi-wallet']}><UserWalletPage /></MemoryRouter>);
  await userEvent.click(await screen.findByRole('button', { name: 'Recargar desde mi banco' }));
}

beforeEach(() => {
  mockGet.mockReset().mockImplementation(routedGet);
  mockPost.mockReset();
  mockToDataURL.mockReset().mockResolvedValue('data:image/png;base64,cXItcHJ1ZWJh');
});

describe('Recarga Bre-B desde Mi Wallet', () => {
  it('abre desde el saldo y genera un QR descargable sin pedir ni enviar un monto', async () => {
    mockPost.mockResolvedValue({ success: true, data: QR });
    await abrirDesdeSaldo();
    await userEvent.click(await screen.findByRole('button', { name: 'Generar mi QR de recarga' }));

    expect(await screen.findByRole('img', { name: /QR Bre-B para recargar mi Wallet/ })).toHaveAttribute('src', 'data:image/png;base64,cXItcHJ1ZWJh');
    expect(mockPost).toHaveBeenCalledExactlyOnceWith(ENDPOINT, {}, undefined, 45_000);
    expect(mockToDataURL).toHaveBeenCalledWith(QR.qrCodeData, expect.any(Object));
    expect(screen.getByRole('link', { name: 'Descargar QR PNG' })).toHaveAttribute('download', 'xpay-recarga-breb.png');
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
    expect(screen.getByText(/indica el valor que deseas recargar/)).toBeInTheDocument();
    expect(screen.getByText(/cuando el banco lo confirme/)).toBeInTheDocument();
    expect(screen.queryByText(/Recarga exitosa/i)).not.toBeInTheDocument();
  });

  it('reutiliza el QR existente y consulta el saldo y las recargas confirmadas al actualizar', async () => {
    let consultasQr = 0;
    mockGet.mockImplementation((path: string) => {
      if (path === ENDPOINT) {
        consultasQr++;
        return Promise.resolve({ success: true, data: { ...QR, recargas: consultasQr > 1
          ? [{ idRecarga: 15, valor: 12345, fecha: '2026-11-17T20:00:00Z', estado: 'APLICADA' }]
          : [] } });
      }
      return routedGet(path);
    });
    await abrirDesdeSaldo();
    await screen.findByRole('img', { name: /QR Bre-B para recargar mi Wallet/ });
    await userEvent.click(screen.getByRole('button', { name: 'Actualizar estado' }));

    expect(await screen.findByText('$ 12.345')).toBeInTheDocument();
    await waitFor(() => expect(mockGet.mock.calls.filter(([path]) => path === '/api/reportes/mi-estado-cuenta').length).toBeGreaterThanOrEqual(2));
    expect(mockPost).not.toHaveBeenCalled();
    expect(consultasQr).toBe(2);
  });

  it('consulta una generación incierta y recupera el QR sin repetir el POST', async () => {
    let creado = false;
    mockGet.mockImplementation((path: string) => path === ENDPOINT
      ? Promise.resolve({ success: true, data: creado ? QR : null })
      : routedGet(path));
    mockPost.mockImplementation(async () => {
      creado = true;
      throw new HttpUncertainError('Respuesta pendiente');
    });
    await abrirDesdeSaldo();
    await userEvent.click(await screen.findByRole('button', { name: 'Generar mi QR de recarga' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/pendiente de confirmar/);
    await userEvent.click(screen.getByRole('button', { name: 'Actualizar estado' }));

    expect(await screen.findByRole('img', { name: /QR Bre-B para recargar mi Wallet/ })).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1);
  });

  it('permite volver a mostrar el QR si falla la imagen sin crear otro código', async () => {
    mockGet.mockResolvedValue({ success: true, data: QR });
    mockToDataURL.mockRejectedValueOnce(new Error('Canvas no disponible'));
    render(<WalletRecargaBreb onActualizarSaldo={vi.fn()} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Reintentar mostrar QR' }));

    expect(await screen.findByRole('img', { name: /QR Bre-B para recargar mi Wallet/ })).toBeInTheDocument();
    expect(mockGet).toHaveBeenCalledTimes(1);
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('descarta una respuesta anterior cuando cambia la Wallet mostrada', async () => {
    let completarAnterior: (respuesta: unknown) => void = () => {};
    mockGet.mockImplementationOnce(() => new Promise(resolve => { completarAnterior = resolve; }))
      .mockResolvedValueOnce({ success: true, data: { ...QR, qrCodeData: 'qr-wallet-actual' } });
    const vista = render(<WalletRecargaBreb key="wallet-anterior" onActualizarSaldo={vi.fn()} />);
    vista.rerender(<WalletRecargaBreb key="wallet-actual" onActualizarSaldo={vi.fn()} />);
    await screen.findByRole('img', { name: /QR Bre-B para recargar mi Wallet/ });
    completarAnterior({ success: true, data: { ...QR, qrCodeData: 'qr-wallet-anterior' } });
    await waitFor(() => expect(mockToDataURL).toHaveBeenCalledTimes(1));
    expect(mockToDataURL).toHaveBeenCalledWith('qr-wallet-actual', expect.any(Object));
  });
});
