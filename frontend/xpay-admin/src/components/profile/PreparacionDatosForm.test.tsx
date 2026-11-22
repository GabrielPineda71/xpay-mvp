import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PreparacionDatosForm } from './PreparacionDatosForm.tsx';
import type { EstadoPreparacionWallet } from './preparacionWallet.ts';
const mockGet = vi.fn();
vi.mock('../../api/client.ts', () => ({ get: (...a: unknown[]) => mockGet(...a) }));
const estado = { direccion: null, idDepartamento: null, idCiudad: null } as EstadoPreparacionWallet;
const deps = [{ idDepartamento: 1, nombre: 'Depto Uno' }, { idDepartamento: 2, nombre: 'Depto Dos' }];
const ciudad = (idCiudad: number, nombre: string) => ({ idCiudad, nombre });
beforeEach(() => {
  mockGet.mockReset();
  mockGet.mockImplementation((url: string) => Promise.resolve({ success: true, data: url === '/api/catalogos/paises'
    ? [{ idPais: 10, codigo: 'COL', nombre: 'Colombia' }] : url.includes('/departamentos') ? deps : [ciudad(11, 'Ciudad Uno')] }));
});
describe('Domicilio: catálogo DANE y pertenencia de ciudad', () => {
  it('envía IDs del catálogo, dirección sin espacios y no nombres libres', async () => {
    const guardar = vi.fn().mockResolvedValue(true);
    render(<PreparacionDatosForm estado={estado} ocupado={false} onGuardar={guardar} />);
    await screen.findByRole('option', { name: 'Depto Uno' });
    await userEvent.type(screen.getByLabelText('Dirección de residencia'), '  Calle de prueba 99  ');
    await userEvent.selectOptions(screen.getByLabelText('Departamento'), '1');
    await screen.findByRole('option', { name: 'Ciudad Uno' });
    await userEvent.selectOptions(screen.getByLabelText('Ciudad o municipio'), '11');
    await userEvent.click(screen.getByRole('button', { name: 'Guardar y continuar' }));
    expect(guardar).toHaveBeenCalledExactlyOnceWith({ direccion: 'Calle de prueba 99', idDepartamento: 1, idCiudad: 11 });
    expect(mockGet).toHaveBeenCalledWith('/api/catalogos/departamentos?idPais=10');
  });
  it('descarta una ciudad elegida cuando cambia el departamento y rechaza respuestas tardías', async () => {
    let resolverUno!: (r: unknown) => void;
    mockGet.mockImplementation((url: string) => {
      if (url === '/api/catalogos/paises') return Promise.resolve({ success: true, data: [{ idPais: 10, codigo: 'COL' }] });
      if (url.includes('/departamentos')) return Promise.resolve({ success: true, data: deps });
      if (url.endsWith('=1')) return new Promise(resolve => { resolverUno = resolve; });
      return Promise.resolve({ success: true, data: [ciudad(22, 'Ciudad Dos')] });
    });
    const guardar = vi.fn(); render(<PreparacionDatosForm estado={estado} ocupado={false} onGuardar={guardar} />);
    await screen.findByRole('option', { name: 'Depto Uno' });
    await userEvent.selectOptions(screen.getByLabelText('Departamento'), '1');
    await userEvent.selectOptions(screen.getByLabelText('Departamento'), '2');
    await screen.findByRole('option', { name: 'Ciudad Dos' });
    await act(async () => { resolverUno({ success: true, data: [ciudad(11, 'Ciudad Uno')] }); });
    expect(screen.queryByRole('option', { name: 'Ciudad Uno' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Ciudad o municipio')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Guardar y continuar' })).toBeDisabled();
    expect(guardar).not.toHaveBeenCalled();
  });
  it('no ofrece escritura libre ni envío si faltan ubicaciones oficiales', async () => {
    mockGet.mockRejectedValue(new Error('Catálogo caído.'));
    const guardar = vi.fn(); render(<PreparacionDatosForm estado={estado} ocupado={false} onGuardar={guardar} />);
    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos cargar las ubicaciones');
    expect(screen.getByRole('button', { name: 'Guardar y continuar' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Actualizar listas' }));
    await waitFor(() => expect(mockGet).toHaveBeenCalledTimes(2)); expect(guardar).not.toHaveBeenCalled();
  });
});
