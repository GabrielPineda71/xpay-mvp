// P0-B frontend — /verificar-email. CERO red real: `post` mockeado;
// ApiError/HttpUncertainError reales (importActual). Decisión J1: el POST
// ocurre SOLO por acción explícita, nunca al cargar; nunca dos simultáneos;
// tras un fallo transitorio se permite un nuevo intento explícito; tras el
// éxito o un 400, ninguno más. Token sintético.
import { StrictMode } from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { VerificarEmailPage } from './VerificarEmailPage.tsx';
import { ApiError, HttpUncertainError } from '../api/client.ts';

const mockPost = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return {
    ...actual,
    post: (...args: unknown[]) => mockPost(...args),
  };
});

const TOKEN = 'Ab3_-'.repeat(8) + 'Xy9';
const CONSOLA = ['log', 'info', 'warn', 'error', 'debug'] as const;
const EXITO = 'Correo verificado. Ya puedes iniciar sesión.';
const INVALIDO = 'El enlace de verificación no es válido o ya fue usado.';
const RATE_LIMIT = 'Has realizado varios intentos. Intenta nuevamente más tarde.';
const INCIERTO = 'No pudimos confirmar el resultado. Intenta iniciar sesión; si no puedes, solicita un nuevo enlace.';
const GENERICO = 'No fue posible verificar tu correo en este momento. Intenta nuevamente en unos minutos.';

