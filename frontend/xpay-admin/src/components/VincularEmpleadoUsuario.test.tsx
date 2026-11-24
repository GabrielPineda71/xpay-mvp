import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { VincularEmpleadoUsuario } from './VincularEmpleadoUsuario.tsx';

const mockGet = vi.fn(); const mockPost = vi.fn();
vi.mock('../api/client.ts', () => ({ get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) }));
const base = { idConvenio: 3, estadoEmpleado: 'ACTIVO', idUsuarioVinculado: null };
function mostrar(props = base) { return render(<MemoryRouter><VincularEmpleadoUsuario {...props} /></MemoryRouter>); }

describe('Acceso a la vinculación por documento', () => {
  it('lleva al convenio correcto sin pedir ni consultar IDs de usuario', () => {
    mostrar();
    expect(screen.getByRole('link', { name: 'Revisar coincidencias' })).toHaveAttribute('href', '/admin/nomina-vinculaciones?idConvenio=3');
    expect(screen.getByText('Sin vincular')).toBeInTheDocument();
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalled(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('muestra el vínculo existente sin identificadores internos ni re-vincular', () => {
    render(<MemoryRouter><VincularEmpleadoUsuario {...base} idUsuarioVinculado={42} /></MemoryRouter>);
    expect(screen.getByText('Vinculado')).toBeInTheDocument();
    expect(screen.queryByText(/42/)).not.toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
  it('no ofrece vincular un empleado inactivo', () => {
    mostrar({ ...base, estadoEmpleado: 'INACTIVO' });
    expect(screen.getByText('Sin vincular')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});
