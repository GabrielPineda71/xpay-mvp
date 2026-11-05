// P0-B frontend — LoginPage: solo se añaden dos enlaces públicos de
// identidad. El error de credenciales sigue siendo el genérico del backend.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { LoginPage } from './LoginPage.tsx';

const mockLogin = vi.fn();

vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual<typeof import('../auth/AuthContext.tsx')>('../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: null, login: mockLogin, logout: vi.fn(), actualizarToken: vi.fn() }) };
});

function renderizar() {
  const router = createMemoryRouter(
    [
      { path: '/login', element: <LoginPage /> },
      { path: '/recuperar-clave', element: <p>PANTALLA RECUPERAR</p> },
      { path: '/reenviar-verificacion', element: <p>PANTALLA REENVIO</p> },
    ],
    { initialEntries: ['/login'] },
  );
  render(<RouterProvider router={router} />);
  return router;
}

beforeEach(() => {
  mockLogin.mockReset();
  sessionStorage.clear();
});

describe('LoginPage — enlaces de identidad (P0-B frontend)', () => {
  it('"¿Olvidaste tu contraseña?" lleva a /recuperar-clave', async () => {
    const user = userEvent.setup();
    renderizar();
    const enlace = screen.getByRole('link', { name: '¿Olvidaste tu contraseña?' });
    expect(enlace).toHaveAttribute('href', '/recuperar-clave');
    await user.click(enlace);
    expect(await screen.findByText('PANTALLA RECUPERAR')).toBeInTheDocument();
  });

  it('"¿No recibiste el correo de verificación?" lleva a /reenviar-verificacion', async () => {
    const user = userEvent.setup();
    renderizar();
    const enlace = screen.getByRole('link', { name: '¿No recibiste el correo de verificación?' });
    expect(enlace).toHaveAttribute('href', '/reenviar-verificacion');
    await user.click(enlace);
    expect(await screen.findByText('PANTALLA REENVIO')).toBeInTheDocument();
  });

  it('el error de credenciales se muestra igual que antes, sin detectar PENDIENTE_VERIFICACION', async () => {
    mockLogin.mockRejectedValueOnce(new Error('Usuario o contraseña inválidos.'));
    const user = userEvent.setup();
    renderizar();
    await user.type(screen.getByLabelText('Usuario'), 'persona.prueba');
    await user.type(screen.getByLabelText('Contraseña'), 'ClaveNueva1!');
    await user.click(screen.getByRole('button', { name: 'Ingresar' }));

    expect(await screen.findByText('Usuario o contraseña inválidos.')).toBeInTheDocument();
    expect(mockLogin).toHaveBeenCalledWith('persona.prueba', 'ClaveNueva1!');
    expect(screen.queryByText(/verific/i, { selector: '.error' })).not.toBeInTheDocument();
  });
});
