// D1 — formulario de alta del piloto. CERO red real: `post` mockeado;
// ApiError/HttpUncertainError reales (importActual).
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  AltaUsuarioPilotoForm, validarAltaPiloto,
  MENSAJE_ALTA_CERRADA, MENSAJE_INCIERTO, MENSAJE_SOLO_SUPERUSUARIO,
} from './AltaUsuarioPilotoForm.tsx';
import { ApiError, HttpUncertainError } from '../api/client.ts';

const mockPost = vi.fn();

vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, post: (...args: unknown[]) => mockPost(...args) };
});

// Valores de PRUEBA del formulario: no son versiones legales reales.
const VALIDO = {
  nombreUsuario: 'piloto.prueba', email: 'piloto.prueba@example.test', celular: '3000000000',
  versionTerminos: 'TEST-T1', versionPoliticaDatos: 'TEST-P1', versionAutorizacionKyc: 'TEST-K1',
  fechaAceptacion: '2026-11-01T10:30', referenciaEvidencia: 'TEST/EVID-001',
};

async function llenar(user: ReturnType<typeof userEvent.setup>, c: Partial<typeof VALIDO> = VALIDO) {
  const etiquetas: [keyof typeof VALIDO, string][] = [
    ['nombreUsuario', 'Nombre de usuario'], ['email', 'Email'], ['celular', 'Celular'],
    ['versionTerminos', 'Versión de términos y condiciones'],
    ['versionPoliticaDatos', 'Versión de política de datos'],
    ['versionAutorizacionKyc', 'Versión de autorización KYC'],
    ['referenciaEvidencia', 'Referencia de la evidencia'],
  ];
  for (const [k, etiqueta] of etiquetas) if (c[k]) await user.type(screen.getByLabelText(etiqueta), c[k]!);
  if (c.fechaAceptacion) {
    const fecha = screen.getByLabelText('Fecha y hora de aceptación') as HTMLInputElement;
    await user.clear(fecha);
    await user.type(fecha, c.fechaAceptacion);
  }
}

const confirmar = (user: ReturnType<typeof userEvent.setup>) =>
  user.click(screen.getByLabelText(/Confirmo que el usuario aceptó/));
const enviar = (user: ReturnType<typeof userEvent.setup>) =>
  user.click(screen.getByRole('button', { name: 'Crear usuario del piloto' }));

describe('validarAltaPiloto', () => {
  const ahora = new Date('2026-11-10T12:00:00Z');

  it('acepta datos completos con formato válido', () => {
    expect(validarAltaPiloto(VALIDO, ahora)).toBeNull();
  });

  it('exige cada versión legal y rechaza caracteres fuera del formato', () => {
    expect(validarAltaPiloto({ ...VALIDO, versionTerminos: '' }, ahora)).toMatch(/términos y condiciones/);
    expect(validarAltaPiloto({ ...VALIDO, versionPoliticaDatos: 'v 1' }, ahora)).toMatch(/política de tratamiento/);
    expect(validarAltaPiloto({ ...VALIDO, versionAutorizacionKyc: 'x'.repeat(51) }, ahora)).toMatch(/autorización KYC/);
  });

  it('rechaza una fecha de aceptación futura', () => {
    expect(validarAltaPiloto({ ...VALIDO, fechaAceptacion: '2027-01-01T00:00' }, ahora)).toMatch(/futura/);
  });

  it('exige la referencia de evidencia sin datos libres', () => {
    expect(validarAltaPiloto({ ...VALIDO, referenciaEvidencia: '' }, ahora)).toMatch(/evidencia/);
    expect(validarAltaPiloto({ ...VALIDO, referenciaEvidencia: 'Juan Pérez CC 123' }, ahora)).toMatch(/evidencia/);
  });
});

describe('AltaUsuarioPilotoForm', () => {
  beforeEach(() => { mockPost.mockReset(); });

  it('no propone versiones legales: los campos legales empiezan vacíos', () => {
    render(<AltaUsuarioPilotoForm onCreado={vi.fn()} />);
    for (const etiqueta of ['Versión de términos y condiciones', 'Versión de política de datos',
      'Versión de autorización KYC', 'Referencia de la evidencia']) {
      const input = screen.getByLabelText(etiqueta) as HTMLInputElement;
      expect(input.value).toBe('');
      expect(input.placeholder).toBe('');
    }
    expect(screen.getByRole('note')).toHaveTextContent('asesoría legal');
  });

  it('sin la confirmación no envía nada', async () => {
    const user = userEvent.setup();
    render(<AltaUsuarioPilotoForm onCreado={vi.fn()} />);
    await llenar(user);
    await enviar(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('Debes confirmar');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('con datos inválidos no envía nada', async () => {
    const user = userEvent.setup();
    render(<AltaUsuarioPilotoForm onCreado={vi.fn()} />);
    await llenar(user, { ...VALIDO, referenciaEvidencia: '' });
    await confirmar(user);
    await enviar(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('referencia de evidencia');
    expect(mockPost).not.toHaveBeenCalled();
  });

  it('envía el contrato del backend con la fecha en UTC y avisa al crear', async () => {
    mockPost.mockResolvedValue({ success: true, data: { idUsuario: 7, idWallet: 9, siguiente: 'x' } });
    const onCreado = vi.fn();
    const user = userEvent.setup();
    render(<AltaUsuarioPilotoForm onCreado={onCreado} />);
    await llenar(user);
    await confirmar(user);
    await enviar(user);

    await waitFor(() => expect(onCreado).toHaveBeenCalledWith({ idUsuario: 7, idWallet: 9, siguiente: 'x' }));
    expect(mockPost).toHaveBeenCalledTimes(1);
    const [url, body] = mockPost.mock.calls[0];
    expect(url).toBe('/api/admin/usuarios/piloto');
    expect(body).toEqual({
      nombreUsuario: 'piloto.prueba', email: 'piloto.prueba@example.test', celular: '3000000000',
      versionTerminos: 'TEST-T1', versionPoliticaDatos: 'TEST-P1', versionAutorizacionKyc: 'TEST-K1',
      fechaAceptacionUtc: new Date('2026-11-01T10:30').toISOString(),
      referenciaEvidencia: 'TEST/EVID-001',
    });
    expect(Object.keys(body)).not.toContain('password');
  });

  it.each([
    [new ApiError('x', 404), MENSAJE_ALTA_CERRADA],
    [new ApiError('x', 403), MENSAJE_SOLO_SUPERUSUARIO],
    [new ApiError('Se alcanzó el número máximo de usuarios del piloto.', 400), 'Se alcanzó el número máximo de usuarios del piloto.'],
    [new HttpUncertainError('t'), MENSAJE_INCIERTO],
  ])('muestra el error correcto del backend (%s)', async (error, mensaje) => {
    mockPost.mockRejectedValue(error);
    const onCreado = vi.fn();
    const user = userEvent.setup();
    render(<AltaUsuarioPilotoForm onCreado={onCreado} />);
    await llenar(user);
    await confirmar(user);
    await enviar(user);
    expect(await screen.findByRole('alert')).toHaveTextContent(mensaje);
    expect(onCreado).not.toHaveBeenCalled();
  });
});
