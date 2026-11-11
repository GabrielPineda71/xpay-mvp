// D5 — retiros inciertos/huérfanos: confirmación explícita, evidencia de
// Passport y precondiciones visibles. get/post mockeados; sin dinero real.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RetirosRevisionAdmin, esHuerfano, resolucionVencida, type EnvioIncierto, type RetiroRevision } from './RetirosRevisionAdmin.tsx';
import { ApiError } from '../api/client.ts';

const mockGet  = vi.fn();
const mockPost = vi.fn();
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a) };
});

const AHORA = new Date('2026-11-10T15:00:00Z');
const hace = (min: number) => new Date(AHORA.getTime() - min * 60_000).toISOString();

const incierto = (p: Partial<EnvioIncierto> = {}): EnvioIncierto => ({
  idBrebRetiro: 9, valor: 100_000, estado: 'ENVIADO_PASSPORT', fechaSolicitud: hace(200),
  fechaEnvioPassport: hace(190), resolucionExpiraUtc: hace(60), antiguedadMinutos: 190, ...p,
});
const EVIDENCIA = 'Caso Passport 12345 del 2026-11-10: pago inexistente confirmado por escrito.';

function renderizar(retiros: RetiroRevision[], inciertos: EnvioIncierto[] = []) {
  mockGet.mockResolvedValue({ success: true, data: inciertos });
  const onCambio = vi.fn();
  render(<RetirosRevisionAdmin retiros={retiros} onCambio={onCambio} />);
  return onCambio;
}

describe('reglas visibles', () => {
  it('huérfano: solo PENDIENTE_ENVIO_PASSPORT con más de 15 min', () => {
    const r = { idBrebRetiro: 1, valor: 1, estado: 'PENDIENTE_ENVIO_PASSPORT', fechaSolicitud: hace(16) };
    expect(esHuerfano(r, AHORA)).toBe(true);
    expect(esHuerfano({ ...r, fechaSolicitud: hace(10) }, AHORA)).toBe(false);
    expect(esHuerfano({ ...r, estado: 'ENVIADO_PASSPORT' }, AHORA)).toBe(false);
  });
  it('resolución: vencida solo con fecha conocida y pasada', () => {
    expect(resolucionVencida(incierto(), AHORA)).toBe(true);
    expect(resolucionVencida(incierto({ resolucionExpiraUtc: null }), AHORA)).toBe(false);
    expect(resolucionVencida(incierto({ resolucionExpiraUtc: new Date(AHORA.getTime() + 60_000).toISOString() }), AHORA)).toBe(false);
  });
});

describe('RetirosRevisionAdmin', () => {
  beforeEach(() => {
    mockGet.mockReset(); mockPost.mockReset();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(AHORA);
  });
  afterEach(() => { vi.useRealTimers(); });

  it('sin casos: lo indica y no ofrece acciones', async () => {
    renderizar([]);
    expect(await screen.findByText('No hay retiros pendientes de revisión.')).toBeInTheDocument();
    expect(mockGet).toHaveBeenCalledWith('/api/breb/admin/retiros/envio-incierto');
  });

  it('rechazo confirmado exige evidencia y certificación antes de llamar al backend', async () => {
    mockPost.mockResolvedValue({ success: true, data: { estado: 'RECHAZADO' } });
    const user = userEvent.setup();
    const onCambio = renderizar([], [incierto()]);
    await user.click(await screen.findByRole('button', { name: 'Resolver como rechazo confirmado #9' }));

    await user.click(screen.getByRole('button', { name: 'Registrar rechazo confirmado' }));
    expect(screen.getByRole('alert')).toHaveTextContent('evidencia de Passport es obligatoria');
    await user.type(screen.getByLabelText(/Evidencia de Passport/), 'corta');
    await user.click(screen.getByRole('button', { name: 'Registrar rechazo confirmado' }));
    expect(screen.getByRole('alert')).toHaveTextContent('20-500');

    await user.clear(screen.getByLabelText(/Evidencia de Passport/));
    await user.type(screen.getByLabelText(/Evidencia de Passport/), EVIDENCIA);
    await user.click(screen.getByRole('button', { name: 'Registrar rechazo confirmado' }));
    expect(screen.getByRole('alert')).toHaveTextContent('certificar');
    expect(mockPost).not.toHaveBeenCalled();

    await user.click(screen.getByLabelText(/Certifico que Passport confirmó/));
    await user.click(screen.getByRole('button', { name: 'Registrar rechazo confirmado' }));
    await waitFor(() => expect(onCambio).toHaveBeenCalledTimes(1));
    expect(mockPost).toHaveBeenCalledWith(
      '/api/breb/admin/retiros/9/resolver-envio-incierto/rechazo-confirmado', { evidencia: EVIDENCIA });
    expect(screen.getByRole('status')).toHaveTextContent('RECHAZADO');
  });

  it('resolución no vencida: no se puede resolver', async () => {
    renderizar([], [incierto({ resolucionExpiraUtc: null })]);
    expect(await screen.findByText(/No se puede resolver todavía/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Resolver como rechazo confirmado/ })).not.toBeInTheDocument();
  });

  it('el rechazo del backend se muestra y no se da por resuelto', async () => {
    mockPost.mockRejectedValueOnce(new ApiError('El retiro ya tiene una resolución de envío incierto registrada.', 400));
    const user = userEvent.setup();
    const onCambio = renderizar([], [incierto()]);
    await user.click(await screen.findByRole('button', { name: 'Resolver como rechazo confirmado #9' }));
    await user.type(screen.getByLabelText(/Evidencia de Passport/), EVIDENCIA);
    await user.click(screen.getByLabelText(/Certifico que Passport confirmó/));
    await user.click(screen.getByRole('button', { name: 'Registrar rechazo confirmado' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Retiro #9: El retiro ya tiene una resolución');
    expect(onCambio).not.toHaveBeenCalled();
  });

  it('liberar huérfano pide confirmación y usa el endpoint del retiro', async () => {
    mockPost.mockResolvedValue({ success: true, data: { estado: 'ERROR' } });
    const user = userEvent.setup();
    renderizar([
      { idBrebRetiro: 4, valor: 50_000, estado: 'PENDIENTE_ENVIO_PASSPORT', fechaSolicitud: hace(30) },
      { idBrebRetiro: 5, valor: 50_000, estado: 'PENDIENTE_ENVIO_PASSPORT', fechaSolicitud: hace(5) },
    ]);
    expect(screen.queryByRole('button', { name: 'Liberar retiro huérfano #5' })).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Liberar retiro huérfano #4' }));
    expect(mockPost).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Confirmar liberación' }));
    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/breb/admin/retiros/4/liberar-pendiente-envio', {}));
  });

  it('con payment_id: consulta el estado a Passport, sin decisión manual', async () => {
    mockPost.mockResolvedValue({ success: true, data: { estado: 'LIQUIDADO' } });
    const user = userEvent.setup();
    renderizar([{ idBrebRetiro: 7, valor: 80_000, estado: 'ENVIADO_PASSPORT', fechaSolicitud: hace(30), paymentIdFingerprint: 'ab12' }]);
    await user.click(await screen.findByRole('button', { name: 'Consultar estado en Passport #7' }));
    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/breb/admin/retiros/7/actualizar-estado', {}));
    expect(await screen.findByRole('status')).toHaveTextContent('LIQUIDADO');
  });
});
