// P0-B frontend — /restablecer-clave. CERO red real: `post` mockeado;
// ApiError/HttpUncertainError reales (importActual). useAuth mockeado para
// observar logout(). Decisión J2: los 400 no se distinguen por texto; el
// formulario sigue disponible y el reintento reutiliza el mismo token.
import { StrictMode } from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { RestablecerClavePage } from './RestablecerClavePage.tsx';
import { ApiError, HttpUncertainError } from '../api/client.ts';

const mockPost = vi.fn();
const mockLogout = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return {
    ...actual,
    post: (...args: unknown[]) => mockPost(...args),
  };
});

vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual<typeof import('../auth/AuthContext.tsx')>('../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: null, login: vi.fn(), logout: mockLogout, actualizarToken: vi.fn() }) };
});

const TOKEN = 'Ab3_-'.repeat(8) + 'Xy9';
const CLAVE = 'ClaveNueva1!';
const CONSOLA = ['log', 'info', 'warn', 'error', 'debug'] as const;

function renderizar(entrada: string) {
  const router = createMemoryRouter(
    [
      { path: '/restablecer-clave', element: <RestablecerClavePage /> },
      { path: '/login', element: <p>PANTALLA LOGIN</p> },
      { path: '/recuperar-clave', element: <p>PANTALLA RECUPERAR</p> },
    ],
    { initialEntries: [entrada] },
  );
  render(<StrictMode><RouterProvider router={router} /></StrictMode>);
  return router;
}

function contenidoAlmacenado(): string {
  const partes: string[] = [document.cookie];
  for (const almacen of [localStorage, sessionStorage]) {
    for (let i = 0; i < almacen.length; i++) {
      const clave = almacen.key(i) ?? '';
      partes.push(clave, almacen.getItem(clave) ?? '');
    }
  }
  return partes.join('|');
}

async function enviar(user: ReturnType<typeof userEvent.setup>, nueva: string, confirmacion: string) {
  if (nueva) await user.type(screen.getByLabelText('Contraseña nueva'), nueva);
  if (confirmacion) await user.type(screen.getByLabelText('Confirmar contraseña nueva'), confirmacion);
  await user.click(screen.getByRole('button', { name: 'Restablecer contraseña' }));
}

