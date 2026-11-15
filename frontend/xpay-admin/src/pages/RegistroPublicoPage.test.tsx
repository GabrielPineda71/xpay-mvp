import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import * as api from '../api/client.ts';
import { RegistroPublicoPage } from './RegistroPublicoPage.tsx';

const contrasena = 'ClaveSegura1!';

function renderRegistro(ambiente: 'QA' | 'PROD') {
  vi.stubEnv('VITE_XPAY_ENV', ambiente);
  return render(
    <MemoryRouter initialEntries={['/registro']}>
      <Routes>
        <Route path="/registro" element={<RegistroPublicoPage />} />
        <Route path="/login" element={<h1>Inicio de sesión</h1>} />
      </Routes>
    </MemoryRouter>,
  );
}

async function completarFormulario(confirmacion = contrasena) {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText('Usuario'), 'persona.prueba');
  await user.type(screen.getByLabelText('Correo electrónico'), 'persona@example.com');
  await user.type(screen.getByLabelText('Celular'), '3000000000');
  await user.type(screen.getByLabelText('Contraseña', { exact: true }), contrasena);
  await user.type(screen.getByLabelText('Confirmar contraseña'), confirmacion);
  await user.click(screen.getByRole('button', { name: 'Crear cuenta' }));
}

afterEach(() => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe('Registro público de QA', () => {
  it('envía los cuatro datos del contrato y guía la activación por correo', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ success: true });
    renderRegistro('QA');

    await completarFormulario();

    expect(post).toHaveBeenCalledExactlyOnceWith('/api/usuarios/registro', {
      nombreUsuario: 'persona.prueba',
      email: 'persona@example.com',
      celular: '3000000000',
      password: contrasena,
    });
    expect(await screen.findByRole('status')).toHaveTextContent('Revisa tu correo para activar la cuenta.');
    expect(screen.getByRole('link', { name: 'Reenviar correo de verificación' })).toHaveAttribute('href', '/reenviar-verificacion');
    expect(screen.queryByLabelText('Contraseña', { exact: true })).not.toBeInTheDocument();
  });

  it('permite corregir una confirmación distinta antes de enviar', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ success: true });
    renderRegistro('QA');

    await completarFormulario('OtraClave1!');

    expect(screen.getByRole('alert')).toHaveTextContent('no coinciden');
    expect(post).not.toHaveBeenCalled();
  });

  it('redirige a iniciar sesión al abrir la URL directamente en PROD', async () => {
    const post = vi.spyOn(api, 'post');
    renderRegistro('PROD');

    expect(await screen.findByRole('heading', { name: 'Inicio de sesión' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Crear cuenta' })).not.toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
  });
});
