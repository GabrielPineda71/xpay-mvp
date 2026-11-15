import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { LoginPage } from './LoginPage.tsx';
import * as api from '../api/client.ts';

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
      { path: '/registro', element: <p>PANTALLA REGISTRO</p> },
    ],
    { initialEntries: ['/login'] },
  );
  render(<RouterProvider router={router} />);
  return router;
}

beforeEach(() => {
  mockLogin.mockReset();
  sessionStorage.clear();
  vi.stubEnv('VITE_XPAY_ENV', 'QA');
  vi.spyOn(api, 'get').mockRejectedValue(new api.ApiError('No encontrado.', 404));
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
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

  it('muestra y oculta la contraseña sin cambiar el valor ni iniciar sesión', async () => {
    const user = userEvent.setup();
    renderizar();
    const password = screen.getByLabelText('Contraseña');

    expect(password).toHaveAttribute('type', 'password');
    await user.type(password, 'ClaveDePrueba1!');
    await user.click(screen.getByRole('button', { name: 'Mostrar contraseña' }));
    expect(password).toHaveAttribute('type', 'text');
    expect(password).toHaveValue('ClaveDePrueba1!');
    await user.click(screen.getByRole('button', { name: 'Ocultar contraseña' }));
    expect(password).toHaveAttribute('type', 'password');
    expect(password).toHaveValue('ClaveDePrueba1!');
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('ofrece Crear cuenta en QA si la API devuelve ambos documentos', async () => {
    const documento = { version: '1.1', fecha: '2026-11-13', url: 'https://app-qa.xpaycol.com/legal/documento.pdf' };
    vi.mocked(api.get).mockResolvedValue({ data: { terminos: documento, politicaDatos: documento } });
    const user = userEvent.setup();
    renderizar();

    const enlace = await screen.findByRole('link', { name: 'Crear cuenta' });
    expect(api.get).toHaveBeenCalledWith('/api/usuarios/registro/configuracion');
    await user.click(enlace);
    expect(await screen.findByText('PANTALLA REGISTRO')).toBeInTheDocument();
  });

  it('mantiene el ingreso de PROD sin consultar disponibilidad ni mostrar Crear cuenta', () => {
    vi.stubEnv('VITE_XPAY_ENV', 'PROD');
    renderizar();

    expect(screen.queryByRole('link', { name: 'Crear cuenta' })).not.toBeInTheDocument();
    expect(api.get).not.toHaveBeenCalled();
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
