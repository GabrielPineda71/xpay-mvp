import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { ApiError } from '../api/client.ts';
import { UserWalletPage } from './UserWalletPage.tsx';

const { get, post } = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));

vi.mock('../api/client.ts', async (importOriginal) => ({
  ...await importOriginal<typeof import('../api/client.ts')>(), get, post,
}));

vi.mock('../auth/AuthContext.tsx', () => {
  const user = {
    idUsuario: 7, idPersona: 7, usuario: 'fixture.wallet',
    roles: ['USUARIO_FINAL'], estado: 'ACTIVO', requiereCambioClave: false,
  };
  return { useAuth: () => ({ user }) };
});

const rutaWallet = '/api/wallets/mi-wallet';
const rutaActivacion = '/api/wallets/mi-wallet/activacion';
const rutaCuenta = '/api/reportes/mi-estado-cuenta';

function cargarPagina(whatsappVerificado: boolean) {
  let walletActiva = false;
  const wallet = { idWallet: 10, idPersona: 7, nombreWallet: 'Wallet de prueba', estado: 'ACTIVA' };
  get.mockImplementation(async (path: string) => {
    if (path === rutaWallet) {
      if (!walletActiva) throw new ApiError('No se encontró Wallet activa.', 404);
      return { success: true, data: wallet };
    }
    if (path === rutaActivacion) return {
      success: true,
      data: {
        identidadVerificada: true, emailVerificado: true, whatsAppVerificado: whatsappVerificado,
        whatsAppDisponible: whatsappVerificado, walletRestringida: false, puedeActivar: whatsappVerificado,
      },
    };
    if (path === '/api/kyc/mi-estado') return { success: true, data: { estadoKyc: 'APROBADO' } };
    if (path === rutaCuenta)
      return { success: true, data: { ...wallet, saldoDisponible: 0, saldoRetenido: 0, movimientos: [] } };
    if (path === '/api/breb/mi-llave') return { success: true, data: null };
    if (path === '/api/breb/mis-retiros') return { success: true, data: [] };
    throw new Error(`GET inesperado ${path}`);
  });
  post.mockImplementation(async () => {
    walletActiva = true;
    return { success: true, data: { walletActiva } };
  });

  render(<MemoryRouter><UserWalletPage /></MemoryRouter>);
}

beforeEach(() => { get.mockReset(); post.mockReset(); });

describe('Mi Wallet pendiente de activación', () => {
  it('muestra WhatsApp pendiente y no habilita la activación sin esa verificación', async () => {
    cargarPagina(false);
    expect(await screen.findByText('WhatsApp · Pendiente')).toBeInTheDocument();
    expect(screen.getByText('La verificación por WhatsApp aún no está disponible.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Activar Wallet' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Verificar contactos en Perfil' })).toBeInTheDocument();
    expect(get).not.toHaveBeenCalledWith(rutaCuenta);
    expect(post).not.toHaveBeenCalled();
  });

  it('activa y carga el saldo cuando el backend confirma las tres verificaciones', async () => {
    const usuario = userEvent.setup();
    cargarPagina(true);
    const boton = await screen.findByRole('button', { name: 'Activar Wallet' });
    await usuario.click(boton);
    expect(post).toHaveBeenCalledWith(rutaActivacion, {});
    await waitFor(() => expect(get).toHaveBeenCalledWith(rutaCuenta));
    expect(screen.queryByRole('heading', { name: 'Activa tu Wallet' })).not.toBeInTheDocument();
  });
});
