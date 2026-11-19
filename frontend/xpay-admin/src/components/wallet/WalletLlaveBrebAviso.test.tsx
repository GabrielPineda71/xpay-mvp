import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { WalletLlaveBrebAviso } from './WalletLlaveBrebAviso.tsx';

const mockGet = vi.fn();
const mockPost = vi.fn();

vi.mock('../../api/client.ts', () => ({
  get: (...args: unknown[]) => mockGet(...args),
  post: (...args: unknown[]) => mockPost(...args),
}));

type KycResponse = { success: boolean; data: { estadoKyc: string } };
type KeyResponse = { success: boolean; data: { estado: string } | null };

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

let estadoKyc: string;
let llave: { estado: string } | null;
let kycQuery: Promise<KycResponse> | null;
let llaveQuery: Promise<KeyResponse> | null;

function renderAviso() {
  return render(<MemoryRouter><WalletLlaveBrebAviso /></MemoryRouter>);
}

async function actualizarEstado() {
  await act(async () => {
    window.dispatchEvent(new Event('xpay:kyc-estado-actualizado'));
  });
}

beforeEach(() => {
  estadoKyc = 'PENDIENTE';
  llave = null;
  kycQuery = null;
  llaveQuery = null;
  mockPost.mockReset();
  mockGet.mockReset().mockImplementation((path: string) => {
    if (path === '/api/kyc/mi-estado') {
      return kycQuery ?? Promise.resolve({ success: true, data: { estadoKyc } });
    }
    if (path === '/api/breb/mi-llave') {
      return llaveQuery ?? Promise.resolve({ success: true, data: llave });
    }
    return Promise.reject(new Error(`GET no simulado: ${path}`));
  });
});

describe('WalletLlaveBrebAviso — actualización de identidad sin remontaje', () => {
  it('al aprobarse la identidad consulta la llave y ofrece registrarla solo si no existe', async () => {
    renderAviso();
    expect(await screen.findByRole('link', { name: 'Verificar identidad en Mi Wallet' })).toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');

    estadoKyc = 'APROBADO';
    await actualizarEstado();

    expect(await screen.findByRole('link', { name: 'Registrar mi llave en Perfil' })).toHaveAttribute('href', '/mi-wallet/perfil');
    expect(screen.queryByRole('link', { name: 'Verificar identidad en Mi Wallet' })).not.toBeInTheDocument();
    expect(mockGet.mock.calls.filter(([path]) => path === '/api/kyc/mi-estado')).toHaveLength(2);
    expect(mockGet.mock.calls.filter(([path]) => path === '/api/breb/mi-llave')).toHaveLength(1);
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('al aprobarse la identidad oculta el aviso si la llave ya está validada', async () => {
    renderAviso();
    await screen.findByRole('link', { name: 'Verificar identidad en Mi Wallet' });

    estadoKyc = 'APROBADO';
    llave = { estado: 'VALIDADA' };
    await actualizarEstado();

    expect(mockGet).toHaveBeenCalledWith('/api/breb/mi-llave');
    expect(screen.queryByRole('complementary', { name: 'Llave Bre-B para retiros' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('una respuesta anterior de identidad no revierte la consulta más reciente', async () => {
    const anterior = deferred<KycResponse>();
    kycQuery = anterior.promise;
    renderAviso();
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/kyc/mi-estado'));

    kycQuery = null;
    estadoKyc = 'APROBADO';
    await actualizarEstado();
    await screen.findByRole('link', { name: 'Registrar mi llave en Perfil' });

    await act(async () => { anterior.resolve({ success: true, data: { estadoKyc: 'PENDIENTE' } }); });

    expect(screen.getByRole('link', { name: 'Registrar mi llave en Perfil' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Verificar identidad en Mi Wallet' })).not.toBeInTheDocument();
    expect(mockGet.mock.calls.filter(([path]) => path === '/api/breb/mi-llave')).toHaveLength(1);
  });

  it('una respuesta anterior de llave no reemplaza el estado más reciente', async () => {
    const anterior = deferred<KeyResponse>();
    estadoKyc = 'APROBADO';
    llaveQuery = anterior.promise;
    renderAviso();
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/breb/mi-llave'));

    llaveQuery = null;
    await actualizarEstado();
    await screen.findByRole('link', { name: 'Registrar mi llave en Perfil' });

    await act(async () => { anterior.resolve({ success: true, data: { estado: 'VALIDADA' } }); });

    expect(screen.getByRole('link', { name: 'Registrar mi llave en Perfil' })).toBeInTheDocument();
    expect(mockGet.mock.calls.filter(([path]) => path === '/api/breb/mi-llave')).toHaveLength(2);
    expect(mockPost).not.toHaveBeenCalled();
  });
});
