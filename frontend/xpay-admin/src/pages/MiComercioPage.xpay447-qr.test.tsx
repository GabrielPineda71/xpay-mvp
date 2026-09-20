// XPAY-447 — fix del bug confirmado en XPAY-446: la sección "QR del
// comercio" de MiComercioPage.tsx mostraba siempre un código QR hardcodeado
// ("QR-DEMO-XPAY-QA-001"), sin relación con el comercio realmente
// autenticado. Cubre F1-F10 del ticket. CERO red real: api/client.ts está
// completamente mockeado con un router por ruta.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MiComercioPage } from './MiComercioPage.tsx';

const mockGet  = vi.fn();
const mockPost = vi.fn();
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return { ...actual, get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a) };
});

vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual('../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: { usuario: 'qa.uat.comercio1' }, login: vi.fn(), logout: vi.fn(), actualizarToken: vi.fn() }) };
});

interface QrFixture {
  idQr: number;
  codigoQr: string;
  idTienda: number;
  nombreTienda: string | null;
  estado: string;
}

const RESUMEN_FIXTURE = {
  saldoDisponible: 0, totalVentas: 0, valorTotalVentas: 0, ventasContingencia: 0, ventasLiquidadas: 0,
};

const RESUMEN_DISPONIBILIDAD_FIXTURE = {
  totalNoDisponibleBruto: 0, totalDescuentoConvenio: 0, totalIvaConvenio: 0,
  totalNoDisponibleNetoProgramado: 0, totalDisponibleBruto: 0, totalLiquidado: 0,
  cantidadNoDisponible: 0, valorEstimadoProximaLiberacion: 0,
};

let idComercioActual = 3;
let qrResponseMode: 'ok' | 'error' = 'ok';
let qrFixtures: QrFixture[] = [];

function scopeFixture() {
  return {
    idUsuario: 17, rolComercio: 'ADMIN_COMERCIO', idComercioAliado: 1,
    idComercioExistente: idComercioActual, idEstablecimiento: undefined,
    puedeVerTodoComercio: true, puedeDisponerRecursos: true, puedeLiquidarAnticipado: true,
    puedeEnviarBreb: true, puedeAnularVentasDiaActual: true, puedeGenerarQr: false,
  };
}

function apiRoutedGet(path: string) {
  if (path === '/api/comercio/mi-scope') return Promise.resolve({ success: true, data: scopeFixture() });
  if (path === '/api/comercio/dashboard') return Promise.resolve({ success: true, data: RESUMEN_FIXTURE });
  if (path.startsWith('/api/comercio/ventas?')) return Promise.resolve({ success: true, data: { items: [] } });
  if (path.startsWith('/api/comercios/retiros')) return Promise.resolve({ success: true, data: { items: [] } });
  if (path === '/api/comercio/mi-qr') {
    if (qrResponseMode === 'error') return Promise.reject(new Error('boom'));
    return Promise.resolve({ success: true, data: qrFixtures });
  }
  if (path.startsWith('/api/breb/mi-llave/comercio')) return Promise.resolve({ success: true, data: null });
  if (path.startsWith('/api/breb/mis-retiros/comercio')) return Promise.resolve({ success: true, data: [] });
  if (path.startsWith('/api/comercio/ventas-disponibilidad/resumen')) return Promise.resolve({ success: true, data: RESUMEN_DISPONIBILIDAD_FIXTURE });
  if (path.startsWith('/api/comercio/ventas-no-disponibles')) return Promise.resolve({ success: true, data: [] });
  if (path === '/api/comercio/wallet-recargas/mis-recargas') return Promise.resolve({ success: true, data: [] });
  if (path === '/api/comercio/wallet-cierres/mis-cierres') return Promise.resolve({ success: true, data: [] });
  return Promise.reject(new Error(`unmocked GET ${path}`));
}

function qr(overrides: Partial<QrFixture> & { idQr: number; codigoQr: string }): QrFixture {
  return { idTienda: 1, nombreTienda: 'Tienda Uno', estado: 'ACTIVO', ...overrides };
}

