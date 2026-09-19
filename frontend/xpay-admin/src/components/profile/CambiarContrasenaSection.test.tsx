// XPAY-401/402 — Seguridad: POST /api/auth/cambiar-clave (XPAY-400, backend
// SIN cambios). CERO red real: api/client.ts tiene `post` mockeado, pero
// `ApiError` se importa REAL (vi.importActual la deja pasar sin mockear) —
// necesario para que `err instanceof ApiError` funcione en los tests igual
// que en producción.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CambiarContrasenaSection } from './CambiarContrasenaSection.tsx';
import { ApiError } from '../../api/client.ts';

const mockPost = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    post: (...args: unknown[]) => mockPost(...args),
  };
});

beforeEach(() => {
  mockPost.mockReset();
  localStorage.clear();
  sessionStorage.clear();
});

async function abrirFormulario() {
  const user = userEvent.setup();
  render(<CambiarContrasenaSection />);
  await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));
  return user;
}

describe('CambiarContrasenaSection (XPAY-401/402)', () => {
  it('muestra los 3 campos (actual, nueva, confirmación) como type="password"', async () => {
    await abrirFormulario();
    const actual = screen.getByLabelText('Contraseña actual') as HTMLInputElement;
    const nueva = screen.getByLabelText('Nueva contraseña') as HTMLInputElement;
    const confirmar = screen.getByLabelText('Confirmar nueva contraseña') as HTMLInputElement;

    expect(actual.type).toBe('password');
    expect(nueva.type).toBe('password');
    expect(confirmar.type).toBe('password');
  });

  it('confirmación distinta a la nueva contraseña bloquea el envío (nunca llama POST)', async () => {
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'ClaveVieja1!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'ClaveNueva1!');
    await user.type(screen.getByLabelText('Confirmar nueva contraseña'), 'OtraCosa2!');
    await user.click(screen.getByRole('button', { name: 'Actualizar contraseña' }));

    expect(await screen.findByText('La nueva contraseña y su confirmación no coinciden.')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('campos incompletos bloquean el envío (nunca llama POST)', async () => {
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'ClaveVieja1!');
    // Nueva/confirmación quedan vacías a propósito.
    await user.click(screen.getByRole('button', { name: 'Actualizar contraseña' }));

    expect(await screen.findByText('Completa los tres campos.')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('POST envía ÚNICAMENTE claveActual y claveNueva — nunca la confirmación', async () => {
    mockPost.mockResolvedValue({ success: true, message: 'Contraseña actualizada correctamente.' });
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'ClaveVieja1!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'ClaveNueva1!');
    await user.type(screen.getByLabelText('Confirmar nueva contraseña'), 'ClaveNueva1!');
    await user.click(screen.getByRole('button', { name: 'Actualizar contraseña' }));

    expect(mockPost).toHaveBeenCalledWith('/api/auth/cambiar-clave', {
      claveActual: 'ClaveVieja1!',
      claveNueva: 'ClaveNueva1!',
    });
    const bodyEnviado = mockPost.mock.calls[0][1] as Record<string, unknown>;
    expect(bodyEnviado).not.toHaveProperty('confirmacion');
    expect(bodyEnviado).not.toHaveProperty('confirmacionClaveNueva');
  });

  it('éxito limpia los 3 campos y cierra el formulario', async () => {
    mockPost.mockResolvedValue({ success: true, message: 'Contraseña actualizada correctamente.' });
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'ClaveVieja1!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'ClaveNueva1!');
    await user.type(screen.getByLabelText('Confirmar nueva contraseña'), 'ClaveNueva1!');
    await user.click(screen.getByRole('button', { name: 'Actualizar contraseña' }));

    expect(await screen.findByText('Contraseña actualizada correctamente.')).toBeInTheDocument();
    // El formulario se cerró — los campos ya no existen en el DOM.
    expect(screen.queryByLabelText('Contraseña actual')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cambiar contraseña' })).toBeInTheDocument();
  });

  // XPAY-402 — detección ahora es por ApiError.status === 429, NO por texto.
  it('XPAY-402: ApiError con status 429 muestra el mensaje amigable', async () => {
    mockPost.mockRejectedValue(new ApiError('Too many requests. Please try again later.', 429));
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'ClaveVieja1!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'ClaveNueva1!');
    await user.type(screen.getByLabelText('Confirmar nueva contraseña'), 'ClaveNueva1!');
    await user.click(screen.getByRole('button', { name: 'Actualizar contraseña' }));

    expect(await screen.findByText('Has realizado varios intentos. Intenta nuevamente más tarde.')).toBeInTheDocument();
    expect(screen.queryByText('Too many requests. Please try again later.')).not.toBeInTheDocument();
  });

  // XPAY-402 requisito C — la detección YA NO depende del texto: un error
  // que contenga literalmente "Too many requests" pero NO sea ApiError
  // status 429 (p. ej. un Error de red genérico) NO debe activar el mensaje
  // amigable de rate limit — demuestra que la dependencia textual de
  // XPAY-401 quedó eliminada de verdad, no solo reemplazada por otra
  // coincidencia de texto encubierta.
  it('XPAY-402: un Error genérico con texto similar a 429 NO activa el mensaje amigable (ya no depende del texto)', async () => {
    mockPost.mockRejectedValue(new Error('Too many requests right now, unrelated network issue.'));
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'ClaveVieja1!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'ClaveNueva1!');
    await user.type(screen.getByLabelText('Confirmar nueva contraseña'), 'ClaveNueva1!');
    await user.click(screen.getByRole('button', { name: 'Actualizar contraseña' }));

    expect(await screen.findByText('Too many requests right now, unrelated network issue.')).toBeInTheDocument();
    expect(screen.queryByText('Has realizado varios intentos. Intenta nuevamente más tarde.')).not.toBeInTheDocument();
  });

  it('contraseña actual incorrecta (ApiError 400) muestra el mensaje seguro devuelto por el backend', async () => {
    mockPost.mockRejectedValue(new ApiError('La contraseña actual no coincide.', 400));
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'Incorrecta1!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'ClaveNueva1!');
    await user.type(screen.getByLabelText('Confirmar nueva contraseña'), 'ClaveNueva1!');
    await user.click(screen.getByRole('button', { name: 'Actualizar contraseña' }));

    expect(await screen.findByText('La contraseña actual no coincide.')).toBeInTheDocument();
    expect(screen.queryByText('Has realizado varios intentos. Intenta nuevamente más tarde.')).not.toBeInTheDocument();
  });

  it('nunca persiste la contraseña en localStorage/sessionStorage', async () => {
    mockPost.mockResolvedValue({ success: true, message: 'Contraseña actualizada correctamente.' });
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'ClaveVieja1!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'ClaveNueva1!');
    await user.type(screen.getByLabelText('Confirmar nueva contraseña'), 'ClaveNueva1!');
    await user.click(screen.getByRole('button', { name: 'Actualizar contraseña' }));
    await screen.findByText('Contraseña actualizada correctamente.');

    const todoLocalStorage = JSON.stringify(localStorage);
    const todoSessionStorage = JSON.stringify(sessionStorage);
    expect(todoLocalStorage).not.toContain('ClaveVieja1!');
    expect(todoLocalStorage).not.toContain('ClaveNueva1!');
    expect(todoSessionStorage).not.toContain('ClaveVieja1!');
    expect(todoSessionStorage).not.toContain('ClaveNueva1!');
  });

  it('Cancelar limpia el formulario sin llamar a POST', async () => {
    const user = await abrirFormulario();
    await user.type(screen.getByLabelText('Contraseña actual'), 'algo');
    await user.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(mockPost).not.toHaveBeenCalled();
    expect(screen.queryByLabelText('Contraseña actual')).not.toBeInTheDocument();
  });
});
