import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiError, HttpUncertainError } from '../../api/client.ts';
import { PreparacionWalletSection } from './PreparacionWalletSection.tsx';
import { TEXTO_TERMINOS_WALLET, TEXTO_DATOS_WALLET, type EstadoPreparacionWallet } from './preparacionWallet.ts';

const mockGet = vi.fn(), mockPost = vi.fn(), mockPatch = vi.fn(), mockPut = vi.fn();
const actualizarSesion = vi.fn(), logout = vi.fn();
const titular = { idUsuario: 99, idPersona: 199, usuario: 'persona.sintetica', estado: 'ACTIVO', roles: ['USUARIO_FINAL'], token: 'test-anterior', requiereCambioClave: false, requierePerfil: true };
vi.mock('../../api/client.ts', async () => ({ ...await vi.importActual('../../api/client.ts'), get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a), patch: (...a: unknown[]) => mockPatch(...a), put: (...a: unknown[]) => mockPut(...a) }));
vi.mock('../../auth/AuthContext.tsx', async () => ({ ...await vi.importActual('../../auth/AuthContext.tsx'), useAuth: () => ({ user: titular, actualizarSesion, logout }) }));
vi.mock('./PinTransaccionalSection.tsx', () => ({ PinTransaccionalSection: ({ onActualizado }: { onActualizado: () => void }) => <button onClick={onActualizado}>PIN guardado por servidor</button> }));
const BASE: EstadoPreparacionWallet = {
  habilitado: true, esRegistroPublico: true, emailVerificado: true, estadoKyc: 'APROBADO', identidadVerificada: true,
  perfilCompleto: true, direccion: 'Calle de pruebas 1', idDepartamento: 1, idCiudad: 10, departamento: 'Departamento prueba', ciudad: 'Ciudad prueba',
  pin: { configurado: true, bloqueado: false, longitudActual: 6, longitudRequerida: 6 }, walletHabilitada: false, siguientePaso: 'ACTIVAR', puedeActivar: true,
  terminos: { version: '1.3', fecha: '2026-11-21', url: 'https://app.xpaycol.com/legal/v1.3/terminos.pdf', sha256: 'a'.repeat(64), aceptados: true },
  politicaDatos: { version: '1.3', fecha: '2026-11-21', url: 'https://app.xpaycol.com/legal/v1.3/politica.pdf', sha256: 'b'.repeat(64), aceptados: true },
};
function montar() { return render(<MemoryRouter initialEntries={['/mi-wallet/perfil']}><Routes><Route path="/mi-wallet/perfil" element={<PreparacionWalletSection fallback={<p>PERFIL ANTERIOR</p>} />} /><Route path="/mi-wallet" element={<p>WALLET CONFIRMADA</p>} /><Route path="/login" element={<p>INGRESO SEGURO</p>} /></Routes></MemoryRouter>); }
function responder(estado: Partial<EstadoPreparacionWallet> = {}) { mockGet.mockResolvedValue({ success: true, data: { ...BASE, ...estado } }); }
async function activar() { await userEvent.click(await screen.findByRole('button', { name: 'Activar mi Wallet' })); }
beforeEach(() => { vi.clearAllMocks(); mockGet.mockReset(); mockPost.mockReset(); mockPatch.mockReset(); mockPut.mockReset(); responder(); });

