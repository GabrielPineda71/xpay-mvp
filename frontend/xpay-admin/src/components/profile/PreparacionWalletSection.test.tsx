import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ApiError, HttpUncertainError } from '../../api/client.ts';
import { PreparacionWalletSection } from './PreparacionWalletSection.tsx';
import { TEXTO_CONDICIONES_WALLET, type EstadoPreparacionWallet } from './preparacionWallet.ts';

const mockGet = vi.fn(), mockPost = vi.fn(), mockPatch = vi.fn(), mockPut = vi.fn();
const actualizarSesion = vi.fn(), logout = vi.fn();
const titular = { idUsuario: 99, idPersona: 199, usuario: 'persona.sintetica', estado: 'ACTIVO', roles: ['USUARIO_FINAL'], token: 'test-anterior', requiereCambioClave: false, requierePerfil: true };
vi.mock('../../api/client.ts', async () => ({ ...await vi.importActual('../../api/client.ts'), get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a), patch: (...a: unknown[]) => mockPatch(...a), put: (...a: unknown[]) => mockPut(...a) }));
vi.mock('../../auth/AuthContext.tsx', async () => ({ ...await vi.importActual('../../auth/AuthContext.tsx'), useAuth: () => ({ user: titular, actualizarSesion, logout }) }));
vi.mock('./PinTransaccionalSection.tsx', () => ({ PinTransaccionalSection: ({ onActualizado }: { onActualizado: () => void }) => <button onClick={onActualizado}>PIN guardado por servidor</button> }));
const BASE: EstadoPreparacionWallet = {
  habilitado: true, esRegistroPublico: true, emailVerificado: true, estadoKyc: 'APROBADO', identidadVerificada: true,
  perfilCompleto: true, direccion: 'Calle de pruebas 1', idDepartamento: 1, idCiudad: 10,
  departamento: 'Departamento prueba', ciudad: 'Ciudad prueba', llave: { opcion: 'OMITIDA' },
  pin: { configurado: true, bloqueado: false, longitudActual: 6, longitudRequerida: 6 }, walletHabilitada: false,
  siguientePaso: 'ACTIVAR', puedeActivar: true,
  condiciones: { version: '1.0', fecha: '2026-11-21', url: 'https://app.xpaycol.com/legal/wallet/v1.0/condiciones.pdf', sha256: 'a'.repeat(64) },
};
function montar() { return render(<MemoryRouter initialEntries={['/mi-wallet/perfil']}><Routes><Route path="/mi-wallet/perfil" element={<PreparacionWalletSection fallback={<p>PERFIL ANTERIOR</p>} />} /><Route path="/mi-wallet" element={<p>WALLET CONFIRMADA</p>} /><Route path="/login" element={<p>INGRESO SEGURO</p>} /></Routes></MemoryRouter>); }
function responder(estado: Partial<EstadoPreparacionWallet> = {}) { mockGet.mockResolvedValue({ success: true, data: { ...BASE, ...estado } }); }
async function aceptarYActivar() {
  await userEvent.click(await screen.findByRole('checkbox', { name: TEXTO_CONDICIONES_WALLET }));
  await userEvent.click(screen.getByRole('button', { name: 'Activar mi Wallet' }));
}
beforeEach(() => { vi.clearAllMocks(); mockGet.mockReset(); mockPost.mockReset(); mockPatch.mockReset(); mockPut.mockReset(); responder(); });

