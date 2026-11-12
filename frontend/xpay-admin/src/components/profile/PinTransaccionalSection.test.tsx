// PIN transaccional — sección de Perfil. CERO red real: get/post mockeados;
// ApiError real (vi.importActual) para que `instanceof` funcione.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PinTransaccionalSection } from './PinTransaccionalSection.tsx';
import { ApiError } from '../../api/client.ts';

const mockGet = vi.fn();
const mockPost = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
  };
});

const estado = (e: Partial<{ configurado: boolean; bloqueado: boolean; restablecido: boolean; intentosRestantes: number }>) =>
  ({ success: true, data: { configurado: false, bloqueado: false, restablecido: false, intentosRestantes: 0, ...e } });

beforeEach(() => {
  mockGet.mockReset();
  mockPost.mockReset();
  localStorage.clear();
  sessionStorage.clear();
});

describe('PinTransaccionalSection', () => {
  it('sin PIN: crea con la contraseña de ingreso y envía solo los campos del contrato', async () => {
    mockGet.mockResolvedValueOnce(estado({})).mockResolvedValueOnce(estado({ configurado: true, intentosRestantes: 5 }));
    mockPost.mockResolvedValue({ success: true, message: 'PIN transaccional creado.' });
    const user = userEvent.setup();
    render(<PinTransaccionalSection />);

    await user.click(await screen.findByRole('button', { name: 'Crear PIN' }));
    await user.type(screen.getByLabelText('Contraseña de ingreso'), 'Clave-1');
    await user.type(screen.getByLabelText('Nuevo PIN (7 dígitos)'), '25a80147');
    await user.type(screen.getByLabelText('Confirmar PIN'), '2580147');
    await user.click(screen.getByRole('button', { name: 'Crear PIN' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/pin', {
      claveActual: 'Clave-1', pin: '2580147', confirmacionPin: '2580147',
    }));
    expect(await screen.findByRole('status')).toHaveTextContent('PIN transaccional creado.');
    expect(await screen.findByTestId('pin-estado')).toHaveTextContent('Tienes un PIN transaccional activo.');
  });

  it('confirmación distinta: no llama al backend', async () => {
    mockGet.mockResolvedValue(estado({}));
    const user = userEvent.setup();
    render(<PinTransaccionalSection />);

    await user.click(await screen.findByRole('button', { name: 'Crear PIN' }));
    await user.type(screen.getByLabelText('Contraseña de ingreso'), 'Clave-1');
    await user.type(screen.getByLabelText('Nuevo PIN (7 dígitos)'), '2580147');
    await user.type(screen.getByLabelText('Confirmar PIN'), '2580148');
    await user.click(screen.getByRole('button', { name: 'Crear PIN' }));

    expect(screen.getByText('El PIN y su confirmación no coinciden.')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('con PIN: cambia con el PIN actual y muestra el error del backend', async () => {
    mockGet.mockResolvedValue(estado({ configurado: true, intentosRestantes: 5 }));
    mockPost.mockRejectedValue(new ApiError('El PIN actual no coincide. Te quedan 4 intento(s).', 400));
    const user = userEvent.setup();
    render(<PinTransaccionalSection />);

    await user.click(await screen.findByRole('button', { name: 'Cambiar PIN' }));
    await user.type(screen.getByLabelText('PIN actual'), '1111112');
    await user.type(screen.getByLabelText('PIN nuevo (7 dígitos)'), '3691470');
    await user.type(screen.getByLabelText('Confirmar PIN'), '3691470');
    await user.click(screen.getByRole('button', { name: 'Cambiar PIN' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/pin/cambiar', {
      pinActual: '1111112', pinNuevo: '3691470', confirmacionPinNuevo: '3691470',
    }));
    expect(await screen.findByText('El PIN actual no coincide. Te quedan 4 intento(s).')).toBeInTheDocument();
  });

  it('bloqueado: informa y no ofrece crear ni cambiar', async () => {
    mockGet.mockResolvedValue(estado({ configurado: true, bloqueado: true }));
    render(<PinTransaccionalSection />);

    expect(await screen.findByTestId('pin-estado')).toHaveTextContent(/bloqueado/);
    expect(screen.queryByRole('button', { name: /Crear PIN|Cambiar PIN/ })).toBeNull();
  });

  it('restablecido por XPAY: pide crear uno nuevo', async () => {
    mockGet.mockResolvedValue(estado({ restablecido: true }));
    render(<PinTransaccionalSection />);

    expect(await screen.findByTestId('pin-estado')).toHaveTextContent(/restablecido por XPAY/);
    expect(screen.getByRole('button', { name: 'Crear PIN' })).toBeInTheDocument();
  });
});