function renderizar(entrada: string) {
  const router = createMemoryRouter(
    [
      { path: '/verificar-email', element: <VerificarEmailPage /> },
      { path: '/login', element: <p>PANTALLA LOGIN</p> },
      { path: '/reenviar-verificacion', element: <p>PANTALLA REENVIO</p> },
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

function botonConfirmar() {
  return screen.getByRole('button', { name: 'Confirmar mi correo' });
}

function sinBotonesDeEnvio() {
  expect(screen.queryByRole('button', { name: 'Confirmar mi correo' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Intentar nuevamente' })).not.toBeInTheDocument();
}

beforeEach(() => {
  mockPost.mockReset();
  localStorage.clear();
  sessionStorage.clear();
  for (const metodo of CONSOLA) vi.spyOn(console, metodo);
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('VerificarEmailPage (P0-B frontend)', () => {
  it('muestra la marca XPAY (no "XPAY Admin")', () => {
    renderizar('/verificar-email#token=' + TOKEN);
    expect(screen.getByRole('heading', { name: 'XPAY' })).toBeInTheDocument();
    expect(screen.queryByText('XPAY Admin')).not.toBeInTheDocument();
  });

  it('captura el token y borra el fragmento de la URL al montar', async () => {
    const router = renderizar('/verificar-email#token=' + TOKEN);
    await waitFor(() => expect(router.state.location.hash).toBe(''));
    expect(router.state.location.pathname).toBe('/verificar-email');
    expect(router.state.location.search).toBe('');
    expect(botonConfirmar()).toBeInTheDocument();
  });

  it('no hace POST al cargar: montaje = 0 POST, solo con el clic explícito', async () => {
    const router = renderizar('/verificar-email#token=' + TOKEN);
    await waitFor(() => expect(router.state.location.hash).toBe(''));
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('StrictMode (doble montaje y doble efecto) no produce POST adicional', async () => {
    mockPost.mockResolvedValueOnce({ success: true, message: EXITO });
    const user = userEvent.setup();
    const router = renderizar('/verificar-email#token=' + TOKEN);
    await waitFor(() => expect(router.state.location.hash).toBe(''));
    expect(mockPost).toHaveBeenCalledTimes(0);
    await user.click(botonConfirmar());
    await screen.findByText(EXITO);
    expect(mockPost).toHaveBeenCalledTimes(1);
  });

  it('el clic hace un único POST con body { token } y muestra el éxito', async () => {
    mockPost.mockResolvedValueOnce({ success: true, message: EXITO });
    const user = userEvent.setup();
    renderizar('/verificar-email#token=' + TOKEN);
    await user.click(botonConfirmar());

    expect(await screen.findByText(EXITO)).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(mockPost).toHaveBeenCalledWith('/api/usuarios/verificar-email', { token: TOKEN });
  });

  it('doble clic mientras está enviando no duplica el POST', async () => {
    mockPost.mockReturnValue(new Promise(() => {}));
    const user = userEvent.setup();
    renderizar('/verificar-email#token=' + TOKEN);
    await user.dblClick(botonConfirmar());
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Verificando...' })).toBeDisabled();
  });

  it('después del éxito no se puede volver a enviar', async () => {
    mockPost.mockResolvedValueOnce({ success: true, message: EXITO });
    const user = userEvent.setup();
    renderizar('/verificar-email#token=' + TOKEN);
    await user.click(botonConfirmar());
    await screen.findByText(EXITO);
    sinBotonesDeEnvio();
    expect(mockPost).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole('link', { name: 'Ir a iniciar sesión' }));
    expect(await screen.findByText('PANTALLA LOGIN')).toBeInTheDocument();
  });

  it('400: mensaje del backend como texto (nunca HTML), sin reintento y con nuevo enlace', async () => {
    const mensaje = INVALIDO + ' <img src=x onerror=alert(1)>';
    mockPost.mockRejectedValueOnce(new ApiError(mensaje, 400));
    const user = userEvent.setup();
    renderizar('/verificar-email#token=' + TOKEN);
    await user.click(botonConfirmar());

    expect(await screen.findByText(mensaje)).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
    sinBotonesDeEnvio();
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('link', { name: 'Solicitar un nuevo enlace' })).toHaveAttribute('href', '/reenviar-verificacion');
  });

  it.each([
    ['429', () => new ApiError('Too many requests. Please try again later.', 429), RATE_LIMIT],
    ['timeout incierto', () => new HttpUncertainError('timeout'), INCIERTO],
    ['500', () => new ApiError('Error interno verificando el correo.', 500), GENERICO],
    ['error de red', () => new Error('No fue posible conectar con el backend XPAY.'), GENERICO],
  ])('después de %s se puede hacer un nuevo intento explícito con el mismo token', async (_caso, crearError, texto) => {
    mockPost.mockRejectedValueOnce(crearError());
    mockPost.mockResolvedValueOnce({ success: true, message: EXITO });
    const user = userEvent.setup();
    const router = renderizar('/verificar-email#token=' + TOKEN);
    await user.click(botonConfirmar());

    expect(await screen.findByText(texto)).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Error interno verificando el correo.')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Intentar nuevamente' }));
    expect(await screen.findByText(EXITO)).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(2);
    expect(mockPost.mock.calls[1]).toEqual(['/api/usuarios/verificar-email', { token: TOKEN }]);
    sinBotonesDeEnvio();
    expect(router.state.location.hash).toBe('');
    expect(router.state.location.search).toBe('');
  });

  it('doble clic en "Intentar nuevamente" mientras está enviando = un solo POST nuevo', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('Error interno verificando el correo.', 500));
    mockPost.mockReturnValueOnce(new Promise(() => {}));
    const user = userEvent.setup();
    renderizar('/verificar-email#token=' + TOKEN);
    await user.click(botonConfirmar());
    await screen.findByText(GENERICO);

    await user.dblClick(screen.getByRole('button', { name: 'Intentar nuevamente' }));
    expect(mockPost).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('button', { name: 'Verificando...' })).toBeDisabled();
  });

  it.each([
    ['sin fragmento', '/verificar-email'],
    ['formato inválido (42 caracteres)', '/verificar-email#token=' + TOKEN.slice(1)],
    ['token en query string', '/verificar-email?token=' + TOKEN],
  ])('%s: no hay botón ni POST y la URL queda limpia', async (_caso, entrada) => {
    const router = renderizar(entrada);
    expect(screen.getByText('El enlace de verificación no es válido o está incompleto.')).toBeInTheDocument();
    sinBotonesDeEnvio();
    await waitFor(() => {
      expect(router.state.location.hash).toBe('');
      expect(router.state.location.search).toBe('');
    });
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('el token nunca llega a storage, cookie, consola ni query, tampoco tras un reintento', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('Too many requests. Please try again later.', 429));
    mockPost.mockRejectedValueOnce(new ApiError(INVALIDO, 400));
    const user = userEvent.setup();
    const router = renderizar('/verificar-email#token=' + TOKEN);
    await user.click(botonConfirmar());
    await screen.findByText(RATE_LIMIT);
    await user.click(screen.getByRole('button', { name: 'Intentar nuevamente' }));
    await screen.findByText(INVALIDO);

    expect(mockPost).toHaveBeenCalledTimes(2);
    expect(contenidoAlmacenado()).not.toContain(TOKEN);
    expect(router.state.location.search).not.toContain(TOKEN);
    expect(router.state.location.hash).not.toContain(TOKEN);
    for (const metodo of CONSOLA) {
      const llamadas = vi.mocked(console[metodo]).mock.calls.flat().map(String).join('|');
      expect(llamadas).not.toContain(TOKEN);
    }
  });
});
