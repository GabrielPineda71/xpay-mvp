// D4 — el pago simulado QA no aparece en el build PROD; el recaudo real
// (pestaña "Recaudo (lote y QR)") sí. get/post mockeados.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MiEmpresaLibranzaPage } from './MiEmpresaLibranzaPage.tsx';

const mockGet = vi.fn();
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...a: unknown[]) => mockGet(...a), post: vi.fn(), postForm: vi.fn() };
});

const convenio = {
  idConvenio: 3, nombreEmpresa: 'Empresa Prueba', nit: '900000000', estado: 'ACTIVO', periodicidadPago: 'MENSUAL',
  diaPago1: 30, porcentajeMaximoCupo: 30, totalEmpleados: 1, empleadosActivos: 1, rolEmpresa: 'ADMIN_EMPRESA',
};
const cobro = {
  idAnticipo: 77, idEmpleado: 1, nombresEmpleado: 'Ana Prueba', numeroDocumento: '1', tipoDocumento: 'CC',
  diaPagoCorte: 30, fechaPagoProgramada: '2026-11-30', valorSolicitado: 500000, valorComision: 12605,
  valorIva: 2395, valorTotalACobrar: 515000, momentoCobroComision: 'VENCIDO', estado: 'DESEMBOLSADO',
};

function rutas() {
  mockGet.mockImplementation((url: string) => {
    if (url.includes('mis-convenios')) return Promise.resolve({ success: true, data: [{ ...convenio }] });
    if (url.includes('mi-convenio'))   return Promise.resolve({ success: true, data: convenio });
    if (url.includes('/cobros'))       return Promise.resolve({ success: true, data: [cobro] });
    return Promise.resolve({ success: true, data: [] });
  });
}

async function verCobros() {
  const user = userEvent.setup();
  render(<MiEmpresaLibranzaPage />);
  await user.click(await screen.findByRole('button', { name: 'Cobros' }));
  const fecha = document.querySelector('input[type="date"]') as HTMLInputElement;
  await user.type(fecha, '2026-11-30');
  await user.click(screen.getByRole('button', { name: 'Ver cobros' }));
  await screen.findByText('Ana Prueba');
  return user;
}

describe('MiEmpresaLibranzaPage — D4', () => {
  beforeEach(() => { mockGet.mockReset(); rutas(); });
  afterEach(() => { vi.unstubAllEnvs(); });

  it('PROD: sin "Aplicar pago QA" y con la pestaña de recaudo real', async () => {
    vi.stubEnv('VITE_XPAY_ENV', 'PROD');
    const user = await verCobros();
    expect(screen.queryByText('Aplicar pago QA (simulado)')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aplicar pago QA' })).not.toBeInTheDocument();
    expect(screen.getByText(/usa la pestaña "Recaudo \(lote y QR\)"/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Recaudo (lote y QR)' }));
    expect(await screen.findByRole('heading', { name: 'Recaudo por lote y QR' })).toBeInTheDocument();
  });

  it('QA: el pago simulado sigue disponible para pruebas', async () => {
    vi.stubEnv('VITE_XPAY_ENV', 'QA');
    await verCobros();
    expect(screen.getByRole('button', { name: 'Aplicar pago QA' })).toBeInTheDocument();
  });
});
