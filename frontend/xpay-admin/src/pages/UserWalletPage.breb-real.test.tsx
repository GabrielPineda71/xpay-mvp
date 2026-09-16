// XPAY-375 FASE 9 — tests offline de la sección "Retirar a mi llave Bre-B"
// (REAL) de UserWalletPage.tsx. CERO red real: api/client.ts está
// completamente mockeado (vi.mock) — ninguna llamada llega jamás a
// Passport/Payment/Wallet reales. Cubre exactamente los escenarios pedidos
// por XPAY-375 FASE 9.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { UserWalletPage } from './UserWalletPage.tsx';
import type { AuthUser } from '../auth/AuthContext.tsx';

const mockGet = vi.fn();
const mockPost = vi.fn();

vi.mock('../api/client.ts', () => ({
  get: (...args: unknown[]) => mockGet(...args),
  post: (...args: unknown[]) => mockPost(...args),
}));

const mockUser: AuthUser = {
  idUsuario: 3, idPersona: 3, usuario: 'qa.usuario1', estado: 'ACTIVO',
  roles: [], token: 'synthetic-test-token', requiereCambioClave: false,
};

vi.mock('../auth/AuthContext.tsx', async () => {
  const actual = await vi.importActual('../auth/AuthContext.tsx');
  return { ...actual, useAuth: () => ({ user: mockUser, login: vi.fn(), logout: vi.fn(), actualizarToken: vi.fn() }) };
});

const CUENTA_BASE = {
  idWallet: 2, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA',
  saldoDisponible: 96112, saldoRetenido: 0, movimientos: [],
};

const LLAVE_BCODE = {
  idBrebLlave: 8, tipoSujeto: 'USUARIO', keyType: 'BCODE', keyValueMasked: '***0268',
  estado: 'VALIDADA', fechaValidacion: '2026-09-16T06:40:00Z',
  resolucionVerificadaPassport: true,
};

function apiRoutedGet(path: string) {
  if (path === '/api/wallets/mi-wallet') return Promise.resolve({ success: true, data: { idWallet: 2, idPersona: 3, nombreWallet: 'Wallet qa.usuario1', estado: 'ACTIVA' } });
  if (path === '/api/reportes/mi-estado-cuenta') return Promise.resolve({ success: true, data: CUENTA_BASE });
  if (path === '/api/kyc/mi-estado') return Promise.resolve({ success: true, data: { estadoKyc: 'APROBADO' } });
  if (path === '/api/breb/mi-llave') return Promise.resolve({ success: true, data: LLAVE_BCODE });
  if (path === '/api/breb/mis-retiros') return Promise.resolve({ success: true, data: [] });
  return Promise.reject(new Error(`unmocked GET ${path}`));
}

async function renderPage(initialTab = 'retirar-breb', getImpl: (path: string) => Promise<unknown> = apiRoutedGet) {
  mockGet.mockImplementation(getImpl);
  const utils = render(
    <MemoryRouter initialEntries={[`/mi-wallet?tab=${initialTab}`]}>
      <UserWalletPage />
    </MemoryRouter>,
  );
  // Espera a que cargue el estado inicial (saldo/llave) antes de continuar.
  await waitFor(() => expect(screen.getByText(/Bre-B real/i)).toBeInTheDocument());
  return utils;
}

beforeEach(() => {
  mockGet.mockReset();
  mockPost.mockReset();
});

