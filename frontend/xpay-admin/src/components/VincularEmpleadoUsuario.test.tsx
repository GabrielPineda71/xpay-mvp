// D2 — vínculo empleado ↔ usuario desde la pantalla de convenios. `post` mockeado.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { VincularEmpleadoUsuario, AYUDA_VINCULO } from './VincularEmpleadoUsuario.tsx';
import { ApiError } from '../api/client.ts';

const mockPost = vi.fn();
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, post: (...args: unknown[]) => mockPost(...args) };
});

const base = { idEmpleado: 11, idConvenio: 3, estadoEmpleado: 'ACTIVO', idUsuarioVinculado: null };

describe('VincularEmpleadoUsuario', () => {
  beforeEach(() => mockPost.mockReset());

  it('muestra el usuario vinculado y no ofrece re-vincular', () => {
    render(<VincularEmpleadoUsuario {...base} idUsuarioVinculado={42} onVinculado={vi.fn()} />);
    expect(screen.getByText('Usuario #42')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Vincular usuario' })).not.toBeInTheDocument();
  });

  it('empleado inactivo: solo estado, sin acción', () => {
    render(<VincularEmpleadoUsuario {...base} estadoEmpleado="INACTIVO" onVinculado={vi.fn()} />);
    expect(screen.getByText('Sin vincular')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Vincular usuario' })).not.toBeInTheDocument();
  });

  it('valida el ID antes de llamar al backend', async () => {
    const user = userEvent.setup();
    render(<VincularEmpleadoUsuario {...base} onVinculado={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Vincular usuario' }));
    await user.click(screen.getByRole('button', { name: 'Confirmar vínculo' }));
    expect(screen.getByRole('alert')).toHaveTextContent('ID de usuario válido');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('envía empleado, usuario y convenio esperado, y avisa al terminar', async () => {
    mockPost.mockResolvedValue({ success: true });
    const onVinculado = vi.fn();
    const user = userEvent.setup();
    render(<VincularEmpleadoUsuario {...base} onVinculado={onVinculado} />);
    await user.click(screen.getByRole('button', { name: 'Vincular usuario' }));
    expect(screen.getByText(AYUDA_VINCULO)).toBeInTheDocument();
    await user.type(screen.getByLabelText('ID del usuario para el empleado 11'), '42');
    await user.click(screen.getByRole('button', { name: 'Confirmar vínculo' }));
    await waitFor(() => expect(onVinculado).toHaveBeenCalledTimes(1));
    expect(mockPost).toHaveBeenCalledWith('/api/libranza/admin/empleados/11/asociar-usuario', { idUsuario: 42, idConvenio: 3 });
  });

  it('muestra el rechazo del backend (documento no coincide)', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('El documento del empleado no corresponde al usuario indicado.', 400));
    const onVinculado = vi.fn();
    const user = userEvent.setup();
    render(<VincularEmpleadoUsuario {...base} onVinculado={onVinculado} />);
    await user.click(screen.getByRole('button', { name: 'Vincular usuario' }));
    await user.type(screen.getByLabelText('ID del usuario para el empleado 11'), '42');
    await user.click(screen.getByRole('button', { name: 'Confirmar vínculo' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('no corresponde al usuario');
    expect(onVinculado).not.toHaveBeenCalled();
  });
});
