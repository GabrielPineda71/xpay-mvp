// XPAY-712 - AVANCE_WALLET: Idempotency-Key por intencion, casilla de
// confirmacion y sin PIN falso. A traves del componente, sin exportar internals.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ApiError, HttpUncertainError } from '../api/client.ts';
import { MiCarteraOrdinariaPage } from './MiCarteraOrdinariaPage.tsx';

const mockGet  = vi.fn();
const mockPost = vi.fn();
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a) };
});

const BASE = '/api/cartera-ordinaria';
const URL_CONFIRMAR = `${BASE}/confirmar-avance-wallet`;
const STORAGE = 'xpay_avance_wallet_idempotency';

let confirmar: () => Promise<unknown>;

const okConfirmacion = () => Promise.resolve({
  idUtilizacion: 77, tipoUtilizacion: 'AVANCE_WALLET', valorCapital: 100000,
  estado: 'DESEMBOLSADO', fechaDesembolso: '2026-10-10T00:00:00Z',
  nuevoSaldoWallet: 100000, nuevoCupoDisponible: 600000, cuotas: [],
});

beforeEach(() => {
  mockGet.mockReset();
  mockPost.mockReset();
  sessionStorage.clear();
  confirmar = okConfirmacion;
  mockGet.mockImplementation((url: string) => {
    if (url === `${BASE}/mi-cupo`) return Promise.resolve({
      idCupo: 1, idWallet: 9, cupoAprobado: 700000, cupoUsado: 0, cupoDisponible: 700000,
      estado: 'ACTIVO', fechaAprobacion: '2026-10-01T00:00:00Z', fechaVencimiento: null,
    });
    if (url === `${BASE}/mis-creditos`) return Promise.resolve([]);
    if (url.startsWith('/api/reportes/wallet/')) return Promise.resolve({ data: { saldoDisponible: 0 } });
    return Promise.reject(new Error(`GET inesperado ${url}`));
  });
  mockPost.mockImplementation((url: string, body: Record<string, unknown>) => {
    if (url === `${BASE}/simular`) return Promise.resolve({
      tipoUtilizacion: body.tipoUtilizacion, valorCapital: body.valorCapital,
      plazoMeses: body.plazoMeses, frecuencia: body.frecuencia,
      tasaEmv: 2, porcAval: 0, porcAdmin: 0, aplicaIva: false, porcIva: 0,
      totalCuotas: body.plazoMeses, valorCuota: 1, valorTotalIntereses: 0,
      valorTotalAval: 0, valorTotalAdmin: 0, valorTotalIva: 0, valorTotalPagar: 1, cuotas: [],
    });
    if (url === URL_CONFIRMAR) return confirmar();
    return Promise.reject(new Error(`POST inesperado ${url}`));
  });
});

const renderPage = () => render(<MemoryRouter><MiCarteraOrdinariaPage /></MemoryRouter>);

async function simularDesembolso(monto = '100000', plazo = '1') {
  fireEvent.click(await screen.findByRole('button', { name: /Desembolsar a mi Wallet/ }));
  fireEvent.change(screen.getByPlaceholderText('Ej: 500000'), { target: { value: monto } });
  fireEvent.change(screen.getByLabelText('Plazo (meses)'), { target: { value: plazo } });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Simular' })).toBeEnabled());
  fireEvent.click(screen.getByRole('button', { name: 'Simular' }));
  await screen.findByRole('button', { name: 'Desembolsar a Wallet' });
}

async function confirmarDesembolso() {
  fireEvent.click(screen.getByRole('checkbox'));
  fireEvent.click(screen.getByRole('button', { name: 'Desembolsar a Wallet' }));
  await waitFor(() => {
    const c = screen.queryByRole('checkbox') as HTMLInputElement | null;
    expect(c === null || !c.checked).toBe(true);
  });
}

const llamadasConfirmar = () => mockPost.mock.calls.filter(c => c[0] === URL_CONFIRMAR);
const keyDe = (i: number) => llamadasConfirmar()[i][2]['Idempotency-Key'] as string;
const guardada = () => JSON.parse(sessionStorage.getItem(STORAGE) ?? 'null');

