// Cuentas y transporte ficticios: estas pruebas no cambian permisos reales.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { AdminAutorizacionesAnticipoPage, type AutorizacionAnticipo } from './AdminAutorizacionesAnticipoPage.tsx';
import { ApiError, HttpUncertainError } from '../api/client.ts';

const mockGet = vi.fn(); const mockPost = vi.fn();
let roles = ['ADMIN_XPAY']; let idUsuario = 901;
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) };
});
vi.mock('../auth/AuthContext.tsx', () => ({ useAuth: () => ({ user: { idUsuario, roles } }) }));
const persona = (id: number, cambios: Partial<AutorizacionAnticipo> = {}): AutorizacionAnticipo => ({
  idEmpleadoUsuario: id, idEmpleado: id + 100, idUsuario: id + 200, idConvenio: 501,
  empresa: 'Empresa sintética', empleado: `Persona de prueba ${id}`, usuario: `prueba${id}`, documentoMasked: `CC · ***00${id}`,
  emailVerificado: true, identidadVerificada: true, walletActiva: true, origenPublico: true,
  estado: 'PENDIENTE', puedeAutorizar: true, puedeRevocar: false, razones: [], huella: `synthetic-snapshot-${id}`,
  ...cambios,
});
let items: AutorizacionAnticipo[]; let totalPaginas = 1;
function mostrar(path = '/admin/nomina-autorizaciones') {
  return render(<MemoryRouter initialEntries={[path]}><AdminAutorizacionesAnticipoPage /></MemoryRouter>);
}
async function abrir(user: ReturnType<typeof userEvent.setup>, accion: 'autorizar' | 'revocar' = 'autorizar', cuenta = 'prueba1') {
  await user.click(await screen.findByRole('button', { name: `${accion === 'autorizar' ? 'Autorizar' : 'Revocar'} a ${cuenta}` }));
  return screen.getByRole('dialog', { name: accion === 'autorizar' ? 'Confirmar autorización de anticipo' : 'Confirmar revocación de anticipo' });
}
async function confirmar(user: ReturnType<typeof userEvent.setup>, accion: 'autorizar' | 'revocar' = 'autorizar') {
  const dialogo = screen.getByRole('dialog');
  await user.click(within(dialogo).getByRole('checkbox'));
  await user.type(within(dialogo).getByRole('textbox', { name: 'Referencia de la decisión' }), 'SOPORTE-SINTETICO');
  await user.click(within(dialogo).getByRole('button', { name: accion === 'autorizar' ? 'Confirmar autorización' : 'Confirmar revocación' }));
}
beforeEach(() => {
  roles = ['ADMIN_XPAY']; idUsuario = 901; totalPaginas = 1;
  items = [persona(1), persona(2, { estado: 'AUTORIZADO', puedeAutorizar: false, puedeRevocar: true }), persona(3, { puedeAutorizar: false, emailVerificado: false, razones: ['Falta confirmar el correo.'] })];
  mockGet.mockReset().mockImplementation((path: string) => {
    if (path === '/api/libranza/admin/convenios') return Promise.resolve({ success: true, data: [{ idConvenio: 501, nombreEmpresa: 'Empresa sintética' }] });
    if (path.startsWith('/api/libranza/admin/autorizaciones-anticipo?')) return Promise.resolve({ success: true, data: { items, pagina: path.includes('pagina=2') ? 2 : 1, total: items.length, totalPaginas } });
    return Promise.reject(new Error('GET no simulado.'));
  });
  mockPost.mockReset();
});

