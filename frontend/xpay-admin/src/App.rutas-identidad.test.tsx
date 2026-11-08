// P0-B frontend — las cuatro rutas de identidad son PÚBLICAS: con el App
// real (BrowserRouter + AuthProvider) y sin sesión, se renderizan en lugar de
// redirigir a /login. CERO red real: `post` mockeado. Token sintético.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import App from './App.tsx';

const mockPost = vi.fn();

vi.mock('./api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('./api/client.ts')>('./api/client.ts');
  return {
    ...actual,
    post: (...args: unknown[]) => mockPost(...args),
  };
});

const TOKEN = 'Ab3_-'.repeat(8) + 'Xy9';

beforeEach(() => {
  mockPost.mockReset();
  localStorage.clear();
  sessionStorage.clear();
});

describe('App — rutas públicas de identidad (P0-B frontend)', () => {
  it.each([
    ['/verificar-email#token=' + TOKEN, 'Verificación de correo'],
    ['/restablecer-clave#token=' + TOKEN, 'Restablecer contraseña'],
    ['/recuperar-clave', 'Recuperar contraseña'],
    ['/reenviar-verificacion', 'Reenviar verificación de correo'],
  ])('%s se renderiza sin sesión', async (ruta, subtitulo) => {
    window.history.pushState({}, '', ruta);
    render(<App />);
    expect(await screen.findByText(subtitulo, { selector: '.subtitle' })).toBeInTheDocument();
    expect(screen.queryByText('Panel administrativo')).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('con el App real, el fragmento #token desaparece de window.location', async () => {
    window.history.pushState({}, '', '/verificar-email#token=' + TOKEN);
    render(<App />);
    await waitFor(() => expect(window.location.hash).toBe(''));
    expect(window.location.pathname).toBe('/verificar-email');
    expect(window.location.href).not.toContain(TOKEN);
  });
});
