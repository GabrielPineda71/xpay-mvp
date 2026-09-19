// XPAY-401 — Mi información: GET/PATCH /api/usuarios/mi-perfil (XPAY-399,
// backend SIN cambios). CERO red real: api/client.ts completamente
// mockeado.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MiInformacionSection } from './MiInformacionSection.tsx';

const mockGet = vi.fn();
const mockPatch = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    get: (...args: unknown[]) => mockGet(...args),
    patch: (...args: unknown[]) => mockPatch(...args),
  };
});

const PERFIL = {
  usuario: 'qa.usuario1',
  primerNombre: 'Juana', segundoNombre: null, primerApellido: 'Pérez', segundoApellido: null,
  tipoDocumento: 'CC', numeroDocumento: '123456789', fechaNacimiento: '1990-05-14T00:00:00',
  celular: '3001234567', email: 'juana@correo.com',
  direccion: 'Calle 1', ciudad: 'Bogota', departamento: 'Cundinamarca', pais: 'Colombia',
  identidadVerificada: false, estadoKycActual: 'NO_INICIADO',
  emailVerificado: false, celularVerificado: false,
};

beforeEach(() => {
  mockGet.mockReset();
  mockPatch.mockReset();
  mockGet.mockImplementation((path: string) =>
    path === '/api/usuarios/mi-perfil'
      ? Promise.resolve({ success: true, data: PERFIL })
      : Promise.reject(new Error(`unmocked GET ${path}`)));
});

async function renderYEsperarCarga() {
  render(<MiInformacionSection />);
  await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/usuarios/mi-perfil'));
  await screen.findByText('Juana Pérez');
}