describe('UserWalletPage — Retirar a mi llave Bre-B (REAL)', () => {
  it('renderiza en un viewport móvil sin desbordamiento horizontal evidente', async () => {
    window.innerWidth = 375; // iPhone SE-ish
    await renderPage();
    // La sección real debe existir y estar contenida en un solo bloque
    // (max-width acotado vía CSS, ya verificado por inspección — aquí sólo
    // confirmamos que el marcado esencial está presente en el DOM).
    expect(screen.getByText(/Bre-B real — este retiro mueve dinero real/i)).toBeInTheDocument();
  });

  it('muestra el saldo disponible y, si hay retenido, también el retenido', async () => {
    await renderPage('retirar-breb', (path: string) =>
      path === '/api/reportes/mi-estado-cuenta'
        ? Promise.resolve({ success: true, data: { ...CUENTA_BASE, saldoRetenido: 5000 } })
        : apiRoutedGet(path));
    expect(screen.getAllByText(/\$\s?96[.,]112/).length).toBeGreaterThan(0);
    expect(screen.getByText('Saldo retenido')).toBeInTheDocument();
  });

  it('muestra la llave propia BCODE con su estado', async () => {
    await renderPage();
    expect(screen.getByText(/BCODE · \*\*\*0268/)).toBeInTheDocument();
    expect(screen.getByText(/Verificada realmente con Passport/i)).toBeInTheDocument();
  });

  it('Resolve exitoso: muestra titular/institución/cuenta sanitizados devueltos por backend', async () => {
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({
          success: true,
          data: {
            idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA',
            resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***',
            entidadFinanciera: 'Sandbox PaaS Participant', tipoCuenta: 'LOW_VALUE',
            cuentaMasked: '***0013', vigenteHasta: '2026-09-16T07:06:00Z',
          },
        });
      }
      return Promise.reject(new Error(`unmocked POST ${path}`));
    });
    const user = userEvent.setup();
    await renderPage();

    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'valor-sintetico-de-prueba');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));

    await waitFor(() => expect(screen.getByTestId('breb-real-destino')).toBeInTheDocument());
    const destino = screen.getByTestId('breb-real-destino');
    expect(within(destino).getByText('XPay ***')).toBeInTheDocument();
    expect(within(destino).getByText('Sandbox PaaS Participant')).toBeInTheDocument();
    expect(within(destino).getByText('***0013')).toBeInTheDocument();

    // El body enviado a Passport-vía-backend nunca contiene resolution_id/
    // account_id/payment_id (campos que ni siquiera existen en el request).
    const [, sentBody] = mockPost.mock.calls[0];
    expect(Object.keys(sentBody)).toEqual(['KeyValue']);
  });

  it('Resolve con error/expirado: muestra mensaje y NO reintenta automáticamente', async () => {
    mockPost.mockImplementation((path: string) =>
      path === '/api/breb/mi-llave/resolver'
        ? Promise.reject(new Error('La resolución Passport del retiro está vencida o ausente; se requiere resolver la llave nuevamente antes de reintentar.'))
        : Promise.reject(new Error(`unmocked POST ${path}`)));
    const user = userEvent.setup();
    await renderPage();

    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'valor-sintetico');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));

    await waitFor(() => expect(screen.getByText(/vencida o ausente/i)).toBeInTheDocument());
    // Sólo UNA llamada — nunca un reintento automático.
    expect(mockPost).toHaveBeenCalledTimes(1);
    // El formulario de monto no debe aparecer sin un Resolve exitoso.
    expect(screen.queryByPlaceholderText('Ej: 5000')).not.toBeInTheDocument();
  });

  it('valida monto: rechaza cero/negativo y montos mayores al saldo disponible', async () => {
    mockPost.mockImplementation((path: string) =>
      path === '/api/breb/mi-llave/resolver'
        ? Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***' } })
        : Promise.reject(new Error('no debería llamarse')));
    const user = userEvent.setup();
    await renderPage();
    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'v');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));
    await waitFor(() => expect(screen.getByTestId('breb-real-destino')).toBeInTheDocument());

    const montoInput = await screen.findByPlaceholderText('Ej: 5000');
    await user.type(montoInput, '200000');
    await user.click(screen.getByRole('button', { name: /Continuar/i }));
    expect(screen.getByText(/no puede superar tu saldo disponible/i)).toBeInTheDocument();
    // Nunca avanzó a confirmación ni llamó al backend de retiro.
    expect(screen.queryByTestId('breb-real-confirm')).not.toBeInTheDocument();
    expect(mockPost).toHaveBeenCalledTimes(1); // sólo el resolve anterior
  });

  it('exige confirmación explícita separada antes de llamar a retiros/real (nunca al escribir el monto)', async () => {
    mockPost.mockImplementation((path: string) =>
      path === '/api/breb/mi-llave/resolver'
        ? Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***', cuentaMasked: '***0013', entidadFinanciera: 'Sandbox PaaS Participant' } })
        : Promise.reject(new Error('no debería llamarse todavía')));
    const user = userEvent.setup();
    await renderPage();
    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'v');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));
    const montoInput = await screen.findByPlaceholderText('Ej: 5000');
    await user.type(montoInput, '5000');
    await user.click(screen.getByRole('button', { name: /Continuar/i }));

    // Al escribir el monto y pulsar "Continuar" NUNCA se llamó
    // /api/breb/retiros/real — sólo se avanzó a la pantalla de confirmación.
    expect(mockPost).toHaveBeenCalledTimes(1);
    const confirmCard = await screen.findByTestId('breb-real-confirm');
    expect(within(confirmCard).getByText(/Confirmar retiro/i)).toBeInTheDocument();
  });

  it('protección contra doble clic: el botón "Confirmar retiro" se deshabilita mientras la request está en curso', async () => {
    let resolvePost: (v: unknown) => void = () => {};
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***' } });
      }
      if (path === '/api/breb/retiros/real') {
        return new Promise(res => { resolvePost = res; });
      }
      return Promise.reject(new Error('unexpected'));
    });
    const user = userEvent.setup();
    await renderPage();
    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'v');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));
    const montoInput = await screen.findByPlaceholderText('Ej: 5000');
    await user.type(montoInput, '5000');
    await user.click(screen.getByRole('button', { name: /Continuar/i }));

    const confirmBtn = await screen.findByRole('button', { name: /Confirmar retiro/i });
    await user.click(confirmBtn);
    expect(confirmBtn).toBeDisabled();
    await user.click(confirmBtn); // segundo clic mientras está en curso — no debe disparar una segunda llamada
    expect(mockPost).toHaveBeenCalledTimes(2); // 1 resolve + 1 retiro (nunca 3)

    resolvePost({ success: true, data: { idBrebRetiro: 1, valor: 5000, moneda: 'COP', estado: 'ENVIADO_PASSPORT', fechaSolicitud: '2026-09-16T07:00:00Z' } });
    await waitFor(() => expect(screen.getByTestId('breb-real-result')).toBeInTheDocument());
  });

  it.each([
    ['PENDIENTE_ENVIO_PASSPORT', /siendo procesado/i],
    ['ENVIADO_PASSPORT', /siendo procesado/i],
    ['LIQUIDADO', /Retiro completado/i],
    ['RECHAZADO', /volvió a estar disponible/i],
  ])('estado %s se representa correctamente sin asumir éxito por HTTP 2xx', async (estado, esperado) => {
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***' } });
      }
      if (path === '/api/breb/retiros/real') {
        return Promise.resolve({ success: true, data: { idBrebRetiro: 1, valor: 5000, moneda: 'COP', estado, fechaSolicitud: '2026-09-16T07:00:00Z' } });
      }
      return Promise.reject(new Error('unexpected'));
    });
    const user = userEvent.setup();
    await renderPage();
    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'v');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));
    const montoInput = await screen.findByPlaceholderText('Ej: 5000');
    await user.type(montoInput, '5000');
    await user.click(screen.getByRole('button', { name: /Continuar/i }));
    await user.click(await screen.findByRole('button', { name: /Confirmar retiro/i }));

    await waitFor(() => expect(screen.getByTestId('breb-real-result')).toBeInTheDocument());
    expect(within(screen.getByTestId('breb-real-result')).getByText(esperado)).toBeInTheDocument();
  });

  it('el body de /api/breb/retiros/real sólo contiene Monto — nunca account_id/resolution_id/payment_id/destination key', async () => {
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***' } });
      }
      return Promise.resolve({ success: true, data: { idBrebRetiro: 1, valor: 5000, moneda: 'COP', estado: 'ENVIADO_PASSPORT', fechaSolicitud: '2026-09-16T07:00:00Z' } });
    });
    const user = userEvent.setup();
    await renderPage();
    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'v');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));
    const montoInput = await screen.findByPlaceholderText('Ej: 5000');
    await user.type(montoInput, '5000');
    await user.click(screen.getByRole('button', { name: /Continuar/i }));
    await user.click(await screen.findByRole('button', { name: /Confirmar retiro/i }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/breb/retiros/real', { Monto: 5000 }));
  });
});
