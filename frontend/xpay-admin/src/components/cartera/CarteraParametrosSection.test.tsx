// Datos exclusivamente sintéticos. Estas pruebas no llaman servicios ni guardan condiciones reales.
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, HttpUncertainError } from '../../api/client.ts';
import { CarteraParametrosSection } from './CarteraParametrosSection.tsx';
import { CarteraOrdinariaAdminPage } from '../../pages/CarteraOrdinariaAdminPage.tsx';

const mockGet = vi.fn(); const mockPost = vi.fn(); const mockPut = vi.fn();
let roles: string[] = ['ADMIN_XPAY']; let idUsuario = 9001; let token = 'sesion-sintetica';
vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a), put: (...a: unknown[]) => mockPut(...a) };
});
vi.mock('../../auth/AuthContext.tsx', () => ({ useAuth: () => ({ user: { idUsuario, token, roles } }) }));
const BASE = '/api/cartera-ordinaria/admin/parametros';
const body = { tasaEmv: 1.2345, porcAval: 0, porcAdmin: 0.5, aplicaIva: false, porcIva: 0, plazoMin: 1, plazoMax: 48, frecuencia: 'QUINCENAL', montoMin: 0, montoMax: 123.45 };
const registro = (tipo = 'COMPRA_COMERCIO', extra: Record<string, unknown> = {}) => ({ idParametro: tipo === 'COMPRA_COMERCIO' ? 1 : 2, tipoUtilizacion: tipo, estado: 'ACTIVO', ...body, ...extra });
let registros: unknown;
const pendiente = <T,>() => { let resolve!: (v: T) => void; let reject!: (e: unknown) => void; const promise = new Promise<T>((r, j) => { resolve = r; reject = j; }); return { promise, resolve, reject }; };
const agregar = () => screen.getByRole('button', { name: 'Agregar parámetros' });
const crear = () => screen.getByRole('button', { name: 'Crear parámetros' });
const campo = (nombre: string) => screen.getByLabelText(new RegExp(`^${nombre.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}`));
function cambiar(nombre: string, valor: string) { fireEvent.change(campo(nombre), { target: { value: valor } }); }
async function abrir(tipo = 'COMPRA_COMERCIO') {
  await waitFor(() => expect(agregar()).toBeEnabled());
  fireEvent.click(agregar());
  cambiar('Tipo de utilización', tipo);
}
function completar(extra: Record<string, string> = {}) {
  const valores = { 'Tasa EMV (%)': '1.2345', 'Aval (%)': '0', 'Administración (%)': '0.5', 'Plazo mínimo (meses)': '1', 'Plazo máximo (meses)': '48', 'Monto mínimo (COP)': '0', 'Monto máximo (COP)': '123.45', 'Frecuencia registrada': 'QUINCENAL', 'Aplica IVA': 'no', ...extra };
  for (const [nombre, valor] of Object.entries(valores)) cambiar(nombre, valor);
}
beforeEach(() => {
  roles = ['ADMIN_XPAY']; idUsuario = 9001; token = 'sesion-sintetica'; registros = [];
  mockGet.mockReset().mockImplementation(() => Promise.resolve(registros));
  mockPost.mockReset().mockImplementation((path: string, enviado: Record<string, unknown>) => {
    const resultado = registro(path.split('/').pop(), enviado);
    registros = [...(registros as unknown[]), resultado];
    return Promise.resolve(resultado);
  });
  mockPut.mockReset().mockImplementation((path: string, enviado: Record<string, unknown>) => {
    const previo = (registros as Record<string, unknown>[]).find(p => p.tipoUtilizacion === path.split('/').pop())!;
    const resultado = { ...previo, ...enviado };
    registros = (registros as Record<string, unknown>[]).map(p => p.tipoUtilizacion === resultado.tipoUtilizacion ? resultado : p);
    return Promise.resolve(resultado);
  });
});