describe('Autorizaciones manuales de Anticipo de Nómina', () => {
  it.each(['OPERADOR_XPAY', 'ADMIN_EMPRESA', 'USUARIO_FINAL'])('el rol %s no consulta ni autoriza', rol => {
    roles = [rol]; mostrar();
    expect(screen.getByRole('alert')).toHaveTextContent('Esta opción está disponible para administradores de XPAY.');
    expect(mockGet).not.toHaveBeenCalled(); expect(mockPost).not.toHaveBeenCalled();
  });
  it.each(['ADMIN_XPAY', 'SUPERUSUARIO'])('el rol %s consulta sin otorgar permisos automáticamente', async rol => {
    roles = [rol]; mostrar();
    expect(await screen.findByRole('button', { name: 'Autorizar a prueba1' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Revocar a prueba2' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Autorizar a prueba3' })).not.toBeInTheDocument();
    expect(screen.getByText('Falta confirmar el correo.')).toBeInTheDocument();
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('exige confirmación expresa y soporte antes de autorizar una persona con su huella', async () => {
    const user = userEvent.setup(); mostrar(); const dialogo = await abrir(user);
    expect(within(dialogo).getByRole('heading', { name: 'Autorizar Anticipo de Nómina' })).toHaveFocus();
    expect(within(dialogo).getByText('prueba1')).toBeInTheDocument();
    expect(within(dialogo).queryByText('prueba2')).not.toBeInTheDocument();
    const guardar = within(dialogo).getByRole('button', { name: 'Confirmar autorización' });
    expect(guardar).toBeDisabled();
    await user.type(within(dialogo).getByRole('textbox'), 'SOPORTE-SINTETICO'); expect(guardar).toBeDisabled();
    await user.click(within(dialogo).getByRole('checkbox', { name: 'Confirmo que XPAY autoriza a esta persona a usar Anticipo de Nómina bajo este convenio.' }));
    expect(guardar).toBeEnabled(); expect(mockPost).not.toHaveBeenCalled();
    mockPost.mockResolvedValue({ success: true, data: persona(1, { estado: 'AUTORIZADO' }) });
    items = [persona(1, { estado: 'AUTORIZADO', puedeAutorizar: false, puedeRevocar: true })];
    await user.click(guardar);
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/libranza/admin/autorizaciones-anticipo/autorizar', { idEmpleadoUsuario: 1, huella: 'synthetic-snapshot-1', referenciaRevision: 'SOPORTE-SINTETICO', confirmaAutorizacion: true }, undefined, 60000);
    expect(await screen.findByText('Autorización registrada para prueba1.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Revocar a prueba1' })).toBeEnabled();
  });
  it('no acepta referencias de menos de cinco caracteres útiles', async () => {
    const user = userEvent.setup(); mostrar(); const dialogo = await abrir(user);
    await user.click(within(dialogo).getByRole('checkbox'));
    await user.type(within(dialogo).getByRole('textbox'), ' abc ');
    expect(within(dialogo).getByRole('button', { name: 'Confirmar autorización' })).toBeDisabled();
    expect(within(dialogo).getByRole('textbox')).toHaveAttribute('maxLength', '200'); expect(mockPost).not.toHaveBeenCalled();
  });
  it('revoca con confirmación separada y explica que las obligaciones existentes se conservan', async () => {
    const user = userEvent.setup(); mostrar(); const dialogo = await abrir(user, 'revocar', 'prueba2');
    expect(within(dialogo).getByText('Esta acción bloquea el acceso al producto. Los anticipos y las obligaciones existentes se conservan.')).toBeInTheDocument();
    expect(within(dialogo).getByRole('button', { name: 'Confirmar revocación' })).toBeDisabled();
    mockPost.mockResolvedValue({ success: true, data: persona(2, { estado: 'REVOCADO' }) });
    items = [persona(2, { estado: 'REVOCADO' })]; await confirmar(user, 'revocar');
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/libranza/admin/autorizaciones-anticipo/revocar', { idEmpleadoUsuario: 2, huella: 'synthetic-snapshot-2', referenciaRevision: 'SOPORTE-SINTETICO', confirmaAutorizacion: true }, undefined, 60000);
    expect(await screen.findByText('Acceso revocado para prueba2.')).toBeInTheDocument();
  });
  it('permite revocar con otros requisitos pendientes cuando el servidor lo autoriza', async () => {
    items = [persona(1, { estado: 'REVISION_REQUERIDA', puedeAutorizar: false, puedeRevocar: true, emailVerificado: false, razones: ['Revisar el correo actual.'] })]; mostrar();
    expect(await screen.findByRole('button', { name: 'Revocar a prueba1' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Autorizar a prueba1' })).not.toBeInTheDocument();
  });
  it('muestra acceso previo sin otorgar permisos ni pedir aceptaciones adicionales', async () => {
    items = [persona(1, { estado: 'FLUJO_PREVIO', puedeRevocar: true, origenPublico: false })]; mostrar();
    expect(await screen.findByText('Acceso previo')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Autorizar a prueba1' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Revocar a prueba1' })).toBeEnabled();
    expect(screen.queryByText(/piloto|WhatsApp/i)).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('Escape cancela sin enviar y devuelve el foco al botón original', async () => {
    const user = userEvent.setup(); mostrar(); await abrir(user); await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Autorizar a prueba1' })).toHaveFocus(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('mantiene el foco de teclado dentro del diálogo', async () => {
    const user = userEvent.setup(); mostrar(); const dialogo = await abrir(user);
    await user.tab({ shift: true }); expect(within(dialogo).getByRole('button', { name: 'Cancelar' })).toHaveFocus();
    await user.tab(); expect(within(dialogo).getByRole('checkbox')).toHaveFocus();
    await user.click(within(dialogo).getByRole('button', { name: 'Cancelar' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('bloquea doble envío, cancelación y cambio de empresa mientras procesa', async () => {
    let resolver!: (x: unknown) => void; mockPost.mockReturnValue(new Promise(resolve => { resolver = resolve; }));
    const user = userEvent.setup(); mostrar(); await abrir(user); await confirmar(user);
    const dialogo = screen.getByRole('dialog');
    expect(within(dialogo).getByRole('button', { name: 'Guardando...' })).toBeDisabled();
    expect(within(dialogo).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(screen.getByRole('combobox', { name: 'Empresa' })).toBeDisabled();
    await user.keyboard('{Escape}'); expect(dialogo).toBeInTheDocument(); expect(mockPost).toHaveBeenCalledTimes(1);
    resolver({ success: true, data: persona(1, { estado: 'AUTORIZADO' }) }); await screen.findByText('Autorización registrada para prueba1.');
  });
  it('un 409 descarta la confirmación y consulta el estado sin repetir el POST', async () => {
    const user = userEvent.setup(); mockPost.mockRejectedValue(new ApiError('Datos cambiados.', 409)); mostrar(); await abrir(user);
    items = [persona(1, { huella: 'synthetic-snapshot-renovado' })]; await confirmar(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('La información cambió. La lista se actualizó; revisa nuevamente a la persona.');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(mockPost).toHaveBeenCalledTimes(1);
    await abrir(user); mockPost.mockResolvedValue({ success: true, data: persona(1) }); await confirmar(user);
    expect(mockPost.mock.calls[1][1]).toMatchObject({ huella: 'synthetic-snapshot-renovado' });
  });
  it('un timeout conserva incertidumbre y muestra el estado actualizado sin reenviar', async () => {
    const user = userEvent.setup(); mockPost.mockRejectedValue(new HttpUncertainError('Tiempo de prueba.')); mostrar(); await abrir(user);
    items = [persona(1, { estado: 'AUTORIZADO', puedeAutorizar: false, puedeRevocar: true })]; await confirmar(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar el resultado. Revisa el estado actualizado antes de intentarlo otra vez.');
    expect(screen.getByRole('button', { name: 'Revocar a prueba1' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Autorizar a prueba1' })).not.toBeInTheDocument(); expect(mockPost).toHaveBeenCalledTimes(1);
  });
  it('trata una respuesta de otra persona como resultado incierto', async () => {
    const user = userEvent.setup(); mockPost.mockResolvedValue({ success: true, data: persona(99) }); mostrar(); await abrir(user); await confirmar(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos confirmar el resultado.');
    expect(screen.queryByText('Autorización registrada para prueba1.')).not.toBeInTheDocument(); expect(mockPost).toHaveBeenCalledTimes(1);
  });
  it('un convenio inválido en URL nunca amplía la consulta a todas las empresas', async () => {
    mostrar('/admin/nomina-autorizaciones?idConvenio=texto');
    expect(await screen.findByRole('alert')).toHaveTextContent('Selecciona una empresa válida.');
    expect(mockGet.mock.calls.filter(([path]) => (path as string).startsWith('/api/libranza/admin/autorizaciones-anticipo?'))).toHaveLength(0);
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('conserva la empresa en la consulta y el enlace de coincidencias', async () => {
    mostrar('/admin/nomina-autorizaciones?idConvenio=501'); await screen.findByRole('button', { name: 'Autorizar a prueba1' });
    expect(mockGet).toHaveBeenCalledWith('/api/libranza/admin/autorizaciones-anticipo?pagina=1&tamanio=50&idConvenio=501');
    expect(screen.getByRole('link', { name: '← Coincidencias de nómina' })).toHaveAttribute('href', '/admin/nomina-vinculaciones?idConvenio=501');
  });
  it('descarta un GET de la empresa anterior que termina tarde', async () => {
    let resolver!: (x: unknown) => void; const implementacion = mockGet.getMockImplementation()!;
    mockGet.mockImplementation((path: string) => path === '/api/libranza/admin/autorizaciones-anticipo?pagina=1&tamanio=50' ? new Promise(resolve => { resolver = resolve; }) : implementacion(path));
    const user = userEvent.setup(); mostrar(); await user.selectOptions(await screen.findByRole('combobox', { name: 'Empresa' }), '501');
    await screen.findByRole('button', { name: 'Autorizar a prueba1' });
    resolver({ success: true, data: { items: [persona(99)], pagina: 1, total: 1, totalPaginas: 1 } });
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Autorizar a prueba99' })).not.toBeInTheDocument());
  });
  it('descarta el resultado de un POST cuando cambió el contexto de empresa', async () => {
    let resolver!: (x: unknown) => void; mockPost.mockReturnValue(new Promise(resolve => { resolver = resolve; }));
    function Cambiar() { const navigate = useNavigate(); return <button onClick={() => navigate('/admin/nomina-autorizaciones?idConvenio=501')}>Otra vista de prueba</button>; }
    const user = userEvent.setup(); render(<MemoryRouter><Cambiar /><AdminAutorizacionesAnticipoPage /></MemoryRouter>);
    await abrir(user); await confirmar(user); await user.click(screen.getByRole('button', { name: 'Otra vista de prueba' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); resolver({ success: true, data: persona(1) });
    await waitFor(() => expect(screen.queryByText('Autorización registrada para prueba1.')).not.toBeInTheDocument()); expect(mockPost).toHaveBeenCalledTimes(1);
  });
  it('pagina sin arrastrar cuentas ocultas ni enviar decisiones', async () => {
    totalPaginas = 2; const user = userEvent.setup(); mostrar(); await screen.findByRole('button', { name: 'Autorizar a prueba1' });
    items = [persona(9)]; await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    expect(await screen.findByRole('button', { name: 'Autorizar a prueba9' })).toBeEnabled();
    expect(mockGet).toHaveBeenCalledWith('/api/libranza/admin/autorizaciones-anticipo?pagina=2&tamanio=50');
    expect(screen.queryByRole('button', { name: 'Autorizar a prueba1' })).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('estados desconocidos o huellas ausentes no ofrecen decisiones', async () => {
    items = [persona(1, { estado: 'DESCONOCIDO' as AutorizacionAnticipo['estado'], puedeRevocar: true }), persona(2, { huella: '', puedeRevocar: true })]; mostrar();
    await screen.findByText('Revisar estado');
    expect(screen.queryByRole('button', { name: /^Autorizar a|^Revocar a/ })).not.toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });
});