beforeEach(() => {
  mockPost.mockReset();
  mockLogout.mockReset();
  localStorage.clear();
  sessionStorage.clear();
  for (const metodo of CONSOLA) vi.spyOn(console, metodo);
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('RestablecerClavePage (P0-B frontend)', () => {
  it('borra el fragmento al montar y muestra dos campos type="password" con la marca XPAY', async () => {
    const router = renderizar('/restablecer-clave#token=' + TOKEN);
    await waitFor(() => expect(router.state.location.hash).toBe(''));
    expect(router.state.location.pathname).toBe('/restablecer-clave');
    expect((screen.getByLabelText('Contraseña nueva') as HTMLInputElement).type).toBe('password');
    expect((screen.getByLabelText('Confirmar contraseña nueva') as HTMLInputElement).type).toBe('password');
    expect(screen.getByRole('heading', { name: 'XPAY' })).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('campos vacíos: no hay POST', async () => {
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);
    await enviar(user, '', '');
    expect(await screen.findByText('Completa ambos campos.')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('confirmación distinta: mensaje del backend replicado y sin POST', async () => {
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);
    await enviar(user, CLAVE, 'OtraClave1!');
    expect(await screen.findByText('La contraseña nueva y su confirmación no coinciden.')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('política no cumplida: mensaje replicado y sin POST', async () => {
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);
    await enviar(user, 'clavenueva1!', 'clavenueva1!');
    expect(await screen.findByText('La contraseña debe incluir al menos una letra mayúscula.')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('éxito: body exacto, limpia campos, cierra la sesión local y ofrece ir a login', async () => {
    mockPost.mockResolvedValueOnce({ success: true, message: 'Contraseña restablecida. Ya puedes iniciar sesión.' });
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);
    await enviar(user, CLAVE, CLAVE);

    expect(await screen.findByText('Contraseña restablecida. Ya puedes iniciar sesión.')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost).toHaveBeenCalledWith('/api/auth/restablecer-clave', {
      token: TOKEN,
      claveNueva: CLAVE,
      confirmacionClaveNueva: CLAVE,
    });
    expect(mockLogout).toHaveBeenCalledTimes(1);
    expect(screen.queryByLabelText('Contraseña nueva')).not.toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: 'Ir a iniciar sesión' }));
    expect(await screen.findByText('PANTALLA LOGIN')).toBeInTheDocument();
  });

  it('400: mensaje del backend como texto, formulario disponible y reintento con el mismo token', async () => {
    const mensaje = 'La contraseña no puede contener el nombre de usuario. <img src=x onerror=alert(1)>';
    mockPost.mockRejectedValueOnce(new ApiError(mensaje, 400));
    mockPost.mockResolvedValueOnce({ success: true, message: 'Contraseña restablecida. Ya puedes iniciar sesión.' });
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);

    await enviar(user, CLAVE, CLAVE);
    expect(await screen.findByText(mensaje)).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
    expect(mockLogout).not.toHaveBeenCalled();
    expect((screen.getByLabelText('Contraseña nueva') as HTMLInputElement).value).toBe('');
    expect((screen.getByLabelText('Confirmar contraseña nueva') as HTMLInputElement).value).toBe('');

    await enviar(user, 'OtraClave2!', 'OtraClave2!');
    expect(await screen.findByText('Contraseña restablecida. Ya puedes iniciar sesión.')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(2);
    expect(mockPost.mock.calls[1]).toEqual(['/api/auth/restablecer-clave', {
      token: TOKEN,
      claveNueva: 'OtraClave2!',
      confirmacionClaveNueva: 'OtraClave2!',
    }]);
  });

  it('429: mensaje de rate limit en español', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('Too many requests. Please try again later.', 429));
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);
    await enviar(user, CLAVE, CLAVE);
    expect(await screen.findByText('Has realizado varios intentos. Intenta nuevamente más tarde.')).toBeInTheDocument();
  });

  it('timeout: mensaje de incertidumbre', async () => {
    mockPost.mockRejectedValueOnce(new HttpUncertainError('timeout'));
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);
    await enviar(user, CLAVE, CLAVE);
    expect(await screen.findByText(
      'No pudimos confirmar el resultado. Intenta iniciar sesión con tu nueva contraseña; si no puedes, solicita un nuevo enlace.',
    )).toBeInTheDocument();
    expect(mockLogout).not.toHaveBeenCalled();
  });

  it('500: mensaje genérico sin el detalle del backend', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('Error interno restableciendo la contraseña.', 500));
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);
    await enviar(user, CLAVE, CLAVE);
    expect(await screen.findByText(
      'No fue posible restablecer tu contraseña en este momento. Intenta nuevamente más tarde.',
    )).toBeInTheDocument();
    expect(screen.queryByText('Error interno restableciendo la contraseña.')).not.toBeInTheDocument();
  });

  it.each([
    ['sin fragmento', '/restablecer-clave'],
    ['formato inválido (44 caracteres)', '/restablecer-clave#token=' + TOKEN + 'A'],
    ['token en query string', '/restablecer-clave?token=' + TOKEN],
  ])('%s: sin formulario ni POST y con enlace a /recuperar-clave', async (_caso, entrada) => {
    const router = renderizar(entrada);
    expect(screen.getByText('El enlace de recuperación no es válido o está incompleto.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Contraseña nueva')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Solicitar un nuevo enlace' })).toHaveAttribute('href', '/recuperar-clave');
    await waitFor(() => {
      expect(router.state.location.hash).toBe('');
      expect(router.state.location.search).toBe('');
    });
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('el token y la clave nunca llegan a storage, cookie ni consola', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('El enlace de recuperación no es válido o ya fue usado.', 400));
    const user = userEvent.setup();
    renderizar('/restablecer-clave#token=' + TOKEN);
    await enviar(user, CLAVE, CLAVE);
    await screen.findByText('El enlace de recuperación no es válido o ya fue usado.');

    const almacenado = contenidoAlmacenado();
    expect(almacenado).not.toContain(TOKEN);
    expect(almacenado).not.toContain(CLAVE);
    for (const metodo of CONSOLA) {
      const llamadas = vi.mocked(console[metodo]).mock.calls.flat().map(String).join('|');
      expect(llamadas).not.toContain(TOKEN);
      expect(llamadas).not.toContain(CLAVE);
    }
  });
});