describe('Parámetros administrativos de cartera', () => {
  it('distingue carga de lista vacía y abre todos los campos sin valores predeterminados ni escrituras', async () => {
    const carga = pendiente<unknown>(); mockGet.mockReturnValue(carga.promise);
    render(<CarteraParametrosSection />);
    expect(screen.getByText('Cargando parámetros…')).toBeInTheDocument();
    expect(agregar()).toBeDisabled();
    expect(screen.queryByText(/Sin parámetros registrados/)).not.toBeInTheDocument();
    await act(async () => carga.resolve([]));
    await abrir();
    cambiar('Tipo de utilización', '');
    expect(screen.getByText(/Sin parámetros registrados/)).toBeInTheDocument();
    for (const input of screen.getAllByRole('textbox')) expect(input).toHaveValue('');
    for (const select of screen.getAllByRole('combobox')) expect(select).toHaveValue('');
    expect(mockPost).not.toHaveBeenCalled(); expect(mockPut).not.toHaveBeenCalled();
    fireEvent.click(crear());
    expect(screen.getByRole('alert')).toHaveTextContent('Revisa los campos');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it.each([null, {}, { data: [] }, [{}], [registro('DESCONOCIDO')], [registro(), registro()], [registro('COMPRA_COMERCIO', { aplicaIva: null })]])('bloquea creación si GET tiene formato inválido (%j)', async respuesta => {
    registros = respuesta; render(<CarteraParametrosSection />);
    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo consultar');
    expect(agregar()).toBeDisabled(); expect(screen.queryByText(/Sin parámetros registrados/)).not.toBeInTheDocument();
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('mantiene bloqueado Agregar después de error de carga hasta una actualización válida', async () => {
    mockGet.mockRejectedValueOnce(new Error('Fallo sintético'));
    render(<CarteraParametrosSection />);
    await screen.findByRole('alert'); expect(agregar()).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar lista' }));
    await waitFor(() => expect(agregar()).toBeEnabled());
    expect(mockPost).not.toHaveBeenCalled();
  });

  it.each(['COMPRA_COMERCIO', 'AVANCE_WALLET'])('crea %s por POST con exactamente los diez campos explícitos y consulta la lista', async tipo => {
    render(<CarteraParametrosSection />); await abrir(tipo); completar(); fireEvent.click(crear());
    await screen.findByText('Parámetros creados correctamente.');
    expect(mockPost).toHaveBeenCalledExactlyOnceWith(`${BASE}/${tipo}`, body);
    expect(Object.keys(mockPost.mock.calls[0][1])).toHaveLength(10);
    expect(mockPut).not.toHaveBeenCalled(); expect(mockGet).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole('button', { name: 'Crear parámetros' })).not.toBeInTheDocument();
    expect(screen.getByText('Activo')).toBeInTheDocument();
  });

  it.each([
    ['Tasa EMV (%)', '0', /debe ser mayor que cero/],
    ['Tasa EMV (%)', '1.23456', /máximo 4 decimales/],
    ['Aval (%)', '-1', /no negativo/],
    ['Administración (%)', '100000', /límite técnico/],
    ['Plazo mínimo (meses)', '0', /debe ser mayor que cero/],
    ['Plazo máximo (meses)', '1.5', /ningún decimal/],
    ['Plazo máximo (meses)', '1073741824', /límite técnico/],
    ['Plazo mínimo (meses)', '49', /plazo máximo debe ser mayor/],
    ['Monto máximo (COP)', '0', /debe ser mayor que cero/],
    ['Monto máximo (COP)', '123.456', /máximo 2 decimales/],
    ['Monto mínimo (COP)', '124', /monto máximo debe ser mayor/],
    ['Monto máximo (COP)', '90071992547409.91', /precisión o el límite técnico/],
    ['Monto máximo (COP)', '90071992547409.71', /precisión o el límite técnico/],
  ])('rechaza %s = %s antes de enviar', async (nombre, valor, mensaje) => {
    render(<CarteraParametrosSection />); await abrir(); completar({ [nombre as string]: valor as string }); fireEvent.click(crear());
    expect(screen.getByText(mensaje as RegExp)).toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
  });

  it('exige frecuencia e IVA explícitos, y exige porcentaje cuando IVA sí aplica', async () => {
    render(<CarteraParametrosSection />); await abrir(); completar({ 'Frecuencia registrada': '', 'Aplica IVA': '' }); fireEvent.click(crear());
    expect(screen.getByText('Selecciona la frecuencia.')).toBeInTheDocument();
    expect(screen.getByText('Selecciona si aplica IVA.')).toBeInTheDocument();
    cambiar('Frecuencia registrada', 'MENSUAL'); cambiar('Aplica IVA', 'si'); fireEvent.click(crear());
    expect(screen.getByText('IVA (%): completa este campo.')).toBeInTheDocument(); expect(mockPost).not.toHaveBeenCalled();
    cambiar('IVA (%)', '0.00001'); fireEvent.click(crear()); expect(mockPost).not.toHaveBeenCalled();
    cambiar('IVA (%)', '0.1250'); fireEvent.click(crear());
    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1));
    expect(mockPost.mock.calls[0][1]).toEqual({ ...body, frecuencia: 'MENSUAL', aplicaIva: true, porcIva: 0.125 });
  });

  it('deriva IVA cero al elegir No aunque antes hubiera un porcentaje y acepta cero en cargos y monto mínimo', async () => {
    render(<CarteraParametrosSection />); await abrir(); completar({ 'Aplica IVA': 'si' }); cambiar('IVA (%)', '0.5'); cambiar('Aplica IVA', 'no');
    expect(campo('IVA (%)')).toHaveValue('0'); expect(campo('IVA (%)')).toBeDisabled(); fireEvent.click(crear());
    await waitFor(() => expect(mockPost).toHaveBeenCalledTimes(1)); expect(mockPost.mock.calls[0][1]).toEqual(body);
  });

  it('acepta límites de escala, centavos representables y plazos técnicos sin inventar tope comercial', async () => {
    render(<CarteraParametrosSection />); await abrir(); completar({ 'Aval (%)': '99999.9999', 'Plazo máximo (meses)': '1073741823', 'Monto máximo (COP)': '90071992547409.9' }); fireEvent.click(crear());
    await screen.findByText('Parámetros creados correctamente.');
    expect(mockPost.mock.calls[0][1]).toMatchObject({ porcAval: 99999.9999, plazoMax: 1073741823, montoMax: 90071992547409.9 });
  });

  it('normaliza ceros decimales finales y coma decimal sin cambiar el valor enviado', async () => {
    render(<CarteraParametrosSection />); await abrir(); completar({ 'Tasa EMV (%)': '1,2345000', 'Monto máximo (COP)': '123.45000' }); fireEvent.click(crear());
    await screen.findByText('Parámetros creados correctamente.'); expect(mockPost.mock.calls[0][1]).toEqual(body);
  });

  it('solo ofrece el tipo faltante; un registro inactivo también ocupa su tipo', async () => {
    registros = [registro('COMPRA_COMERCIO', { estado: 'INACTIVO' })]; render(<CarteraParametrosSection />); await abrir('AVANCE_WALLET');
    const opciones = within(campo('Tipo de utilización')).getAllByRole('option').map(o => (o as HTMLOptionElement).value);
    expect(opciones).toEqual(['', 'AVANCE_WALLET']); expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });

  it('bloquea agregar cuando existen los dos tipos incluso con un inactivo', async () => {
    registros = [registro(), registro('AVANCE_WALLET', { estado: 'INACTIVO' })]; render(<CarteraParametrosSection />);
    await screen.findByText(/Los dos tipos ya están registrados/); expect(agregar()).toBeDisabled(); expect(screen.getAllByRole('button', { name: /^Editar / })).toHaveLength(2);
  });

  it('edita con valores precargados por PUT, conserva estado inactivo y no incluye estado ni asignaciones', async () => {
    registros = [registro('COMPRA_COMERCIO', { estado: 'INACTIVO' })]; render(<CarteraParametrosSection />);
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Compra en comercio' }));
    expect(campo('Tasa EMV (%)')).toHaveValue('1.2345'); expect(campo('Tipo de utilización')).toBeDisabled();
    expect(screen.getByText(/Este registro seguirá inactivo/)).toBeInTheDocument();
    cambiar('Administración (%)', '0'); fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await screen.findByText('Cambios guardados correctamente.');
    expect(mockPut).toHaveBeenCalledExactlyOnceWith(`${BASE}/COMPRA_COMERCIO`, { ...body, porcAdmin: 0 });
    expect(mockPost).not.toHaveBeenCalled(); expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });

  it('409 cierra el formulario, reconcilia sin sobrescribir y conserva el estado recibido', async () => {
    mockPost.mockImplementation(() => { registros = [registro('COMPRA_COMERCIO', { estado: 'INACTIVO', tasaEmv: 3 })]; return Promise.reject(new ApiError('Ya existe', 409)); });
    render(<CarteraParametrosSection />); await abrir(); completar(); fireEvent.click(crear());
    await screen.findByText(/Este tipo ya está registrado; no se sobrescribió/);
    expect(screen.getByText('Inactivo')).toBeInTheDocument(); expect(mockGet).toHaveBeenCalledTimes(2); expect(mockPost).toHaveBeenCalledTimes(1); expect(mockPut).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Crear parámetros' })).not.toBeInTheDocument();
    fireEvent.click(agregar()); expect(within(campo('Tipo de utilización')).queryByRole('option', { name: /COMPRA_COMERCIO/ })).not.toBeInTheDocument();
  });

  it.each([new HttpUncertainError('timeout sintético'), new Error('Red sintética'), new ApiError('Timeout sintético', 408), new ApiError('Fallo sintético', 503)])('ante resultado incierto consulta sin repetir ni afirmar fallo o éxito (%s)', async error => {
    mockPost.mockImplementation(() => { registros = [registro()]; return Promise.reject(error); });
    render(<CarteraParametrosSection />); await abrir(); completar(); fireEvent.click(crear());
    await screen.findByText(/No se pudo confirmar el guardado; no se repitió el envío/);
    expect(mockGet).toHaveBeenCalledTimes(2); expect(mockPost).toHaveBeenCalledTimes(1); expect(mockPut).not.toHaveBeenCalled();
    expect(screen.getByText('Activo')).toBeInTheDocument(); expect(screen.queryByText(/creados correctamente/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Crear parámetros' })).not.toBeInTheDocument();
  });

  it.each([{ success: true }, registro('AVANCE_WALLET'), registro('COMPRA_COMERCIO', { estado: 'INACTIVO' }), registro('COMPRA_COMERCIO', { montoMax: 99 }), registro('COMPRA_COMERCIO', { idParametro: 0 })])('trata una respuesta de escritura inesperada como incierta (%j)', async respuesta => {
    mockPost.mockResolvedValue(respuesta); render(<CarteraParametrosSection />); await abrir(); completar(); fireEvent.click(crear());
    await screen.findByText(/No se pudo confirmar el guardado; no se repitió el envío/);
    expect(mockPost).toHaveBeenCalledTimes(1); expect(mockGet).toHaveBeenCalledTimes(2); expect(screen.queryByText(/creados correctamente/)).not.toBeInTheDocument();
  });

  it('bloquea Agregar cuando falla reconciliación y no afirma que se actualizó la lista', async () => {
    mockPost.mockRejectedValue(new Error('Red sintética')); mockGet.mockResolvedValueOnce([]).mockRejectedValueOnce(new Error('GET sintético'));
    render(<CarteraParametrosSection />); await abrir(); completar(); fireEvent.click(crear());
    await screen.findByText(/No se pudo confirmar el guardado; no se repitió el envío. No se pudo consultar la lista/);
    expect(agregar()).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Crear parámetros' })).not.toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar lista' }));
    await waitFor(() => expect(agregar()).toBeEnabled()); expect(mockPost).toHaveBeenCalledTimes(1);
  });

  it('informa guardado confirmado pero bloquea nuevas acciones si la consulta posterior falla', async () => {
    mockGet.mockResolvedValueOnce([]).mockRejectedValueOnce(new Error('GET sintético'));
    render(<CarteraParametrosSection />); await abrir(); completar(); fireEvent.click(crear());
    await screen.findByText(/Parámetros creados correctamente. No se pudo consultar la lista/); expect(agregar()).toBeDisabled();
  });

  it('400 conserva el formulario para corregirlo sin reenvío automático', async () => {
    mockPost.mockRejectedValue(new ApiError('Validación sintética', 400)); render(<CarteraParametrosSection />); await abrir(); completar(); fireEvent.click(crear());
    expect(await screen.findByRole('alert')).toHaveTextContent('No se guardaron los parámetros. Validación sintética');
    expect(campo('Monto máximo (COP)')).toHaveValue('123.45'); expect(crear()).toBeEnabled(); expect(mockPost).toHaveBeenCalledTimes(1); expect(mockGet).toHaveBeenCalledTimes(1);
  });

  it('impide doble submit y bloquea todo el formulario, Cancelar y cambio de pestaña hasta reconciliar', async () => {
    const envio = pendiente<unknown>(); const lectura = pendiente<unknown>();
    mockPost.mockReturnValue(envio.promise); mockGet.mockResolvedValueOnce([]).mockReturnValueOnce(lectura.promise);
    render(<CarteraOrdinariaAdminPage />); await abrir(); completar();
    const boton = crear(); const form = boton.closest('form')!;
    fireEvent.submit(form); fireEvent.submit(form);
    expect(mockPost).toHaveBeenCalledTimes(1); expect(campo('Tasa EMV (%)')).toBeDisabled(); expect(screen.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Gastos Cobranza' })).toBeDisabled();
    await act(async () => envio.resolve(registro()));
    expect(screen.getByRole('button', { name: 'Política Crédito' })).toBeDisabled();
    await act(async () => lectura.resolve([registro()]));
    await screen.findByText('Parámetros creados correctamente.');
    expect(screen.getByRole('button', { name: 'Gastos Cobranza' })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: 'Gastos Cobranza' }));
    await screen.findByText('Sin tramos registrados'); expect(mockGet).toHaveBeenLastCalledWith('/api/cartera-ordinaria/admin/gastos-cobranza');
  });

  it('cancelar no escribe y una nueva creación vuelve a estar vacía', async () => {
    render(<CarteraParametrosSection />); await abrir(); completar(); fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(mockPost).not.toHaveBeenCalled(); expect(mockPut).not.toHaveBeenCalled();
    await abrir(); expect(campo('Tasa EMV (%)')).toHaveValue(''); expect(campo('Aplica IVA')).toHaveValue('');
  });

  it.each(['OPERADOR_XPAY', 'USUARIO_FINAL', 'COMERCIO'])('rol %s no consulta ni muestra gestión', rol => {
    roles = [rol]; render(<CarteraParametrosSection />); expect(screen.getByRole('alert')).toHaveTextContent('Solo administradores');
    expect(mockGet).not.toHaveBeenCalled(); expect(mockPost).not.toHaveBeenCalled(); expect(screen.queryByRole('button', { name: 'Agregar parámetros' })).not.toBeInTheDocument();
  });
  it('permite consultar para SUPERUSUARIO', async () => { roles = ['SUPERUSUARIO']; render(<CarteraParametrosSection />); await waitFor(() => expect(agregar()).toBeEnabled()); });

  it('ignora respuesta de una escritura tras desmontar sin consultar ni notificar a la siguiente pantalla', async () => {
    const envio = pendiente<unknown>(); const busy = vi.fn(); mockPost.mockReturnValue(envio.promise);
    const vista = render(<CarteraParametrosSection onBusyChange={busy} />); await abrir(); completar(); fireEvent.click(crear()); vista.unmount();
    busy.mockClear(); await act(async () => envio.resolve(registro()));
    expect(mockGet).toHaveBeenCalledTimes(1); expect(busy).not.toHaveBeenCalled();
  });

  it('ignora la escritura del actor anterior cuando cambia la sesión y no mezcla listas', async () => {
    const envio = pendiente<unknown>(); mockPost.mockReturnValue(envio.promise);
    const vista = render(<CarteraParametrosSection />); await abrir(); completar(); fireEvent.click(crear());
    idUsuario = 9002; token = 'otra-sesion-sintetica'; registros = [registro('AVANCE_WALLET', { estado: 'INACTIVO' })];
    vista.rerender(<CarteraParametrosSection />); await screen.findByText('Inactivo');
    await act(async () => envio.resolve(registro()));
    expect(mockGet).toHaveBeenCalledTimes(2); expect(screen.queryByText(/creados correctamente/)).not.toBeInTheDocument(); expect(screen.getByText('Inactivo')).toBeInTheDocument();
    expect(agregar()).toBeEnabled();
  });

  it('ignora una consulta antigua después del cambio de actor', async () => {
    const vieja = pendiente<unknown>(); mockGet.mockReturnValueOnce(vieja.promise).mockResolvedValueOnce([registro('AVANCE_WALLET')]);
    const vista = render(<CarteraParametrosSection />); idUsuario = 9002; vista.rerender(<CarteraParametrosSection />);
    await screen.findByText('Activo'); await act(async () => vieja.resolve([]));
    expect(screen.getByRole('button', { name: 'Editar Avance a wallet' })).toBeInTheDocument(); expect(screen.queryByText(/Sin parámetros registrados/)).not.toBeInTheDocument();
  });
});
