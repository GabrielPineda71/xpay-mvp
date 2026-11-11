// D3 — recaudo por lote y QR desde la vista de empresa. CERO red real:
// get/post mockeados; qrcode mockeado (no se dibuja nada).
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RecaudoLotesEmpresa, MENSAJE_QR_INCIERTO, puedeOperarRecaudo, type Lote } from './RecaudoLotesEmpresa.tsx';
import { ApiError, HttpUncertainError } from '../api/client.ts';

const mockGet  = vi.fn();
const mockPost = vi.fn();
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a) };
});
vi.mock('qrcode', () => ({ default: { toDataURL: vi.fn().mockResolvedValue('data:image/png;base64,QR') } }));

function lote(p: Partial<Lote> = {}): Lote {
  return {
    idLote: 5, fechaCorte: '2026-11-30', estado: 'BORRADOR', referenciaPublica: 'XPL-ABC123',
    montoTotal: 515_000, cantidadDetalles: 1, fechaCreacion: '2026-11-10T10:00:00Z',
    qrEstado: null, qrCodeData: null, fechaAplicacion: null, motivoRevision: null,
    detalles: [{ idDetalle: 1, idAnticipo: 77, nombresEmpleado: 'Ana Prueba', montoCapital: 500_000,
      montoComision: 12_605, montoIva: 2_395, montoTotal: 515_000, estado: 'VIGENTE' }],
    ...p,
  };
}

function rutasGet(lotes: Lote[], detalle: Lote) {
  mockGet.mockImplementation((url: string) => Promise.resolve(
    url.startsWith('/api/libranza/empresa/lotes?') ? { success: true, data: lotes } : { success: true, data: detalle }));
}

describe('puedeOperarRecaudo', () => {
  it('solo ADMIN_EMPRESA y OPERADOR_EMPRESA operan', () => {
    expect(puedeOperarRecaudo('ADMIN_EMPRESA')).toBe(true);
    expect(puedeOperarRecaudo('OPERADOR_EMPRESA')).toBe(true);
    expect(puedeOperarRecaudo('CONSULTA_EMPRESA')).toBe(false);
    expect(puedeOperarRecaudo('')).toBe(false);
  });
});