describe('Preparación de Wallet: estado del servidor y apertura explícita', () => {
  it('no habilita, consulta Veriff ni solicita PIN al montar aun si todos los pasos están completos', async () => {
    montar();
    expect(await screen.findByRole('button', { name: 'Activar mi Wallet' })).toBeDisabled();
    expect(screen.getByRole('checkbox')).not.toBeChecked();
    expect(mockGet).toHaveBeenCalledTimes(1);
    expect(mockPost).not.toHaveBeenCalled(); expect(mockPatch).not.toHaveBeenCalled(); expect(mockPut).not.toHaveBeenCalled();
    expect(actualizarSesion).not.toHaveBeenCalled();
    expect(screen.queryByText(/Autorización biométrica registrada/)).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Mi información' })).not.toBeInTheDocument();
  });
  it('ofrece Perfil y configuración sin consentimiento biométrico y solo consulta al desplegarlo', async () => {
    const perfil = { celular: '3000000001', email: 'persona@example.test', pais: 'Colombia', identidadVerificada: false, emailVerificado: true, celularVerificado: false };
    mockGet.mockImplementation((path: string) => Promise.resolve({ success: true, data: path === '/api/usuarios/mi-perfil' ? perfil
      : path === '/api/contactos/estado' ? { emailVerificado: true, celularVerificado: false, requeridoParaOperar: false, servicioWhatsApp: false, publicidadEmail: false, publicidadWhatsApp: false }
        : { ...BASE, siguientePaso: 'REVISION', identidadVerificada: false, puedeActivar: false } }));
    montar(); await screen.findByRole('heading', { name: 'Estamos revisando tu registro' });
    expect(mockGet).toHaveBeenCalledTimes(1);
    await userEvent.click(screen.getByText('Mi perfil y configuración'));
    expect(await screen.findByRole('heading', { name: 'Mi información' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Editar información' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Comunicaciones' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Seguridad' })).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /biometría/i })).not.toBeInTheDocument();
    expect(mockGet).not.toHaveBeenCalledWith('/api/pin/estado'); expect(mockGet).not.toHaveBeenCalledWith('/api/breb/mi-llave');
    expect(mockPost).not.toHaveBeenCalled(); expect(mockPatch).not.toHaveBeenCalled(); expect(mockPut).not.toHaveBeenCalled();
  });
  it('activa solo al pulsar y adopta la sesión completa del mismo titular', async () => {
    const sesion = { ...titular, token: 'test-nueva', requierePerfil: false };
    mockPost.mockResolvedValue({ success: true, data: sesion });
    montar(); await aceptarYActivar();
    expect(await screen.findByText('WALLET CONFIRMADA')).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/activar', { aceptaCondiciones: true, versionCondiciones: '1.0' });
    expect(actualizarSesion).toHaveBeenCalledExactlyOnceWith(sesion);
    expect(titular.requierePerfil).toBe(true);
  });
  it('una respuesta de otro titular exige reingreso y nunca cambia privilegios', async () => {
    mockPost.mockResolvedValue({ success: true, data: { ...titular, idPersona: 299, token: 'otra', requierePerfil: false } });
    montar(); await aceptarYActivar();
    expect(await screen.findByRole('button', { name: 'Volver a ingresar' })).toBeInTheDocument();
    expect(actualizarSesion).not.toHaveBeenCalled(); expect(screen.queryByText('WALLET CONFIRMADA')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Volver a ingresar' }));
    expect(logout).toHaveBeenCalledTimes(1); expect(await screen.findByText('INGRESO SEGURO')).toBeInTheDocument();
  });
  it('no repite activación tras un resultado incierto ni al recibir foco', async () => {
    mockPost.mockRejectedValue(new HttpUncertainError('Espera agotada.'));
    montar(); await aceptarYActivar();
    expect(await screen.findByRole('button', { name: 'Volver a ingresar' })).toBeInTheDocument();
    window.dispatchEvent(new Event('focus'));
    expect(mockPost).toHaveBeenCalledTimes(1); expect(mockGet).toHaveBeenCalledTimes(1); expect(actualizarSesion).not.toHaveBeenCalled();
  });
  it('descarta una sesión tardía cuando se abandona el registro o se cierra sesión', async () => {
    let resolver!: (valor: unknown) => void;
    mockPost.mockImplementation(() => new Promise(resolve => { resolver = resolve; }));
    const pagina = montar(); await aceptarYActivar();
    pagina.unmount();
    await act(async () => { resolver({ success: true, data: { ...titular, token: 'nuevo', requierePerfil: false } }); });
    expect(actualizarSesion).not.toHaveBeenCalled();
  });
  it('guardar PIN solo vuelve a consultar y espera la decisión autoritativa', async () => {
    responder({ siguientePaso: 'PIN', puedeActivar: false, pin: { configurado: false, bloqueado: false, longitudRequerida: 6 } });
    montar();
    const boton = await screen.findByRole('button', { name: 'PIN guardado por servidor' });
    responder(); await userEvent.click(boton);
    expect(await screen.findByRole('button', { name: 'Activar mi Wallet' })).toBeInTheDocument();
    expect(mockGet).toHaveBeenCalledTimes(2); expect(mockPost).not.toHaveBeenCalled();
  });
  it('deja registrar la llave después y avanza únicamente con respuesta del servidor', async () => {
    responder({ siguientePaso: 'LLAVE', puedeActivar: false, llave: { opcion: 'PENDIENTE' } });
    mockPut.mockResolvedValue({ success: true, data: { ...BASE, siguientePaso: 'PIN', puedeActivar: false } });
    montar(); await userEvent.click(await screen.findByRole('button', { name: 'Registrar después' }));
    expect(await screen.findByRole('button', { name: 'PIN guardado por servidor' })).toBeInTheDocument();
    expect(mockPut).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/llave', { omitir: true, confirmaPropiedad: false });
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('una llave @ requiere confirmar propiedad y no consulta resolución', async () => {
    responder({ siguientePaso: 'LLAVE', puedeActivar: false, llave: { opcion: 'PENDIENTE' } });
    mockPut.mockResolvedValue({ success: true, data: { ...BASE, siguientePaso: 'PIN', puedeActivar: false } });
    montar(); await userEvent.selectOptions(await screen.findByLabelText('Tipo de llave'), 'ALPHA');
    await userEvent.type(screen.getByPlaceholderText('Ejemplo: @gpo118'), '@prueba118');
    expect(screen.getByRole('button', { name: 'Guardar y continuar' })).toBeDisabled();
    await userEvent.click(screen.getByRole('checkbox')); await userEvent.click(screen.getByRole('button', { name: 'Guardar y continuar' }));
    await waitFor(() => expect(mockPut).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/llave', { omitir: false, tipoLlave: 'ALPHA', valorLlave: '@prueba118', confirmaPropiedad: true }));
    expect(mockPost).not.toHaveBeenCalled(); expect(mockGet.mock.calls.every(c => c[0] === '/api/usuarios/preparacion-wallet')).toBe(true);
  });
  it('un guardado de llave incierto exige consultar y recupera el avance sin volver a enviarlo', async () => {
    responder({ siguientePaso: 'LLAVE', puedeActivar: false, llave: { opcion: 'PENDIENTE' } });
    mockPut.mockRejectedValue(new Error('Conexión interrumpida.'));
    montar(); await userEvent.click(await screen.findByRole('button', { name: 'Registrar después' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar el guardado');
    expect(screen.queryByRole('button', { name: 'Registrar después' })).not.toBeInTheDocument();
    responder({ siguientePaso: 'PIN', puedeActivar: false });
    await userEvent.click(screen.getByRole('button', { name: 'Actualizar mi avance' }));
    expect(await screen.findByRole('button', { name: 'PIN guardado por servidor' })).toBeInTheDocument();
    expect(mockPut).toHaveBeenCalledTimes(1); expect(mockPost).not.toHaveBeenCalled();
  });
  it.each([undefined, { ...BASE.condiciones!, url: 'https://ajeno.example/legal/wallet/v1.0/condiciones.pdf' }, { ...BASE.condiciones!, sha256: '' }, { ...BASE.condiciones!, version: '2.0' }])('no permite aceptar ni activar con condiciones ausentes o no reconocidas (%s)', async condiciones => {
    responder({ condiciones }); montar();
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar tu registro');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it.each([404, 'cerrado'])('conserva Perfil anterior si el recorrido no está habilitado (%s)', async tipo => {
    if (tipo === 404) mockGet.mockRejectedValue(new ApiError('No disponible', 404)); else responder({ habilitado: false });
    montar(); expect(await screen.findByText('PERFIL ANTERIOR')).toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('un error de consulta o estado inconsistente no habilita ni retrocede al Perfil anterior', async () => {
    responder({ identidadVerificada: false }); montar();
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar tu registro');
    expect(screen.queryByText('PERFIL ANTERIOR')).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('Veriff aprobado sin documento consolidado se muestra como revisión sin formularios financieros', async () => {
    responder({ siguientePaso: 'REVISION', identidadVerificada: false, puedeActivar: false, mensaje: 'Necesitamos revisar tu documento.' }); montar();
    expect(await screen.findByText('Necesitamos revisar tu documento.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Activar mi Wallet' })).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('solo actualiza la identificación cuando el servidor lo ofrece y el titular pulsa el botón', async () => {
    responder({ siguientePaso: 'REVISION', identidadVerificada: false, puedeActivar: false, puedeActualizarIdentidad: true });
    mockPost.mockResolvedValue({ success: true, data: { ...BASE, siguientePaso: 'DATOS', perfilCompleto: false, puedeActivar: false } });
    montar(); const boton = await screen.findByRole('button', { name: 'Actualizar mi identificación' });
    expect(mockPost).not.toHaveBeenCalled();
    await userEvent.click(boton);
    expect(await screen.findByRole('heading', { name: '¿Dónde vives?' })).toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/usuarios/preparacion-wallet/identidad/actualizar', {});
    expect(actualizarSesion).not.toHaveBeenCalled();
    expect(mockPost).not.toHaveBeenCalledWith('/api/kyc/veriff/session', expect.anything());
  });
  it('no repite la actualización de identificación si el resultado es incierto', async () => {
    responder({ siguientePaso: 'REVISION', identidadVerificada: false, puedeActivar: false, puedeActualizarIdentidad: true });
    mockPost.mockRejectedValue(new HttpUncertainError('Espera agotada.'));
    montar(); await userEvent.click(await screen.findByRole('button', { name: 'Actualizar mi identificación' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar la actualización de tu identificación');
    expect(screen.queryByRole('button', { name: 'Actualizar mi identificación' })).not.toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1); expect(mockGet).toHaveBeenCalledTimes(1); expect(actualizarSesion).not.toHaveBeenCalled();
  });
});