describe('MiCarteraOrdinariaPage - desembolso AVANCE_WALLET', () => {
  it('I: misma intencion tras error incierto reutiliza la key', async () => {
    confirmar = () => Promise.reject(new HttpUncertainError('timeout'));
    renderPage();
    await simularDesembolso();
    await confirmarDesembolso();
    await confirmarDesembolso();

    expect(llamadasConfirmar()).toHaveLength(2);
    expect(keyDe(1)).toBe(keyDe(0));
  });

  it('J: desmontar y remontar con sessionStorage conserva la key', async () => {
    confirmar = () => Promise.reject(new HttpUncertainError('timeout'));
    const primera = renderPage();
    await simularDesembolso();
    await confirmarDesembolso();
    primera.unmount();

    renderPage();
    await simularDesembolso();
    await confirmarDesembolso();

    expect(llamadasConfirmar()).toHaveLength(2);
    expect(keyDe(1)).toBe(keyDe(0));
  });

  it('K: intencion distinta genera key nueva', async () => {
    confirmar = () => Promise.reject(new HttpUncertainError('timeout'));
    renderPage();
    await simularDesembolso('100000');
    await confirmarDesembolso();
    await simularDesembolso('200000');
    await confirmarDesembolso();

    expect(llamadasConfirmar()).toHaveLength(2);
    expect(keyDe(1)).not.toBe(keyDe(0));
  });

  it('L: POST lleva Idempotency-Key y el body esperado', async () => {
    renderPage();
    await simularDesembolso();
    await confirmarDesembolso();

    const [url, body, headers] = llamadasConfirmar()[0];
    expect(url).toBe(URL_CONFIRMAR);
    expect(body).toEqual({ tipoUtilizacion: 'AVANCE_WALLET', valorCapital: 100000, plazoMeses: 1, frecuencia: 'MENSUAL' });
    expect(headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/);
  });

  // Un replay 200 resuelve post() igual que la primera ejecucion.
  it('M: respuesta exitosa (incluido replay 200) elimina la key', async () => {
    renderPage();
    await simularDesembolso();
    await confirmarDesembolso();

    expect(await screen.findByText(/Desembolso confirmado #77/)).toBeInTheDocument();
    expect(sessionStorage.getItem(STORAGE)).toBeNull();
  });

  it.each([
    ['HttpUncertainError', () => new HttpUncertainError('timeout')],
    ['ApiError 503', () => new ApiError('En curso', 503)],
    ['error de red', () => new Error('Sin conexión')],
  ])('N: %s conserva la key', async (_n, error) => {
    confirmar = () => Promise.reject(error());
    renderPage();
    await simularDesembolso();
    await confirmarDesembolso();

    expect(guardada()).toEqual({ key: keyDe(0), intencion: 'AVANCE_WALLET|100000.00|1|MENSUAL' });
  });

  it('O: sin la casilla no se puede confirmar', async () => {
    renderPage();
    await simularDesembolso();
    const btn = screen.getByRole('button', { name: 'Desembolsar a Wallet' });

    expect(screen.getByRole('checkbox')).not.toBeChecked();
    expect(btn).toBeDisabled();
    fireEvent.click(btn);
    expect(llamadasConfirmar()).toHaveLength(0);
  });

  it('P: doble clic con POST pendiente envia un solo POST', async () => {
    let resolver: (v: unknown) => void = () => {};
    confirmar = () => new Promise(r => { resolver = r; });
    renderPage();
    await simularDesembolso();
    fireEvent.click(screen.getByRole('checkbox'));
    const btn = screen.getByRole('button', { name: 'Desembolsar a Wallet' });
    fireEvent.click(btn);
    fireEvent.click(btn);
    fireEvent.click(screen.getByRole('button', { name: 'Desembolsando…' }));

    expect(llamadasConfirmar()).toHaveLength(1);
    resolver(await okConfirmacion());
    expect(await screen.findByText(/Desembolso confirmado #77/)).toBeInTheDocument();
  });

  it('Q: AVANCE_WALLET no pide PIN y muestra la nota factual', async () => {
    const { container } = renderPage();
    await simularDesembolso();

    expect(screen.getByText('Esta confirmación no solicita un PIN adicional.')).toBeInTheDocument();
    expect(screen.queryByPlaceholderText('·······')).toBeNull();
    expect(container.querySelector('input[type="password"]')).toBeNull();
    expect(screen.queryByLabelText(/PIN/i)).toBeNull();
  });

  it('R: nueva simulacion y cambio de modo resetean la casilla', async () => {
    renderPage();
    await simularDesembolso();
    fireEvent.click(screen.getByRole('checkbox'));
    expect(screen.getByRole('checkbox')).toBeChecked();

    fireEvent.click(screen.getByRole('button', { name: 'Simular' }));
    await waitFor(() => expect(screen.getByRole('checkbox')).not.toBeChecked());

    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.click(screen.getByRole('button', { name: /Compra en comercio/ }));
    expect(screen.queryByRole('checkbox')).toBeNull();
    await simularDesembolso();
    expect(screen.getByRole('checkbox')).not.toBeChecked();
  });
});
