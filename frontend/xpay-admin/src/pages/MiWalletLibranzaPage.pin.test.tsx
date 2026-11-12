// PIN transaccional — el anticipo exige el PIN y lo envía en el cuerpo; un
// rechazo del PIN (4xx) se muestra tal cual. get/post mockeados.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MiWalletLibranzaPage } from './MiWalletLibranzaPage.tsx';
import { ApiError } from '../api/client.ts';

const mockGet = vi.fn();
const mockPost = vi.fn();
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a) };
});

const cupo = {
  idConvenio: 5, nombreEmpresa: 'Empresa Prueba', idEmpleado: 8, nombresEmpleado: 'Ana Prueba',
  numeroDocumento: '1', periodicidadPago: 'MENSUAL', anticiposActivos: [], historialAnticipos: [],
  corteVigente: {
    diaPago: 30, periodoInicio: 1, periodoFin: 30, fechaPago: '2026-11-30', valorPagoProgramado: 600000,
    porcentajeCupo: 30, cupoBase: 180000, cupoUsado: 0, cupoDisponible: 180000, fechaSimulada: '2026-11-12', esDiaPago: false,
  },
};

beforeEach(() => {
  mockGet.mockReset();
  mockPost.mockReset();
  sessionStorage.clear();
  mockGet.mockResolvedValue({ success: true, data: cupo });
});

async function llenar(pin: string) {
  const user = userEvent.setup();
  render(<MiWalletLibranzaPage />);
  await user.type(await screen.findByPlaceholderText('100000'), '100000');
  if (pin) await user.type(screen.getByLabelText('PIN transaccional (7 dígitos)'), pin);
  return user;
}

describe('MiWalletLibranzaPage — PIN transaccional', () => {
  it('sin PIN completo el botón queda deshabilitado y no se llama al backend', async () => {
    await llenar('258');
    expect(screen.getByRole('button', { name: 'Solicitar anticipo' })).toBeDisabled();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('envía el PIN en el cuerpo del anticipo', async () => {
    mockPost.mockResolvedValue({ success: true, data: { idAnticipo: 6, valorNetoDesembolsado: 100000, valorComision: 0, valorIva: 0 } });
    const user = await llenar('2580147');
    await user.click(screen.getByRole('button', { name: 'Solicitar anticipo' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith(
      '/api/libranza/cliente/anticipos',
      { valorSolicitado: 100000, pin: '2580147' },
      expect.objectContaining({ 'Idempotency-Key': expect.any(String) }),
    ));
  });

  it('PIN incorrecto: muestra el mensaje del backend y limpia el PIN', async () => {
    mockPost.mockRejectedValue(new ApiError('PIN incorrecto. Te quedan 4 intento(s).', 400));
    const user = await llenar('1111112');
    await user.click(screen.getByRole('button', { name: 'Solicitar anticipo' }));

    expect(await screen.findByText('PIN incorrecto. Te quedan 4 intento(s).')).toBeInTheDocument();
    expect((screen.getByLabelText('PIN transaccional (7 dígitos)') as HTMLInputElement).value).toBe('');
  });
});
