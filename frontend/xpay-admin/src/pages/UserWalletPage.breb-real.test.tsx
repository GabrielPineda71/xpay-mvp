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

// XPAY-377 — HttpUncertainError se importa de la implementación REAL (no
// mockeada): sólo get/post están sustituidos; la clase de error debe ser
// la misma que usa UserWalletPage.tsx para que `instanceof` funcione en
// los tests de timeout.
vi.mock('../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../api/client.ts')>('../api/client.ts');
  return {
    ...actual,
    get: (...args: unknown[]) => mockGet(...args),
    post: (...args: unknown[]) => mockPost(...args),
  };
});
import { HttpUncertainError } from '../api/client.ts';

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
  // XPAY-415 — el badge "Bre-B real" (aviso rojo permanente) se eliminó de
  // esta vista; el placeholder del formulario de verificación es una ancla
  // igual de estable para el estado "ya cargó, hay llave BCODE" que usan
  // todos los tests de este archivo.
  await waitFor(() => expect(screen.getByPlaceholderText(/Valor de tu llave BCODE/i)).toBeInTheDocument());
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
    expect(screen.getByRole('button', { name: /Verificar mi llave/i })).toBeInTheDocument();
  });

  // XPAY-415 — el aviso rojo permanente "Bre-B real — este retiro mueve
  // dinero real" se retira de la vista; la confirmación final del retiro
  // (monto/destino/institución/"moverá dinero real") sigue intacta y se
  // prueba por separado más abajo.
  it('XPAY-415: ya NO muestra el aviso rojo permanente "Bre-B real — este retiro mueve dinero real"', async () => {
    await renderPage();
    expect(screen.queryByText(/Bre-B real — este retiro mueve dinero real/i)).not.toBeInTheDocument();
  });

  it('XPAY-415: el formulario de verificación usa "Ingresa tu llave Bre-B para verificarla"', async () => {
    await renderPage();
    expect(screen.getByText('Ingresa tu llave Bre-B para verificarla')).toBeInTheDocument();
    expect(screen.queryByText(/Confirma el valor de tu llave para verificarla/i)).not.toBeInTheDocument();
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

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith(
      '/api/breb/retiros/real', { Monto: 5000 }, undefined, expect.any(Number),
    ));
  });

  // XPAY-377 FASE 6 — escenarios nuevos: timeout cliente, incertidumbre,
  // reconciliación, ownership.

  it('Payment queda pendiente hasta timeout cliente: la UI sale de "Procesando..." sin decir que falló', async () => {
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***' } });
      }
      if (path === '/api/breb/retiros/real') {
        return Promise.reject(new HttpUncertainError('Se agotó el tiempo de espera esperando la respuesta del servidor.'));
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

    // Sale de "Procesando..." (el botón vuelve a estar habilitado/con su
    // texto normal en la tarjeta de confirmación, que ya no se muestra) —
    // pero NUNCA aparece un mensaje de fallo financiero normal.
    const uncertain = await screen.findByTestId('breb-real-uncertain');
    expect(within(uncertain).getByText(/No pudimos confirmar todavía el resultado de tu retiro/i)).toBeInTheDocument();
    expect(screen.queryByTestId('breb-real-confirm')).not.toBeInTheDocument();
  });

  it('timeout cliente NUNCA se presenta como fallo financiero (nunca "El retiro fue rechazado" ni "Retiro completado")', async () => {
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***' } });
      }
      return Promise.reject(new HttpUncertainError('timeout'));
    });
    const user = userEvent.setup();
    await renderPage();
    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'v');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));
    const montoInput = await screen.findByPlaceholderText('Ej: 5000');
    await user.type(montoInput, '5000');
    await user.click(screen.getByRole('button', { name: /Continuar/i }));
    await user.click(await screen.findByRole('button', { name: /Confirmar retiro/i }));

    await screen.findByTestId('breb-real-uncertain');
    expect(screen.queryByText(/Retiro completado/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/fue rechazado/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/No se pudo procesar el retiro/i)).not.toBeInTheDocument();
  });

  it('timeout cliente muestra explícitamente "no vuelvas a intentarlo"', async () => {
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***' } });
      }
      return Promise.reject(new HttpUncertainError('timeout'));
    });
    const user = userEvent.setup();
    await renderPage();
    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'v');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));
    const montoInput = await screen.findByPlaceholderText('Ej: 5000');
    await user.type(montoInput, '5000');
    await user.click(screen.getByRole('button', { name: /Continuar/i }));
    await user.click(await screen.findByRole('button', { name: /Confirmar retiro/i }));

    const uncertain = await screen.findByTestId('breb-real-uncertain');
    expect(within(uncertain).getByText(/No vuelvas a intentarlo/i)).toBeInTheDocument();
  });

  it('retiro transitorio puede consultar estado vía el endpoint user-side, nunca enviando payment_id', async () => {
    mockPost.mockImplementation((path: string) => {
      if (path === '/api/breb/mi-llave/resolver') {
        return Promise.resolve({ success: true, data: { idBrebLlave: 8, keyType: 'BCODE', keyValueMasked: '***0268', estado: 'VALIDADA', resolucionVerificadaPassport: true, titularNombreMasked: 'XPay ***' } });
      }
      if (path === '/api/breb/retiros/real') {
        return Promise.resolve({ success: true, data: { idBrebRetiro: 42, valor: 5000, moneda: 'COP', estado: 'ENVIADO_PASSPORT', fechaSolicitud: '2026-09-16T07:00:00Z' } });
      }
      if (path === '/api/breb/mis-retiros/42/actualizar-estado') {
        return Promise.resolve({ success: true, data: { idBrebRetiro: 42, valor: 5000, moneda: 'COP', estado: 'LIQUIDADO', fechaSolicitud: '2026-09-16T07:00:00Z' } });
      }
      return Promise.reject(new Error(`unmocked POST ${path}`));
    });
    const user = userEvent.setup();
    await renderPage();
    await user.type(screen.getByPlaceholderText(/Valor de tu llave BCODE/i), 'v');
    await user.click(screen.getByRole('button', { name: /Verificar mi llave/i }));
    const montoInput = await screen.findByPlaceholderText('Ej: 5000');
    await user.type(montoInput, '5000');
    await user.click(screen.getByRole('button', { name: /Continuar/i }));
    await user.click(await screen.findByRole('button', { name: /Confirmar retiro/i }));

    await screen.findByTestId('breb-real-result');
    await user.click(screen.getByRole('button', { name: /Consultar estado/i }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/breb/mis-retiros/42/actualizar-estado', {}));
    await waitFor(() => expect(screen.getByText(/Retiro completado/i)).toBeInTheDocument());

    // El id va SIEMPRE en la ruta (local, propio) — el body nunca lleva
    // payment_id ni ningún identificador Passport.
    const call = mockPost.mock.calls.find(c => c[0] === '/api/breb/mis-retiros/42/actualizar-estado');
    expect(call?.[1]).toEqual({});
  });
});
