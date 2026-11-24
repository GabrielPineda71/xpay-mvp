// Todo el transporte y los candidatos son ficticios: no hay aprobaciones reales.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import { AdminVinculacionesNominaPage, type CoincidenciaNomina } from './AdminVinculacionesNominaPage.tsx';
import { HttpUncertainError } from '../api/client.ts';
const mockGet = vi.fn(); const mockPost = vi.fn();
let roles = ['ADMIN_XPAY'];
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...args: unknown[]) => mockGet(...args), post: (...args: unknown[]) => mockPost(...args) };
});
vi.mock('../auth/AuthContext.tsx', () => ({ useAuth: () => ({ user: { roles } }) }));
const persona = (id: number, cambio: Partial<CoincidenciaNomina> = {}): CoincidenciaNomina => ({
  idEmpleado: id, idUsuario: id + 100, idConvenio: 501, empresa: 'Empresa sintética', empleado: `Persona de prueba ${id}`,
  usuario: `prueba${id}`, documentoMasked: `CC ***00${id}`,
  emailVerificado: true, walletActiva: true, identidadVerificada: true, elegible: true,
  razones: [], huella: `synthetic-review-${id}`, estado: 'PENDIENTE', ...cambio,
});
let items: CoincidenciaNomina[]; let totalPaginas: number;
function mostrar(path = '/admin/nomina-vinculaciones') { return render(<MemoryRouter initialEntries={[path]}><AdminVinculacionesNominaPage /></MemoryRouter>); }
beforeEach(() => {
  roles = ['ADMIN_XPAY']; items = [persona(1), persona(2), persona(3, { elegible: false, walletActiva: false, razones: ['Wallet pendiente de activación.'] })]; totalPaginas = 1;
  mockGet.mockReset().mockImplementation((path: string) => {
    if (path === '/api/libranza/admin/convenios') return Promise.resolve({ success: true, data: [{ idConvenio: 501, nombreEmpresa: 'Empresa sintética' }] });
    if (path.startsWith('/api/libranza/admin/vinculaciones?')) return Promise.resolve({ success: true, data: { items, pagina: path.includes('pagina=2') ? 2 : 1, total: items.length, totalPaginas } });
    return Promise.reject(new Error('GET no simulado.'));
  });
  mockPost.mockReset();
});

