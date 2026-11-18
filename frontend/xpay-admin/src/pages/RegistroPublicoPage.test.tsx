import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../api/client.ts';
import { RegistroPublicoPage } from './RegistroPublicoPage.tsx';

const contrasena = 'ClaveSegura1!';
const configuracion = {
  data: {
    terminos: { version: '1.1', fecha: '2026-11-13', url: 'https://app-qa.xpaycol.com/legal/terminos.pdf' },
    politicaDatos: { version: '1.1', fecha: '2026-11-13', url: 'https://app-qa.xpaycol.com/legal/politica.pdf' },
  },
};

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

async function completarFormulario(confirmacion = contrasena, consentimientos = true) {
  const user = userEvent.setup();
  await user.type(await screen.findByLabelText('Usuario'), 'persona.prueba');
  await user.type(screen.getByLabelText('Correo electrónico'), 'persona@example.com');
  await user.type(screen.getByLabelText('Celular'), '3000000000');
  await user.type(screen.getByLabelText('Contraseña', { exact: true }), contrasena);
  await user.type(screen.getByLabelText('Confirmar contraseña'), confirmacion);
  if (consentimientos) {
    await user.click(screen.getByRole('checkbox', { name: /Declaro que tengo dieciocho/ }));
    await user.click(screen.getByRole('checkbox', { name: /He leído la Política/ }));
  }
  await user.click(screen.getByRole('button', { name: 'Crear cuenta' }));
}

beforeEach(() => {
  vi.spyOn(api, 'get').mockResolvedValue(configuracion);
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe('Registro público', () => {
  it.each(['QA', 'PROD'] as const)('en %s envía los datos y las dos aceptaciones con sus versiones y guía la activación por correo', async ambiente => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ success: true });
    renderRegistro(ambiente);

    await completarFormulario();

    expect(post).toHaveBeenCalledExactlyOnceWith('/api/usuarios/registro', {
      nombreUsuario: 'persona.prueba',
      email: 'persona@example.com',
      celular: '3000000000',
      password: contrasena,
      consentimiento: {
        aceptaTerminos: true, versionTerminos: '1.1',
        autorizaTratamientoDatos: true, versionPoliticaDatos: '1.1',
      },
    });
    expect(await screen.findByRole('status')).toHaveTextContent('Revisa tu correo para activar la cuenta.');
    expect(screen.getByRole('link', { name: 'Reenviar correo de verificación' })).toHaveAttribute('href', '/reenviar-verificacion');
    expect(screen.queryByLabelText('Contraseña', { exact: true })).not.toBeInTheDocument();
  });

  it('muestra los documentos vigentes y exige ambas casillas desmarcadas por defecto', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ success: true });
    renderRegistro('QA');

    const terminos = await screen.findByRole('link', { name: 'Términos y Condiciones' });
    const politica = screen.getByRole('link', { name: 'Política de Tratamiento de Datos Personales' });
    expect(terminos).toHaveAttribute('href', configuracion.data.terminos.url);
    expect(politica).toHaveAttribute('href', configuracion.data.politicaDatos.url);
    expect(screen.getAllByText('13 de noviembre de 2026')).toHaveLength(2);
    const casillas = screen.getAllByRole('checkbox');
    expect(casillas).toHaveLength(2);
    casillas.forEach(casilla => expect(casilla).not.toBeChecked());

    await userEvent.click(casillas[0]);
    await completarFormulario(contrasena, false);

    expect(post).not.toHaveBeenCalled();
  });

  it('permite corregir una confirmación distinta antes de enviar', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ success: true });
    renderRegistro('QA');

    await completarFormulario('OtraClave1!');

    expect(screen.getByRole('alert')).toHaveTextContent('no coinciden');
    expect(post).not.toHaveBeenCalled();
  });

  it('permite el registro habilitado por la API en PROD e informa el alcance del acceso', async () => {
    const post = vi.spyOn(api, 'post');
    renderRegistro('PROD');

    expect(await screen.findByLabelText('Usuario')).toBeInTheDocument();
    expect(screen.getByText(/Confirma tu correo para activar tu cuenta y entrar a tu perfil/)).toHaveTextContent(/verificaciones adicionales y una habilitación posterior/);
    expect(screen.queryByText(/Ambiente de pruebas/)).not.toBeInTheDocument();
    expect(api.get).toHaveBeenCalledWith('/api/usuarios/registro/configuracion');
    expect(post).not.toHaveBeenCalled();
  });

  it.each(['QA', 'PROD'] as const)('en %s cierra el formulario si la API no habilita el registro', async ambiente => {
    vi.mocked(api.get).mockRejectedValue(new api.ApiError('No encontrado.', 404));
    const post = vi.spyOn(api, 'post');
    renderRegistro(ambiente);

    expect(await screen.findByText('El registro no está disponible en este momento.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Crear cuenta' })).not.toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
  });
});
