// XPAY-392 — B/C/D/E/F: ProfilePage (ruta mi-wallet/perfil) + BrebMyKeySection.
// CERO red real: api/client.ts está completamente mockeado (vi.mock).
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { ProfilePage } from './ProfilePage.tsx';
import type { AuthUser } from '../auth/AuthContext.tsx';

const mockGet = vi.fn();
const mockPost = vi.fn();
const mockPatch = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return {
    ...actual,
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
    patch: (...args: unknown[]) => mockPatch(...args),
  };
});

const mockUser: AuthUser = {
  idUsuario: 3, idPersona: 3, usuario: 'qa.usuario1', estado: 'ACTIVO',
  roles: [], token: 'synthetic-test-token', requiereCambioClave: false,
};

vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual('../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: mockUser, login: vi.fn(), logout: vi.fn(), actualizarToken: vi.fn() }) };
});

function renderProfilePage(initialPath = '/mi-wallet/perfil') {
  // B — misma ruta declarada en App.tsx (mi-wallet/perfil), aislada aquí
  // sin el resto del árbol de guards (PrivateRoute/RequireView/etc.), que
  // no son objeto de esta prueba.
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/mi-wallet/perfil" element={<ProfilePage />} />
        <Route path="/mi-wallet" element={<div>MI_WALLET_HOME</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

const MI_PERFIL_BASE = {
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
  mockPost.mockReset();
  mockPatch.mockReset();
  mockGet.mockImplementation((path: string) => {
    if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
    if (path === '/api/usuarios/mi-perfil') return Promise.resolve({ success: true, data: MI_PERFIL_BASE });
    return Promise.reject(new Error(`unmocked GET ${path}`));
  });
});

describe('ProfilePage (XPAY-392/401)', () => {
  it('B: la ruta /mi-wallet/perfil renderiza ProfilePage', async () => {
    renderProfilePage();
    expect(screen.getByText('Perfil')).toBeInTheDocument();
  });

  it('C: Perfil muestra la sección "Mi llave Bre-B"', async () => {
    renderProfilePage();
    expect(await screen.findByRole('heading', { name: 'Mi llave Bre-B' })).toBeInTheDocument();
  });

  it('D: al cargar Perfil se consulta GET /api/breb/mi-llave', async () => {
    renderProfilePage();
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/breb/mi-llave'));
  });

  it('E: abrir Perfil NUNCA llama a POST /api/breb/mi-llave/resolver', async () => {
    renderProfilePage();
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/breb/mi-llave'));
    expect(mockPost).not.toHaveBeenCalled();
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave/resolver', expect.anything());
  });

  it('F: registrar la llave usa POST /api/breb/mi-llave (nunca /resolver, nunca Passport)', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({
      success: true,
      data: { idBrebLlave: 1, tipoSujeto: 'USUARIO', keyType: 'ID', keyValueMasked: '***7890', estado: 'PENDIENTE_VALIDACION' },
    });
    renderProfilePage();
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/breb/mi-llave'));

    await user.type(screen.getByLabelText('Valor de la llave'), '1234567890');
    await user.click(screen.getByRole('button', { name: 'Registrar llave' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith(
      '/api/breb/mi-llave',
      { keyType: 'ID', keyValue: '1234567890' },
    ));
    expect(mockPost).not.toHaveBeenCalledWith('/api/breb/mi-llave/resolver', expect.anything());
  });

  // XPAY-401 — las 3 secciones (A/B/C) conviven en la MISMA página, sin
  // crear una segunda ruta de perfil.
  it('XPAY-401: muestra las 3 secciones — Mi información, Seguridad, Mi llave Bre-B', async () => {
    renderProfilePage();
    expect(await screen.findByRole('heading', { name: 'Mi información' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Seguridad' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Mi llave Bre-B' })).toBeInTheDocument();
  });

  it('XPAY-401: al entrar consulta GET /api/usuarios/mi-perfil', async () => {
    renderProfilePage();
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/usuarios/mi-perfil'));
  });

  it('XPAY-401: un fallo en la carga de datos personales no rompe la sección Bre-B', async () => {
    mockGet.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: null });
      if (path === '/api/usuarios/mi-perfil') return Promise.reject(new Error('Error HTTP 500'));
      return Promise.reject(new Error(`unmocked GET ${path}`));
    });
    renderProfilePage();
    expect(await screen.findByRole('heading', { name: 'Mi llave Bre-B' })).toBeInTheDocument();
    expect(screen.getByText('Error HTTP 500')).toBeInTheDocument();
  });

  it('"← Mi Wallet" navega de regreso a /mi-wallet', async () => {
    const user = userEvent.setup();
    renderProfilePage();
    await user.click(screen.getByRole('button', { name: '← Mi Wallet' }));
    expect(await screen.findByText('MI_WALLET_HOME')).toBeInTheDocument();
  });
});
