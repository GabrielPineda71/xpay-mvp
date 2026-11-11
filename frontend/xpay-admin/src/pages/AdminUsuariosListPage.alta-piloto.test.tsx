// D1 — el formulario de alta del piloto solo aparece para SUPERUSUARIO y,
// tras el alta, enlaza al detalle donde se emite la clave temporal.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { AdminUsuariosListPage } from './AdminUsuariosListPage.tsx';

const mockGet = vi.fn();
let roles: string[] = [];

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...args: unknown[]) => mockGet(...args) };
});

vi.mock('../components/AltaUsuarioPilotoForm.tsx', () => ({
  AltaUsuarioPilotoForm: ({ onCreado }: { onCreado: (r: unknown) => void }) => (
    <button type="button" onClick={() => onCreado({ idUsuario: 42, idWallet: 5, siguiente: '' })}>FORM ALTA</button>
  ),
}));

vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual<typeof import('../auth/AuthContext.tsx')>('../auth/AuthContext.tsx');
  return {
    ...actual,
    useAuth: () => ({
      user: { idUsuario: 1, idPersona: 1, usuario: 'admin', estado: 'ACTIVO', roles, token: 't', requiereCambioClave: false },
      login: vi.fn(), logout: vi.fn(), actualizarToken: vi.fn(),
    }),
  };
});

function renderizar() {
  const router = createMemoryRouter(
    [
      { path: '/admin/usuarios', element: <AdminUsuariosListPage /> },
      { path: '/admin/usuarios/:id', element: <p>DETALLE USUARIO</p> },
    ],
    { initialEntries: ['/admin/usuarios'] },
  );
  render(<RouterProvider router={router} />);
  return router;
}

describe('AdminUsuariosListPage — alta del piloto', () => {
  beforeEach(() => {
    mockGet.mockReset();
    mockGet.mockResolvedValue({ success: true, data: { items: [], total: 0, page: 1, pageSize: 20 } });
  });

  it('ADMIN_XPAY sin SUPERUSUARIO no ve el alta', async () => {
    roles = ['ADMIN_XPAY'];
    renderizar();
    await screen.findByText('Sin resultados para los filtros aplicados.');
    expect(screen.queryByRole('button', { name: 'Alta de usuario del piloto' })).not.toBeInTheDocument();
  });

  it('SUPERUSUARIO crea el usuario y llega al detalle para emitir la clave', async () => {
    roles = ['SUPERUSUARIO'];
    const user = userEvent.setup();
    const router = renderizar();
    await user.click(await screen.findByRole('button', { name: 'Alta de usuario del piloto' }));
    await user.click(screen.getByRole('button', { name: 'FORM ALTA' }));

    expect(screen.getByRole('status')).toHaveTextContent('Usuario #42 creado con wallet #5');
    expect(mockGet).toHaveBeenCalledTimes(2); // la lista se recarga tras el alta
    await user.click(screen.getByRole('button', { name: 'Ir al detalle para emitir la clave temporal' }));
    expect(router.state.location.pathname).toBe('/admin/usuarios/42');
    expect(await screen.findByText('DETALLE USUARIO')).toBeInTheDocument();
  });
});
