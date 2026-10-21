// P0-B frontend — /reenviar-verificacion. CERO red real: `post` mockeado;
// ApiError/HttpUncertainError reales (importActual). Respuesta neutra: la UI
// nunca depende del texto del backend ni revela si el usuario existe.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { ReenviarVerificacionPage } from './ReenviarVerificacionPage.tsx';
import { ApiError, HttpUncertainError } from '../api/client.ts';

const mockPost = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return {
    ...actual,
    post: (...args: unknown[]) => mockPost(...args),
  };
});

const NEUTRO = 'Si la cuenta está pendiente de verificación, enviaremos un nuevo enlace a su correo registrado.';
const GENERICO = 'No fue posible procesar tu solicitud en este momento. Intenta nuevamente más tarde.';

function renderizar() {
  const router = createMemoryRouter(
    [
      { path: '/reenviar-verificacion', element: <ReenviarVerificacionPage /> },
      { path: '/login', element: <p>PANTALLA LOGIN</p> },
    ],
    { initialEntries: ['/reenviar-verificacion'] },
  );
  render(<RouterProvider router={router} />);
  return router;
}

async function solicitar(user: ReturnType<typeof userEvent.setup>, usuario: string) {
  if (usuario) await user.type(screen.getByLabelText('Usuario'), usuario);
  await user.click(screen.getByRole('button', { name: 'Reenviar enlace' }));
}

beforeEach(() => {
  mockPost.mockReset();
});

describe('ReenviarVerificacionPage (P0-B frontend)', () => {
  it('muestra la marca XPAY y el campo "Usuario"', () => {
    renderizar();
    expect(screen.getByRole('heading', { name: 'XPAY' })).toBeInTheDocument();
    expect(screen.getByLabelText('Usuario')).toBeInTheDocument();
  });

  it('usuario vacío: no hay POST', async () => {
    const user = userEvent.setup();
    renderizar();
    await solicitar(user, '');
    expect(await screen.findByText('Ingresa tu usuario.')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('202: POST exacto { usuario } recortado y mensaje neutro fijo (no el texto del backend)', async () => {
    mockPost.mockResolvedValueOnce({ success: true, message: 'TEXTO DEL BACKEND' });
    const user = userEvent.setup();
    renderizar();
    await solicitar(user, '  persona.prueba  ');

    expect(await screen.findByText(NEUTRO)).toBeInTheDocument();
    expect(screen.queryByText('TEXTO DEL BACKEND')).not.toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost).toHaveBeenCalledWith('/api/usuarios/reenviar-verificacion', { usuario: 'persona.prueba' });
  });

  it('un 400 inesperado usa el mensaje genérico y nunca muestra el detalle (no enumeración)', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('La cuenta ya está verificada.', 400));
    const user = userEvent.setup();
    renderizar();
    await solicitar(user, 'persona.prueba');
    expect(await screen.findByText(GENERICO)).toBeInTheDocument();
    expect(screen.queryByText('La cuenta ya está verificada.')).not.toBeInTheDocument();
  });

  it('429: mensaje de rate limit en español', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('Too many requests. Please try again later.', 429));
    const user = userEvent.setup();
    renderizar();
    await solicitar(user, 'persona.prueba');
    expect(await screen.findByText('Has realizado varios intentos. Intenta nuevamente más tarde.')).toBeInTheDocument();
  });

  it('timeout: mensaje de incertidumbre', async () => {
    mockPost.mockRejectedValueOnce(new HttpUncertainError('timeout'));
    const user = userEvent.setup();
    renderizar();
    await solicitar(user, 'persona.prueba');
    expect(await screen.findByText(
      'No pudimos confirmar si recibimos tu solicitud. Espera unos minutos y revisa tu correo antes de intentarlo de nuevo.',
    )).toBeInTheDocument();
  });

  it('500: mensaje genérico', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('Error interno en el reenvío de verificación.', 500));
    const user = userEvent.setup();
    renderizar();
    await solicitar(user, 'persona.prueba');
    expect(await screen.findByText(GENERICO)).toBeInTheDocument();
  });

  it('"Volver a iniciar sesión" lleva a /login', async () => {
    const user = userEvent.setup();
    renderizar();
    await user.click(screen.getByRole('link', { name: 'Volver a iniciar sesión' }));
    expect(await screen.findByText('PANTALLA LOGIN')).toBeInTheDocument();
  });
});