async function waitForLoaded() {
  await waitFor(() => expect(screen.getByText('Saldo disponible')).toBeInTheDocument());
}

function qrSection() {
  return screen.getByText('QR del comercio').closest('div') as HTMLElement;
}

beforeEach(() => {
  idComercioActual = 3;
  qrResponseMode = 'ok';
  qrFixtures = [];
  mockGet.mockReset();
  mockGet.mockImplementation(apiRoutedGet);
  mockPost.mockReset();
});

describe('XPAY-447 — MiComercioPage QR real del comercio', () => {
  // F1 — un solo QR: muestra CodigoQr real y NombreTienda.
  it('F1: con exactamente un QR activo, muestra su CodigoQr real y su tienda', async () => {
    qrFixtures = [qr({ idQr: 3, codigoQr: 'QR-UAT-XPAY-001', idTienda: 3, nombreTienda: 'QA UAT Tienda 1' })];
    render(<MiComercioPage />);
    await waitForLoaded();

    const section = within(qrSection());
    await waitFor(() => expect(section.getByText('QR-UAT-XPAY-001')).toBeInTheDocument());
    expect(section.getByText('QA UAT Tienda 1')).toBeInTheDocument();
    expect(section.queryByText(/QR-DEMO-XPAY-QA-001/)).toBeNull();
  });

  // F2/F3 — el payload generado contiene el CodigoQr real y el monto opcional embebido.
  it('F2-F3: el QR generado embebe el CodigoQr real y el monto opcional', async () => {
    qrFixtures = [qr({ idQr: 3, codigoQr: 'QR-UAT-XPAY-001', nombreTienda: 'QA UAT Tienda 1' })];
    render(<MiComercioPage />);
    await waitForLoaded();
    const section = within(qrSection());
    await waitFor(() => expect(section.getByRole('button', { name: /Generar QR comercio/ })).not.toBeDisabled());

    const valorInput = section.getByLabelText(/Valor \(opcional/);
    await userEvent.type(valorInput, '100');
    await userEvent.click(section.getByRole('button', { name: /Generar QR comercio/ }));

    await waitFor(() => expect(section.getByAltText('QR del comercio')).toBeInTheDocument());
    expect(section.getByText(/qrCode=QR-UAT-XPAY-001/)).toBeInTheDocument();
  });

  // F4 — el QR demo hardcodeado no aparece para un comercio distinto.
  it('F4: el código demo hardcodeado nunca aparece para un comercio con su propio QR', async () => {
    qrFixtures = [qr({ idQr: 2, codigoQr: 'QR-DISTINTO-002', nombreTienda: 'Otra Tienda' })];
    render(<MiComercioPage />);
    await waitForLoaded();
    const section = within(qrSection());
    await waitFor(() => expect(section.getByText('QR-DISTINTO-002')).toBeInTheDocument());
    expect(section.queryByText(/QR-DEMO-XPAY-QA-001/)).toBeNull();
  });

  // F5 — cero QR: mensaje claro y generación deshabilitada/ausente.
  it('F5: sin QR activos muestra mensaje claro y no permite generar', async () => {
    qrFixtures = [];
    render(<MiComercioPage />);
    await waitForLoaded();
    const section = within(qrSection());
    await waitFor(() => expect(section.getByText('No hay un QR activo configurado para este comercio.')).toBeInTheDocument());
    expect(section.queryByRole('button', { name: /Generar QR comercio/ })).toBeNull();
    expect(section.queryByText(/QR-DEMO-XPAY-QA-001/)).toBeNull();
  });

  // F6 — múltiples QR: selector visible.
  it('F6: con más de un QR activo, muestra un selector explícito', async () => {
    qrFixtures = [
      qr({ idQr: 10, codigoQr: 'QR-NORTE-001', nombreTienda: 'Tienda Norte' }),
      qr({ idQr: 11, codigoQr: 'QR-SUR-001', nombreTienda: 'Tienda Sur' }),
    ];
    render(<MiComercioPage />);
    await waitForLoaded();
    const section = within(qrSection());
    await waitFor(() => expect(section.getByLabelText('Selecciona una tienda / QR')).toBeInTheDocument());
    expect(section.getByText('Tienda Norte — QR-NORTE-001')).toBeInTheDocument();
    expect(section.getByText('Tienda Sur — QR-SUR-001')).toBeInTheDocument();
  });

  // F7 — múltiples QR: no genera hasta seleccionar uno.
  it('F7: con múltiples QR, el botón de generar permanece deshabilitado sin selección', async () => {
    qrFixtures = [
      qr({ idQr: 10, codigoQr: 'QR-NORTE-001', nombreTienda: 'Tienda Norte' }),
      qr({ idQr: 11, codigoQr: 'QR-SUR-001', nombreTienda: 'Tienda Sur' }),
    ];
    render(<MiComercioPage />);
    await waitForLoaded();
    const section = within(qrSection());
    await waitFor(() => expect(section.getByLabelText('Selecciona una tienda / QR')).toBeInTheDocument());
    expect(section.getByRole('button', { name: /Generar QR comercio/ })).toBeDisabled();
  });

  // F8 — después de seleccionar, genera el QR seleccionado, no otro.
  it('F8: tras seleccionar una tienda/QR, genera exactamente ese QR', async () => {
    qrFixtures = [
      qr({ idQr: 10, codigoQr: 'QR-NORTE-001', nombreTienda: 'Tienda Norte' }),
      qr({ idQr: 11, codigoQr: 'QR-SUR-001', nombreTienda: 'Tienda Sur' }),
    ];
    render(<MiComercioPage />);
    await waitForLoaded();
    const section = within(qrSection());
    await waitFor(() => expect(section.getByLabelText('Selecciona una tienda / QR')).toBeInTheDocument());

    await userEvent.selectOptions(section.getByLabelText('Selecciona una tienda / QR'), '11');
    await waitFor(() => expect(section.getByRole('button', { name: /Generar QR comercio/ })).not.toBeDisabled());
    await userEvent.click(section.getByRole('button', { name: /Generar QR comercio/ }));

    await waitFor(() => expect(section.getByAltText('QR del comercio')).toBeInTheDocument());
    expect(section.getByText(/qrCode=QR-SUR-001/)).toBeInTheDocument();
    expect(section.queryByText(/qrCode=QR-NORTE-001/)).toBeNull();
  });

  // F9 — cambio de comercio (nueva instancia/remount) no conserva el QR del comercio anterior.
  it('F9: un comercio distinto (nuevo montaje) nunca conserva el QR del comercio previo', async () => {
    qrFixtures = [qr({ idQr: 3, codigoQr: 'QR-UAT-XPAY-001', nombreTienda: 'QA UAT Tienda 1' })];
    const first = render(<MiComercioPage />);
    await waitForLoaded();
    await waitFor(() => expect(within(qrSection()).getByText('QR-UAT-XPAY-001')).toBeInTheDocument());
    first.unmount();

    idComercioActual = 2;
    qrFixtures = [qr({ idQr: 2, codigoQr: 'QR-DEMO-XPAY-QA-001', nombreTienda: 'Comercio Demo XPAY QA' })];
    render(<MiComercioPage />);
    await waitForLoaded();
    const section = within(qrSection());
    await waitFor(() => expect(section.getByText('QR-DEMO-XPAY-QA-001')).toBeInTheDocument());
    expect(section.queryByText('QR-UAT-XPAY-001')).toBeNull();
  });

  // F10 — error del endpoint: no hace fallback al QR demo.
  it('F10: si GET /api/comercio/mi-qr falla, no hace fallback al QR demo hardcodeado', async () => {
    qrResponseMode = 'error';
    render(<MiComercioPage />);
    await waitForLoaded();
    const section = within(qrSection());
    await waitFor(() => expect(section.getByText(/No fue posible cargar el QR/)).toBeInTheDocument());
    expect(section.queryByText(/QR-DEMO-XPAY-QA-001/)).toBeNull();
    expect(section.queryByRole('button', { name: /Generar QR comercio/ })).toBeNull();
  });
});