describe('RecaudoLotesEmpresa', () => {
  beforeEach(() => { mockGet.mockReset(); mockPost.mockReset(); });

  it('CONSULTA_EMPRESA ve lotes pero no puede crear, generar QR ni cancelar', async () => {
    rutasGet([lote()], lote());
    const user = userEvent.setup();
    render(<RecaudoLotesEmpresa idConvenio={3} rolEmpresa="CONSULTA_EMPRESA" />);
    await user.click(await screen.findByRole('button', { name: 'Ver lote #5' }));
    await screen.findByText(/Lote #5 · corte 2026-11-30/);
    expect(screen.queryByRole('button', { name: 'Crear lote del corte' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Generar QR de pago' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar lote' })).not.toBeInTheDocument();
  });

  it('crea el lote del corte con la fecha indicada y muestra sus anticipos', async () => {
    rutasGet([], lote());
    mockPost.mockResolvedValue({ success: true, data: lote() });
    const user = userEvent.setup();
    render(<RecaudoLotesEmpresa idConvenio={3} rolEmpresa="ADMIN_EMPRESA" />);
    await user.click(await screen.findByRole('button', { name: 'Crear lote del corte' }));
    expect(screen.getByRole('alert')).toHaveTextContent('fecha de pago');
    expect(mockPost).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText('Fecha de pago (corte)'), '2026-11-30');
    await user.click(screen.getByRole('button', { name: 'Crear lote del corte' }));
    await screen.findByText('Ana Prueba');
    expect(mockPost).toHaveBeenCalledWith('/api/libranza/empresa/lotes?idConvenio=3', { fechaCorte: '2026-11-30' });
    expect(screen.getByText('XPL-ABC123')).toBeInTheDocument();
  });

  it('genera el QR con timeout ampliado y lo muestra con el valor exacto', async () => {
    const pendiente = lote({ estado: 'PENDIENTE_PAGO', qrEstado: 'GENERADO', qrCodeData: '000201...' });
    rutasGet([lote()], lote());
    mockPost.mockResolvedValue({ success: true, data: pendiente });
    const user = userEvent.setup();
    render(<RecaudoLotesEmpresa idConvenio={3} rolEmpresa="OPERADOR_EMPRESA" />);
    await user.click(await screen.findByRole('button', { name: 'Ver lote #5' }));
    await user.click(await screen.findByRole('button', { name: 'Generar QR de pago' }));

    expect(await screen.findByAltText('QR de pago del lote 5')).toHaveAttribute('src', 'data:image/png;base64,QR');
    const [url, body, headers, timeout] = mockPost.mock.calls[0];
    expect(url).toBe('/api/libranza/empresa/lotes/5/qr?idConvenio=3');
    expect(body).toEqual({});
    expect(headers).toBeUndefined();
    expect(timeout).toBe(45_000);
    expect(screen.getByText(/Paga el valor exacto/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Generar QR de pago' })).not.toBeInTheDocument();
  });

  it.each([
    ['timeout', new HttpUncertainError('t')],
    ['502 Passport', new ApiError('No fue posible generar el QR. Intente de nuevo.', 502)],
  ])('QR incierto (%s): avisa que se reintenta con la misma referencia', async (_n, error) => {
    rutasGet([lote()], lote());
    mockPost.mockRejectedValueOnce(error);
    const user = userEvent.setup();
    render(<RecaudoLotesEmpresa idConvenio={3} rolEmpresa="ADMIN_EMPRESA" />);
    await user.click(await screen.findByRole('button', { name: 'Ver lote #5' }));
    await user.click(await screen.findByRole('button', { name: 'Generar QR de pago' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(MENSAJE_QR_INCIERTO);
  });

  it('interruptor financiero cerrado (503): muestra el mensaje del backend', async () => {
    rutasGet([], lote());
    mockPost.mockRejectedValueOnce(new ApiError('Las operaciones financieras están deshabilitadas.', 503));
    const user = userEvent.setup();
    render(<RecaudoLotesEmpresa idConvenio={3} rolEmpresa="ADMIN_EMPRESA" />);
    await user.type(await screen.findByLabelText('Fecha de pago (corte)'), '2026-11-30');
    await user.click(screen.getByRole('button', { name: 'Crear lote del corte' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('deshabilitadas');
  });

  it('cancelar exige una confirmación explícita', async () => {
    rutasGet([lote()], lote());
    mockPost.mockResolvedValue({ success: true, data: lote({ estado: 'CANCELADO' }) });
    const user = userEvent.setup();
    render(<RecaudoLotesEmpresa idConvenio={3} rolEmpresa="ADMIN_EMPRESA" />);
    await user.click(await screen.findByRole('button', { name: 'Ver lote #5' }));
    await user.click(await screen.findByRole('button', { name: 'Cancelar lote' }));
    expect(mockPost).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Confirmar cancelación' }));
    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/libranza/empresa/lotes/5/cancelar?idConvenio=3', {}));
    expect(await screen.findByText(/Lote cancelado antes de cualquier pago/)).toBeInTheDocument();
  });

  it.each(['PAGO_EN_PROCESO', 'APLICADO', 'REQUIERE_REVISION'])(
    'lote %s: sin generar QR ni cancelar', async (estado) => {
      const l = lote({ estado, motivoRevision: estado === 'REQUIERE_REVISION' ? 'MONTO_NO_COINCIDE' : null });
      rutasGet([l], l);
      const user = userEvent.setup();
      render(<RecaudoLotesEmpresa idConvenio={3} rolEmpresa="ADMIN_EMPRESA" />);
      await user.click(await screen.findByRole('button', { name: 'Ver lote #5' }));
      await screen.findByText(new RegExp(`Lote #5 · corte 2026-11-30 · ${estado}`));
      expect(screen.queryByRole('button', { name: 'Generar QR de pago' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Cancelar lote' })).not.toBeInTheDocument();
      if (estado === 'REQUIERE_REVISION') expect(screen.getByText(/no vuelvas a pagar/)).toBeInTheDocument();
    });
});
