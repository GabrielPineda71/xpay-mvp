// Verificación de contactos — CERO red real: api/client.ts mockeado.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { VerificarContacto } from './VerificarContacto.tsx';
import { ComunicacionesSection } from './ComunicacionesSection.tsx';
import { ApiError } from '../../api/client.ts';

const mockGet = vi.fn();
const mockPost = vi.fn();
const mockPut = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
    put: (...args: unknown[]) => mockPut(...args),
  };
});

beforeEach(() => {
  mockGet.mockReset();
  mockPost.mockReset();
  mockPut.mockReset();
});

describe('VerificarContacto', () => {
  it('correo: pide el código, confirma y avisa al terminar', async () => {
    const user = userEvent.setup();
    const onVerificado = vi.fn();
    mockPost.mockImplementation((path: string) => Promise.resolve(
      path === '/api/contactos/email/codigo'
        ? { success: true, message: 'Te enviamos un código a j***@correo.com. Vence en 10 minutos.' }
        : { success: true, message: 'Correo verificado.' }));
    render(<VerificarContacto canal="email" onVerificado={onVerificado} />);

    await user.click(screen.getByRole('button', { name: 'Verificar correo' }));
    await user.click(screen.getByRole('button', { name: 'Enviar código al correo' }));
    expect(mockPost).toHaveBeenCalledWith('/api/contactos/email/codigo', { aceptaServicioWhatsApp: false });
    expect(await screen.findByText(/Te enviamos un código/)).toBeInTheDocument();

    await user.type(screen.getByLabelText('Código de verificación'), '48a29-13');
    expect(screen.getByLabelText('Código de verificación')).toHaveValue('482913');
    await user.click(screen.getByRole('button', { name: 'Confirmar código' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/contactos/email/verificar', { codigo: '482913' }));
    await waitFor(() => expect(onVerificado).toHaveBeenCalled());
  });

  it('celular: sin autorizar mensajes de servicio por WhatsApp no envía', async () => {
    const user = userEvent.setup();
    render(<VerificarContacto canal="celular" onVerificado={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Verificar celular' }));
    await user.click(screen.getByRole('button', { name: 'Enviar código por WhatsApp' }));
    expect(mockPost).not.toHaveBeenCalled();
    expect(screen.getByText(/debes autorizar los mensajes de servicio/)).toBeInTheDocument();

    mockPost.mockResolvedValue({ success: true, message: 'Te enviamos un código por WhatsApp.' });
    await user.click(screen.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: 'Enviar código por WhatsApp' }));
    expect(mockPost).toHaveBeenCalledWith('/api/contactos/celular/codigo', { aceptaServicioWhatsApp: true });
  });

  it('código incorrecto: muestra el mensaje del backend, limpia el campo y no da por verificado', async () => {
    const user = userEvent.setup();
    const onVerificado = vi.fn();
    mockPost.mockImplementation((path: string) => path.endsWith('/codigo')
      ? Promise.resolve({ success: true, message: 'Te enviamos un código.' })
      : Promise.reject(new ApiError('Código incorrecto. Te quedan 4 intento(s).', 400)));
    render(<VerificarContacto canal="email" onVerificado={onVerificado} />);

    await user.click(screen.getByRole('button', { name: 'Verificar correo' }));
    await user.click(screen.getByRole('button', { name: 'Enviar código al correo' }));
    await user.type(await screen.findByLabelText('Código de verificación'), '111111');
    await user.click(screen.getByRole('button', { name: 'Confirmar código' }));

    expect(await screen.findByText('Código incorrecto. Te quedan 4 intento(s).')).toBeInTheDocument();
    expect(screen.getByLabelText('Código de verificación')).toHaveValue('');
    expect(onVerificado).not.toHaveBeenCalled();
  });
});

describe('ComunicacionesSection', () => {
  const ESTADO = {
    emailVerificado: false, celularVerificado: false,
    canalEmailDisponible: true, canalWhatsAppDisponible: true, requeridoParaOperar: false,
    servicioWhatsApp: false, publicidadEmail: false, publicidadWhatsApp: false,
  };

  it('la publicidad aparece desactivada y se cambia por separado', async () => {
    const user = userEvent.setup();
    mockGet.mockResolvedValue({ success: true, data: ESTADO });
    mockPut.mockResolvedValue({ success: true, data: { ...ESTADO, publicidadEmail: true } });
    render(<ComunicacionesSection />);

    const correo = await screen.findByLabelText('Ofertas y novedades de XPAY por correo');
    expect(correo).not.toBeChecked();
    expect(screen.getByLabelText('Ofertas y novedades de XPAY por WhatsApp')).not.toBeChecked();
    await user.click(correo);
    expect(mockPut).toHaveBeenCalledWith('/api/contactos/preferencias', { publicidadEmail: true });
    await waitFor(() => expect(screen.getByLabelText('Ofertas y novedades de XPAY por correo')).toBeChecked());
  });

  it('solo avisa del requisito cuando el backend lo informa efectivo', async () => {
    mockGet.mockResolvedValue({ success: true, data: ESTADO });
    const { unmount } = render(<ComunicacionesSection />);
    await screen.findByLabelText('Ofertas y novedades de XPAY por correo');
    expect(screen.queryByTestId('contactos-requeridos')).not.toBeInTheDocument();
    unmount();

    mockGet.mockResolvedValue({ success: true, data: { ...ESTADO, requeridoParaOperar: true } });
    render(<ComunicacionesSection />);
    expect(await screen.findByTestId('contactos-requeridos')).toBeInTheDocument();
  });
});