describe('MiInformacionSection (XPAY-401)', () => {
  it('consulta GET /api/usuarios/mi-perfil al montar', async () => {
    await renderYEsperarCarga();
  });

  it('muestra identidad legal como texto de solo lectura (sin inputs)', async () => {
    await renderYEsperarCarga();
    expect(screen.getByText('Juana Pérez')).toBeInTheDocument();
    expect(screen.getByText('CC 123456789')).toBeInTheDocument();
    expect(screen.getByText('14/05/1990')).toBeInTheDocument();
    expect(screen.getByText('Colombia')).toBeInTheDocument();
    // Ningún <input> existe para estos campos en ningún momento — no hay
    // modo de edición para identidad legal en este componente.
    expect(screen.queryByLabelText(/primer.?nombre/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/documento/i)).not.toBeInTheDocument();
  });

  it('identidadVerificada=false muestra "Identidad pendiente de verificación"', async () => {
    await renderYEsperarCarga();
    expect(screen.getByText('Identidad pendiente de verificación')).toBeInTheDocument();
    expect(screen.queryByText('Identidad verificada')).not.toBeInTheDocument();
  });

  it('identidadVerificada=true muestra "Identidad verificada"', async () => {
    mockGet.mockImplementation((path: string) =>
      path === '/api/usuarios/mi-perfil'
        ? Promise.resolve({ success: true, data: { ...PERFIL, identidadVerificada: true } })
        : Promise.reject(new Error(`unmocked GET ${path}`)));
    render(<MiInformacionSection />);
    expect(await screen.findByText('Identidad verificada')).toBeInTheDocument();
  });

  it('no muestra "Verificado" cuando el backend devuelve false para email/celular', async () => {
    await renderYEsperarCarga();
    const badges = screen.getAllByText('No verificado');
    expect(badges.length).toBe(2); // email + celular
    expect(screen.queryByText('Verificado')).not.toBeInTheDocument();
  });

  it('"Editar información" muestra inputs SOLO para los 5 campos permitidos', async () => {
    const user = userEvent.setup();
    await renderYEsperarCarga();
    await user.click(screen.getByRole('button', { name: 'Editar información' }));

    expect(screen.getByLabelText('Celular')).toBeInTheDocument();
    expect(screen.getByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByLabelText('Dirección')).toBeInTheDocument();
    expect(screen.getByLabelText('Ciudad')).toBeInTheDocument();
    expect(screen.getByLabelText('Departamento')).toBeInTheDocument();
    // Identidad legal sigue sin ningún input, incluso en modo edición.
    expect(screen.queryByLabelText(/documento/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/nacimiento/i)).not.toBeInTheDocument();
  });

  it('Guardar llama PATCH /api/usuarios/mi-perfil con únicamente los 5 campos editables', async () => {
    const user = userEvent.setup();
    mockPatch.mockResolvedValue({ success: true, message: 'Información actualizada correctamente.', data: { ...PERFIL, ciudad: 'Medellin' } });
    await renderYEsperarCarga();

    await user.click(screen.getByRole('button', { name: 'Editar información' }));
    const ciudad = screen.getByLabelText('Ciudad');
    await user.clear(ciudad);
    await user.type(ciudad, 'Medellin');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(mockPatch).toHaveBeenCalledWith('/api/usuarios/mi-perfil', {
      celular: '3001234567',
      email: 'juana@correo.com',
      direccion: 'Calle 1',
      ciudad: 'Medellin',
      departamento: 'Cundinamarca',
    }));
  });

  it('el body del PATCH nunca contiene IDs ni identidad/KYC', async () => {
    const user = userEvent.setup();
    mockPatch.mockResolvedValue({ success: true, data: PERFIL });
    await renderYEsperarCarga();
    await user.click(screen.getByRole('button', { name: 'Editar información' }));
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(mockPatch).toHaveBeenCalled());
    const bodyEnviado = mockPatch.mock.calls[0][1] as Record<string, unknown>;
    const clavesProhibidas = ['idUsuario', 'idPersona', 'primerNombre', 'primerApellido', 'tipoDocumento', 'numeroDocumento', 'fechaNacimiento', 'identidadVerificada', 'estadoKycActual'];
    for (const clave of clavesProhibidas) {
      expect(bodyEnviado).not.toHaveProperty(clave);
    }
  });

  it('Cancelar restaura los valores cargados y NO llama a PATCH', async () => {
    const user = userEvent.setup();
    await renderYEsperarCarga();
    await user.click(screen.getByRole('button', { name: 'Editar información' }));

    const ciudad = screen.getByLabelText('Ciudad');
    await user.clear(ciudad);
    await user.type(ciudad, 'Un valor que se va a descartar');
    await user.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(mockPatch).not.toHaveBeenCalled();
    // Vuelve a modo lectura mostrando el valor original (no el descartado).
    expect(screen.getByText('Bogota')).toBeInTheDocument();
    expect(screen.queryByText('Un valor que se va a descartar')).not.toBeInTheDocument();
  });

  it('éxito actualiza la UI con la respuesta del PATCH y sale de edición', async () => {
    const user = userEvent.setup();
    mockPatch.mockResolvedValue({
      success: true,
      message: 'Información actualizada correctamente.',
      data: { ...PERFIL, ciudad: 'Cali' },
    });
    await renderYEsperarCarga();
    await user.click(screen.getByRole('button', { name: 'Editar información' }));
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('Información actualizada correctamente.')).toBeInTheDocument();
    expect(screen.getByText('Cali')).toBeInTheDocument();
    expect(screen.queryByLabelText('Ciudad')).not.toBeInTheDocument(); // salió de edición
  });

  it('error del backend mantiene el formulario abierto con los valores escritos', async () => {
    const user = userEvent.setup();
    mockPatch.mockRejectedValue(new Error('El formato del email no es válido.'));
    await renderYEsperarCarga();
    await user.click(screen.getByRole('button', { name: 'Editar información' }));

    const email = screen.getByLabelText('Email');
    await user.clear(email);
    await user.type(email, 'no-es-un-email');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('El formato del email no es válido.')).toBeInTheDocument();
    // Sigue en modo edición, con el valor escrito conservado.
    expect(screen.getByLabelText('Email')).toHaveValue('no-es-un-email');
  });
});