describe('Bandeja de coincidencias de nómina', () => {
  it('restringe la vista a ADMIN_XPAY o SUPERUSUARIO sin consultar candidatos para operador', () => {
    roles = ['OPERADOR_XPAY']; mostrar();
    expect(screen.getByRole('alert')).toHaveTextContent('Esta revisión está disponible para administradores de XPAY.');
    expect(mockGet).not.toHaveBeenCalled(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('carga sin seleccionar ni aprobar y bloquea casos incompletos', async () => {
    mostrar();
    const casilla = await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' });
    expect(casilla).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Seleccionar prueba3 en Empresa sintética' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Revisar seleccionadas (0)' })).toBeDisabled();
    expect(screen.getByText('Wallet pendiente de activación.')).toBeInTheDocument();
    expect(screen.getByText('Requisitos pendientes')).toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('la revisión individual exige confirmación laboral y referencia antes del POST', async () => {
    const user = userEvent.setup(); mostrar();
    await user.click(await screen.findByRole('button', { name: 'Revisar a prueba1' }));
    const revision = screen.getByRole('region', { name: 'Confirmar aprobación de nómina' });
    expect(within(revision).getByText('Revisa antes de aprobar')).toHaveFocus();
    const aprobar = within(revision).getByRole('button', { name: 'Aprobar 1 persona(s)' });
    expect(aprobar).toBeDisabled();
    await user.click(within(revision).getByRole('checkbox'));
    expect(aprobar).toBeDisabled();
    const referencia = within(revision).getByRole('textbox', { name: 'Referencia de la revisión' });
    await user.type(referencia, 'abc');
    expect(aprobar).toBeDisabled();
    await user.clear(referencia);
    await user.type(referencia, 'SOPORTE-TEST-1');
    expect(aprobar).toBeEnabled(); expect(mockPost).not.toHaveBeenCalled();
  });
  it('muestra cuenta, documento y requisitos sin pedir IDs ni verificar WhatsApp', async () => {
    mostrar();
    await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' });
    expect(screen.getAllByText('CC ***001')).toHaveLength(1);
    expect(screen.getAllByText('Wallet: activa')).toHaveLength(2);
    expect(screen.queryByText(/WhatsApp/)).not.toBeInTheDocument();
    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument();
    expect(screen.queryByText(/ID del usuario/)).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('aprueba solo las filas seleccionadas con sus huellas y muestra resultados parciales', async () => {
    const user = userEvent.setup();
    mockPost.mockResolvedValue({ success: true, data: { aprobados: 1, rechazados: 1, resultados: [{ idEmpleado: 1, idUsuario: 101, aprobado: true, mensaje: 'Vínculo de prueba aprobado.' }, { idEmpleado: 2, idUsuario: 102, aprobado: false, mensaje: 'La base cambió. Revisa otra vez.' }] } });
    mostrar();
    await user.click(await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' }));
    await user.click(screen.getByRole('checkbox', { name: 'Seleccionar prueba2 en Empresa sintética' }));
    await user.click(screen.getByRole('button', { name: 'Revisar seleccionadas (2)' }));
    const revision = screen.getByRole('region', { name: 'Confirmar aprobación de nómina' });
    expect(within(revision).queryByText('prueba3')).not.toBeInTheDocument();
    await user.click(within(revision).getByRole('checkbox'));
    await user.type(within(revision).getByRole('textbox'), 'SOPORTE-TEST-2');
    await user.click(within(revision).getByRole('button', { name: 'Aprobar 2 persona(s)' }));
    expect(mockPost).toHaveBeenCalledExactlyOnceWith('/api/libranza/admin/vinculaciones/aprobar', { seleccion: [{ idEmpleado: 1, idUsuario: 101, huella: 'synthetic-review-1' }, { idEmpleado: 2, idUsuario: 102, huella: 'synthetic-review-2' }], confirmaPertenenciaLaboral: true, referenciaRevision: 'SOPORTE-TEST-2' }, undefined, 60000);
    expect(await screen.findByText(/La base cambió\. Revisa otra vez\./)).toBeInTheDocument();
    expect(screen.getByText('1 aprobada(s). 1 sin aprobar.')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Confirmar aprobación de nómina' })).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Revisar seleccionadas (0)' })).toBeDisabled());
  });
  it('un timeout consulta otra vez y conserva la incertidumbre sin repetir aprobaciones', async () => {
    const user = userEvent.setup(); mockPost.mockRejectedValue(new HttpUncertainError('Tiempo de prueba.'));
    mostrar(); await user.click(await screen.findByRole('button', { name: 'Revisar a prueba1' }));
    const revision = screen.getByRole('region', { name: 'Confirmar aprobación de nómina' });
    await user.click(within(revision).getByRole('checkbox'));
    await user.type(within(revision).getByRole('textbox'), 'SOPORTE-TEST-3');
    await user.click(within(revision).getByRole('button', { name: 'Aprobar 1 persona(s)' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('No sabemos todavía cuáles aprobaciones se completaron.');
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('region', { name: 'Confirmar aprobación de nómina' })).not.toBeInTheDocument();
    expect(mockGet.mock.calls.filter(([p]) => (p as string).startsWith('/api/libranza/admin/vinculaciones?'))).toHaveLength(2);
  });
  it('cambiar de página descarta la selección y no añade personas ocultas', async () => {
    const user = userEvent.setup(); totalPaginas = 2; mostrar();
    await user.click(await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' }));
    expect(screen.getByRole('button', { name: 'Revisar seleccionadas (1)' })).toBeEnabled();
    items = [persona(4)];
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    expect(await screen.findByRole('checkbox', { name: 'Seleccionar prueba4 en Empresa sintética' })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Revisar seleccionadas (0)' })).toBeDisabled();
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('mantiene deshabilitadas aprobadas o sin huella aunque un booleano diga elegible', async () => {
    items = [persona(1, { estado: 'APROBADO' }), persona(2, { huella: '' })]; mostrar();
    expect(await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: 'Seleccionar prueba2 en Empresa sintética' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Revisar a prueba1' })).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });
  it('seleccionar la página requiere un clic y conserva fuera los casos bloqueados', async () => {
    const user = userEvent.setup(); mostrar();
    await user.click(await screen.findByRole('button', { name: 'Seleccionar elegibles de esta página (2)' }));
    expect(screen.getByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Seleccionar prueba2 en Empresa sintética' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Seleccionar prueba3 en Empresa sintética' })).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Revisar seleccionadas (2)' })).toBeEnabled();
    expect(mockPost).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Limpiar selección' }));
    expect(screen.getByRole('button', { name: 'Revisar seleccionadas (0)' })).toBeDisabled();
  });

  it('el enlace de convenio consulta únicamente su empresa', async () => {
    mostrar('/admin/nomina-vinculaciones?idConvenio=501');
    await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' });
    expect(mockGet).toHaveBeenCalledWith('/api/libranza/admin/vinculaciones?pagina=1&tamanio=50&idConvenio=501');
    expect(screen.getByRole('combobox', { name: 'Empresa' })).toHaveValue('501');
  });
});


describe('Actualización y límites de la revisión', () => {
  it('descarta la selección al cambiar de empresa', async () => {
    const user = userEvent.setup(); mostrar();
    await user.click(await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' }));
    await user.selectOptions(screen.getByRole('combobox', { name: 'Empresa' }), '501');
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith('/api/libranza/admin/vinculaciones?pagina=1&tamanio=50&idConvenio=501'));
    expect(await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' })).not.toBeChecked();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('selecciona como máximo 50 personas aunque el transporte entregue más', async () => {
    items = Array.from({ length: 51 }, (_, i) => persona(i + 1));
    const user = userEvent.setup(); mostrar();
    await user.click(await screen.findByRole('button', { name: 'Seleccionar elegibles de esta página (50)' }));
    expect(screen.getAllByRole('checkbox').filter(c => (c as HTMLInputElement).checked)).toHaveLength(50);
    expect(screen.getByRole('checkbox', { name: 'Seleccionar prueba51 en Empresa sintética' })).toBeDisabled();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('no amplía a todas las empresas cuando la URL contiene un convenio inválido', async () => {
    mostrar('/admin/nomina-vinculaciones?idConvenio=texto');
    expect(await screen.findByRole('alert')).toHaveTextContent('Selecciona una empresa válida.');
    expect(mockGet.mock.calls.filter(([p]) => (p as string).startsWith('/api/libranza/admin/vinculaciones?'))).toHaveLength(0);
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('un GET anterior no reemplaza el resultado de una empresa nueva', async () => {
    let resolverAnterior!: (value: unknown) => void;
    const anterior = new Promise(resolve => { resolverAnterior = resolve; });
    const implementacion = mockGet.getMockImplementation()!;
    mockGet.mockImplementation((path: string) => path === '/api/libranza/admin/vinculaciones?pagina=1&tamanio=50' ? anterior : implementacion(path));
    const user = userEvent.setup(); mostrar();
    await user.selectOptions(await screen.findByRole('combobox', { name: 'Empresa' }), '501');
    expect(await screen.findByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' })).toBeInTheDocument();
    resolverAnterior({ success: true, data: { items: [persona(99)], pagina: 1, total: 1, totalPaginas: 1 } });
    await waitFor(() => expect(screen.queryByRole('checkbox', { name: 'Seleccionar prueba99 en Empresa sintética' })).not.toBeInTheDocument());
    expect(screen.getByRole('checkbox', { name: 'Seleccionar prueba1 en Empresa sintética' })).toBeInTheDocument();
  });

  it('salir de la empresa durante un POST no repite el envío ni muestra su respuesta en otra empresa', async () => {
    let resolver!: (value: unknown) => void;
    mockPost.mockReturnValue(new Promise(resolve => { resolver = resolve; }));
    function CambiarRuta() { const navigate = useNavigate(); return <button onClick={() => navigate('/admin/nomina-vinculaciones?idConvenio=501')}>Cambiar vista de prueba</button>; }
    const user = userEvent.setup();
    render(<MemoryRouter><CambiarRuta /><AdminVinculacionesNominaPage /></MemoryRouter>);
    await user.click(await screen.findByRole('button', { name: 'Revisar a prueba1' }));
    const revision = screen.getByRole('region', { name: 'Confirmar aprobación de nómina' });
    await user.click(within(revision).getByRole('checkbox'));
    await user.type(within(revision).getByRole('textbox'), 'BASE-FICTICIA');
    await user.click(within(revision).getByRole('button', { name: 'Aprobar 1 persona(s)' }));
    await user.click(screen.getByRole('button', { name: 'Cambiar vista de prueba' }));
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Confirmar aprobación de nómina' })).not.toBeInTheDocument());
    resolver({ success: true, data: { aprobados: 1, rechazados: 0, resultados: [{ idEmpleado: 1, idUsuario: 101, aprobado: true, mensaje: 'RESULTADO ANTIGUO' }] } });
    await waitFor(() => expect(screen.queryByText('RESULTADO ANTIGUO')).not.toBeInTheDocument());
    expect(mockPost).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('status', { name: 'Resultado de la revisión' })).not.toBeInTheDocument();
  });
});