describe('Preparación de Wallet: cinco pasos y aceptaciones vigentes del servidor', () => {
  it('no pide otra aceptación a un nuevo usuario ni registra llave ni activa al entrar', async () => {
    montar(); expect(await screen.findByRole('button', { name: 'Activar mi Wallet' })).toBeEnabled();
    expect(screen.getByText('PASO 5 DE 5')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    expect(screen.queryByText(/piloto|WhatsApp|próxima etapa|Agrega tu llave|Condiciones de habilitación/i)).not.toBeInTheDocument();
    expect(mockGet).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet');
    expect(mockPost).not.toHaveBeenCalled(); expect(mockPut).not.toHaveBeenCalled(); expect(actualizarSesion).not.toHaveBeenCalled();
  });
  it('activa manualmente sin volver a aceptar A/B vigentes y adopta sesión del mismo titular', async () => {
    const sesion = { ...titular, token: 'test-nueva', requierePerfil: false }; mockPost.mockResolvedValue({ success: true, data: sesion });
    montar(); await activar(); expect(await screen.findByText('WALLET CONFIRMADA')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/activar', { aceptaTerminos: false, autorizaDatos: false });
    expect(actualizarSesion).toHaveBeenCalledExactlyOnceWith(sesion); expect(titular.requierePerfil).toBe(true);
  });
  it('solo a usuarios con A/B anteriores les pide ambas actualizaciones sin preselección', async () => {
    responder({ terminos: { ...BASE.terminos!, aceptados: false }, politicaDatos: { ...BASE.politicaDatos!, aceptados: false } });
    mockPost.mockResolvedValue({ success: true, data: { ...titular, requierePerfil: false } }); montar();
    const t = await screen.findByRole('checkbox', { name: TEXTO_TERMINOS_WALLET }); const p = screen.getByRole('checkbox', { name: TEXTO_DATOS_WALLET });
    expect(t).not.toBeChecked(); expect(p).not.toBeChecked(); expect(screen.getByRole('button', { name: 'Activar mi Wallet' })).toBeDisabled();
    expect(screen.getByRole('link', { name: 'Leer Términos y Condiciones · 1.3' })).toHaveAttribute('href', BASE.terminos!.url);
    expect(screen.getByRole('link', { name: 'Leer Política de Tratamiento de Datos Personales · 1.3' })).toHaveAttribute('href', BASE.politicaDatos!.url);
    await userEvent.click(t); expect(screen.getByRole('button', { name: 'Activar mi Wallet' })).toBeDisabled();
    await userEvent.click(p); await activar();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/activar', { aceptaTerminos: true, versionTerminos: '1.3', autorizaDatos: true, versionPoliticaDatos: '1.3' });
  });
  it('solo pide la categoría pendiente, sin una autorización nueva de biometría', async () => {
    responder({ politicaDatos: { ...BASE.politicaDatos!, aceptados: false } }); montar();
    await userEvent.click(await screen.findByRole('checkbox', { name: TEXTO_DATOS_WALLET }));
    expect(screen.queryByRole('checkbox', { name: TEXTO_TERMINOS_WALLET })).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /biometr/i })).not.toBeInTheDocument();
    mockPost.mockResolvedValue({ success: true, data: { ...titular, requierePerfil: false } }); await activar();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/activar', { aceptaTerminos: false, autorizaDatos: true, versionPoliticaDatos: '1.3' });
  });
  it('guardar PIN solo consulta el avance; el siguiente paso no exige llave propia', async () => {
    responder({ siguientePaso: 'PIN', puedeActivar: false, pin: { configurado: false, bloqueado: false, longitudRequerida: 6 } }); montar();
    const boton = await screen.findByRole('button', { name: 'PIN guardado por servidor' }); responder(); await userEvent.click(boton);
    expect(await screen.findByRole('button', { name: 'Activar mi Wallet' })).toBeEnabled(); expect(mockPost).not.toHaveBeenCalled(); expect(mockPut).not.toHaveBeenCalled();
    expect(mockGet).toHaveBeenCalledTimes(2); expect(screen.queryByText(/Llave Bre-B/)).not.toBeInTheDocument();
  });
  it('guardar domicilio avanza directamente al PIN usando la respuesta del servidor, sin paso de llave', async () => {
    mockGet.mockImplementation((path: string) => Promise.resolve({ success: true, data: path === '/api/catalogos/paises' ? [{ idPais: 1, codigo: 'COL' }]
      : path.startsWith('/api/catalogos/departamentos') ? [{ idDepartamento: 1, nombre: 'Departamento prueba' }]
      : path.startsWith('/api/catalogos/ciudades') ? [{ idCiudad: 10, nombre: 'Ciudad prueba' }]
      : { ...BASE, siguientePaso: 'DATOS', perfilCompleto: false, direccion: null, idDepartamento: null, idCiudad: null, puedeActivar: false } }));
    mockPatch.mockResolvedValue({ success: true, data: { ...BASE, siguientePaso: 'PIN', puedeActivar: false, pin: { configurado: false, bloqueado: false, longitudRequerida: 6 } } }); montar();
    await userEvent.type(await screen.findByLabelText('Dirección de residencia'), 'Calle de prueba 1');
    await screen.findByRole('option', { name: 'Departamento prueba' }); await userEvent.selectOptions(screen.getByLabelText('Departamento'), '1');
    await screen.findByRole('option', { name: 'Ciudad prueba' }); await userEvent.selectOptions(screen.getByLabelText('Ciudad o municipio'), '10');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar y continuar' }));
    expect(await screen.findByRole('button', { name: 'PIN guardado por servidor' })).toBeInTheDocument();
    expect(screen.getByText('PASO 4 DE 5')).toBeInTheDocument();
    expect(mockPatch).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/datos', { direccion: 'Calle de prueba 1', idDepartamento: 1, idCiudad: 10 });
    expect(mockPost).not.toHaveBeenCalled(); expect(mockPut).not.toHaveBeenCalled();
  });
  it('ofrece Perfil y configuración sin biometría y solo consulta al desplegarlo', async () => {
    const perfil = { celular: '3000000001', email: 'persona@example.test', pais: 'Colombia', identidadVerificada: false, emailVerificado: true, celularVerificado: false };
    mockGet.mockImplementation((path: string) => Promise.resolve({ success: true, data: path === '/api/usuarios/mi-perfil' ? perfil : path === '/api/contactos/estado'
      ? { emailVerificado: true, celularVerificado: false, requeridoParaOperar: false, servicioWhatsApp: false, publicidadEmail: false, publicidadWhatsApp: false }
      : { ...BASE, siguientePaso: 'REVISION', identidadVerificada: false, puedeActivar: false } }));
    montar(); await screen.findByRole('heading', { name: 'Estamos revisando tu registro' }); expect(mockGet).toHaveBeenCalledTimes(1);
    await userEvent.click(screen.getByText('Mi perfil y configuración'));
    expect(await screen.findByRole('button', { name: 'Editar información' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Comunicaciones' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Seguridad' })).toBeInTheDocument();
    expect(screen.queryByText(/próxima etapa|piloto|WhatsApp pendiente/i)).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Ofertas y novedades de XPAY por WhatsApp' })).not.toBeChecked();
    expect(mockPost).not.toHaveBeenCalled(); expect(mockPatch).not.toHaveBeenCalled(); expect(mockPut).not.toHaveBeenCalled();
  });
  it('una sesión de otro titular exige reingreso y nunca cambia privilegios', async () => {
    mockPost.mockResolvedValue({ success: true, data: { ...titular, idPersona: 299, requierePerfil: false } }); montar(); await activar();
    await userEvent.click(await screen.findByRole('button', { name: 'Volver a ingresar' }));
    expect(await screen.findByText('INGRESO SEGURO')).toBeInTheDocument(); expect(logout).toHaveBeenCalledTimes(1); expect(actualizarSesion).not.toHaveBeenCalled();
  });
  it('no repite activación ante incertidumbre ni al recibir foco', async () => {
    mockPost.mockRejectedValue(new HttpUncertainError('Espera agotada.')); montar(); await activar();
    await screen.findByRole('button', { name: 'Volver a ingresar' }); window.dispatchEvent(new Event('focus'));
    expect(mockPost).toHaveBeenCalledTimes(1); expect(mockGet).toHaveBeenCalledTimes(1); expect(actualizarSesion).not.toHaveBeenCalled();
  });
  it('descarta una sesión tardía al abandonar o cerrar sesión', async () => {
    let resolver!: (valor: unknown) => void; mockPost.mockImplementation(() => new Promise(resolve => { resolver = resolve; }));
    const pagina = montar(); await activar(); pagina.unmount();
    await act(async () => { resolver({ success: true, data: { ...titular, requierePerfil: false } }); }); expect(actualizarSesion).not.toHaveBeenCalled();
  });
  it.each([undefined, { ...BASE.terminos!, url: 'https://ajeno.example/legal/v1.3/terminos.pdf' }, { ...BASE.terminos!, sha256: '' }, { ...BASE.terminos!, version: '1.2' }])('falla cerrado con Términos ausentes o inválidos (%s)', async terminos => {
    responder({ terminos }); montar(); expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar tu registro'); expect(mockPost).not.toHaveBeenCalled();
  });
  it.each([undefined, { ...BASE.politicaDatos!, url: BASE.terminos!.url }, { ...BASE.politicaDatos!, version: '1.2' }])('falla cerrado con Política de Datos ausente o inválida (%s)', async politicaDatos => { responder({ politicaDatos }); montar(); expect(await screen.findByRole('alert')).toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled(); });
  it.each([404, 'cerrado'])('conserva Perfil anterior con recorrido cerrado (%s)', async tipo => {
    if (tipo === 404) mockGet.mockRejectedValue(new ApiError('No disponible', 404)); else responder({ habilitado: false }); montar();
    expect(await screen.findByText('PERFIL ANTERIOR')).toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('un estado inconsistente no abre Wallet ni vuelve al Perfil anterior', async () => { responder({ identidadVerificada: false }); montar(); expect(await screen.findByRole('alert')).toBeInTheDocument(); expect(screen.queryByText('PERFIL ANTERIOR')).not.toBeInTheDocument(); });
  it('actualiza identificación solo al pulsar y usa el siguiente paso del servidor', async () => {
    responder({ siguientePaso: 'REVISION', identidadVerificada: false, puedeActivar: false, puedeActualizarIdentidad: true });
    mockPost.mockResolvedValue({ success: true, data: { ...BASE, siguientePaso: 'PIN', puedeActivar: false } }); montar();
    const boton = await screen.findByRole('button', { name: 'Actualizar mi identificación' }); expect(mockPost).not.toHaveBeenCalled(); await userEvent.click(boton);
    expect(await screen.findByRole('button', { name: 'PIN guardado por servidor' })).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/identidad/actualizar', {}); expect(actualizarSesion).not.toHaveBeenCalled();
  });
  it('una actualización de identificación incierta exige consultar antes de repetir', async () => {
    responder({ siguientePaso: 'REVISION', identidadVerificada: false, puedeActivar: false, puedeActualizarIdentidad: true }); mockPost.mockRejectedValue(new HttpUncertainError('Espera agotada.'));
    montar(); await userEvent.click(await screen.findByRole('button', { name: 'Actualizar mi identificación' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar la actualización'); expect(mockPost).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Actualizar mi identificación' })).not.toBeInTheDocument();
  });
});
