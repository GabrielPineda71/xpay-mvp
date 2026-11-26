import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError, HttpUncertainError } from '../../api/client.ts';
import type { MiPerfil } from '../../hooks/useMiPerfil.ts';
import { CompletarPrimerApellidoSection } from './CompletarPrimerApellidoSection.tsx';

const post = vi.fn();
vi.mock('../../api/client.ts', async () => ({
  ...await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts'),
  post: (...args: unknown[]) => post(...args),
}));
const perfil: MiPerfil = {
  usuario: 'persona.sintetica', primerNombre: null, segundoNombre: null, primerApellido: null, segundoApellido: null,
  tipoDocumento: null, numeroDocumento: null, fechaNacimiento: null, celular: '3000000000', email: 'persona@example.test',
  direccion: null, ciudad: null, departamento: null, pais: 'Colombia', identidadVerificada: true,
  nombreVerificado: 'PERSONA GARCÍA DE LA HOZ', estadoKycActual: 'APROBADO', emailVerificado: true,
  celularVerificado: false, puedeCompletarPrimerApellido: true,
};
beforeEach(() => { post.mockReset(); });

describe('Completar primer apellido faltante', () => {
  it.each([
    { puedeCompletarPrimerApellido: false }, { primerApellido: 'García' }, { emailVerificado: false },
    { identidadVerificada: false }, { estadoKycActual: 'PENDIENTE' },
  ])('no ofrece completar cuando falta habilitación o el dato ya existe (%j)', cambio => {
    render(<CompletarPrimerApellidoSection perfil={{ ...perfil, ...cambio }} onActualizado={vi.fn()} />);
    expect(screen.queryByLabelText('Primer apellido')).not.toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
  });

  it('solicita el dato vacío sin inferirlo y envía solo la declaración explícita', async () => {
    const actualizar = vi.fn();
    post.mockResolvedValue({ success: true, data: { ...perfil, primerApellido: 'García de la Hoz', puedeCompletarPrimerApellido: false } });
    render(<CompletarPrimerApellidoSection perfil={perfil} onActualizado={actualizar} />);
    expect(screen.getByLabelText('Primer apellido')).toHaveValue('');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Primer apellido'), '  Garci\u0301a de la Hoz  ');
    await user.click(screen.getByRole('button', { name: 'Guardar primer apellido' }));
    expect(post).toHaveBeenCalledExactlyOnceWith('/api/usuarios/mi-perfil/primer-apellido', { primerApellido: 'García de la Hoz' });
    expect(actualizar).toHaveBeenCalledExactlyOnceWith();
    expect(await screen.findByRole('status')).toHaveTextContent('Primer apellido guardado.');
  });

  it('permite corregir una discordancia conocida, sin alterar el nombre por su cuenta', async () => {
    post.mockRejectedValue(new ApiError('El primer apellido no coincide con la identidad verificada.', 400));
    render(<CompletarPrimerApellidoSection perfil={perfil} onActualizado={vi.fn()} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Primer apellido'), 'Otra declaración');
    await user.click(screen.getByRole('button', { name: 'Guardar primer apellido' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('no coincide');
    expect(screen.getByLabelText('Primer apellido')).toHaveValue('Otra declaración');
    expect(screen.getByLabelText('Primer apellido')).not.toBeDisabled();
  });

  it('un resultado incierto exige actualizar antes de volver a enviar', async () => {
    post.mockRejectedValue(new HttpUncertainError('Timeout sintético'));
    const actualizar = vi.fn();
    render(<CompletarPrimerApellidoSection perfil={perfil} onActualizado={actualizar} />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Primer apellido'), 'García');
    await user.dblClick(screen.getByRole('button', { name: 'Guardar primer apellido' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar');
    expect(post).toHaveBeenCalledTimes(1);
    expect(screen.getByLabelText('Primer apellido')).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Actualizar perfil' }));
    await waitFor(() => expect(actualizar).toHaveBeenCalledTimes(1));
  });
});
