// XPAY-438/438A — notificación operacional de venta QR, vista COMERCIO.
// Cubre F1-F18 (XPAY-438) + las 4 correcciones de XPAY-438A (baseline sin
// drenaje, fail-safe >2000 en polling normal, guard in-flight, guard de
// generación). CERO red real: api/caja.ts está completamente mockeado
// (listarVentasQrDesde + obtenerUltimoIdVentaQr — las únicas dos funciones
// que este árbol de componentes usa) y api/client.ts (get/post) también,
// para poder probar explícitamente F18 (cero POST financiero) sin
// ambigüedad.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CommerceNotificationsProvider } from './CommerceNotificationsContext.tsx';
import { CommerceNotificationBell } from './CommerceNotificationBell.tsx';
import type { VentaQrNotificacion } from '../../api/caja.ts';

const mockListarVentasQrDesde     = vi.fn();
const mockObtenerUltimoIdVentaQr  = vi.fn();
vi.mock('../../api/caja.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/caja.ts')>('../../api/caja.ts');
  return {
    ...actual,
    listarVentasQrDesde:    (...args: unknown[]) => mockListarVentasQrDesde(...args),
    obtenerUltimoIdVentaQr: (...args: unknown[]) => mockObtenerUltimoIdVentaQr(...args),
  };
});

const mockGet  = vi.fn();
const mockPost = vi.fn();
vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return { ...actual, get: (...a: unknown[]) => mockGet(...a), post: (...a: unknown[]) => mockPost(...a) };
});

const ID_COMERCIO = 7;
const ID_COMERCIO_B = 8;

function venta(overrides: Partial<VentaQrNotificacion> & { idVentaQr: number }): VentaQrNotificacion {
  return {
    valorBruto:   120_000,
    estado:       'CONTINGENCIA',
    fechaVenta:   '2026-09-20T15:30:00Z',
    idTienda:     1,
    nombreTienda: 'Tienda Centro',
    ...overrides,
  };
}

let ventasState: VentaQrNotificacion[] = [];

function apiListar(desde: number): Promise<VentaQrNotificacion[]> {
  const pagina = ventasState
    .filter(v => v.idVentaQr > desde)
    .sort((a, b) => a.idVentaQr - b.idVentaQr)
    .slice(0, 100);
  return Promise.resolve(pagina);
}

function apiUltimoId(): Promise<number> {
  if (ventasState.length === 0) return Promise.resolve(0);
  return Promise.resolve(Math.max(...ventasState.map(v => v.idVentaQr)));
}

function lastSeenKey(idComercio: number = ID_COMERCIO): string {
  return `xpay.comercio.${idComercio}.notifications.lastSeenVentaQrId`;
}

function notifDot(): Element | null {
  return document.querySelector('.commerce-notification-dot');
}

function renderHarness(idComercioExistente: number | null = ID_COMERCIO) {
  return render(
    <CommerceNotificationsProvider idComercioExistente={idComercioExistente}>
      <CommerceNotificationBell />
    </CommerceNotificationsProvider>,
  );
}

async function abrirCampana() {
  await userEvent.click(screen.getByRole('button', { name: 'Notificaciones de ventas' }));
}

function getPanel() {
  return screen.getByRole('dialog', { name: 'Notificaciones' });
}

// Primer uso (sin cursor persistido): el baseline llama a
// obtenerUltimoIdVentaQr() — NUNCA a listarVentasQrDesde (XPAY-438A §2).
async function esperarBaseline() {
  await waitFor(() => expect(mockObtenerUltimoIdVentaQr).toHaveBeenCalled());
}

// Reanudación (SÍ existe cursor persistido, p.ej. tras un remount): retoma
// vía listarVentasQrDesde(cursorPersistido).
async function esperarReanudacion(desde: number) {
  await waitFor(() => expect(mockListarVentasQrDesde).toHaveBeenCalledWith(desde));
}

beforeEach(() => {
  ventasState = [];
  window.localStorage.clear();
  mockListarVentasQrDesde.mockReset();
  mockListarVentasQrDesde.mockImplementation(apiListar);
  mockObtenerUltimoIdVentaQr.mockReset();
  mockObtenerUltimoIdVentaQr.mockImplementation(apiUltimoId);
  mockGet.mockReset();
  mockPost.mockReset();
  vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'setInterval', 'clearTimeout', 'clearInterval'] });
});
afterEach(() => { vi.useRealTimers(); });

describe('XPAY-438/438A — CommerceNotifications', () => {
  // F1 — primer uso → baseline, 0 históricos unread.
  it('F1: primer uso establece baseline (via ultimo-id) sin marcar historial como no-leído', async () => {
    ventasState = [venta({ idVentaQr: 1 }), venta({ idVentaQr: 2 }), venta({ idVentaQr: 3 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('3'));
    expect(notifDot()).toBeNull();
    // XPAY-438A §2 — el baseline de primer uso NUNCA drena por páginas.
    expect(mockListarVentasQrDesde).not.toHaveBeenCalled();
  });

  // XPAY-438A §2 — 0 ventas: baseline = 0, sin crash, sin unread.
  it('XPAY-438A §2: comercio con 0 VentaQr obtiene baseline=0 sin errores', async () => {
    ventasState = [];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('0'));
    expect(notifDot()).toBeNull();
  });

  // XPAY-438A §2 — hallazgo central del ticket: >2.000 VentaQr históricas ya
  // NO dejan el baseline congelado en una venta antigua (antes: drenaje por
  // páginas con tope 2.000; ahora: una sola consulta MAX(id)).
  it('XPAY-438A §2: comercio con 2.500 VentaQr históricas obtiene baseline = la más reciente, no la #2.000', async () => {
    for (let i = 1; i <= 2500; i++) ventasState.push(venta({ idVentaQr: i }));
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('2500'));
    expect(notifDot()).toBeNull();
    expect(mockListarVentasQrDesde).not.toHaveBeenCalled();

    // Una venta genuinamente nueva SÍ debe notificarse normalmente después.
    ventasState.push(venta({ idVentaQr: 2501 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());
    await abrirCampana();
    expect(within(getPanel()).getByText('Venta #2501')).toBeInTheDocument();
    expect(within(getPanel()).queryByText('Venta #2000')).toBeNull(); // NUNCA se notifica historial.
  });

  // F2 — nueva VentaQr → badge.
  it('F2: una venta nueva tras el baseline enciende el badge', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2, valorBruto: 50_000 }));
    await vi.advanceTimersByTimeAsync(7000);

    await waitFor(() => expect(notifDot()).not.toBeNull());
  });

  // F3/F4/F5/F6 — contenido correcto del ítem (monto, Venta #N, fecha, tienda).
  it('F3-F6: el ítem muestra monto, número de venta, tienda y fecha correctos', async () => {
    ventasState = [venta({ idVentaQr: 10 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('10'));

    ventasState.push(venta({
      idVentaQr: 11, valorBruto: 75_000, nombreTienda: 'Sucursal Norte', fechaVenta: '2026-09-20T09:00:00Z',
    }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await abrirCampana();
    const panel = within(getPanel());
    expect(panel.getByText('Venta #11')).toBeInTheDocument();
    expect(panel.getByText('Tienda: Sucursal Norte')).toBeInTheDocument();
    expect(panel.getByText(/75[.,]000/)).toBeInTheDocument();
  });

  // F7 — NombreTienda null → notificación válida sin línea "Tienda:".
  it('F7: NombreTienda null no bloquea la notificación ni renderiza la línea de tienda', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2, nombreTienda: null }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await abrirCampana();
    const panel = within(getPanel());
    expect(panel.getByText('Venta #2')).toBeInTheDocument();
    expect(panel.queryByText(/^Tienda:/)).toBeNull();
  });

  // F8 — múltiples ventas entre polls → todas aparecen.
  it('F8: varias ventas detectadas en un mismo poll aparecen todas', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }), venta({ idVentaQr: 3 }), venta({ idVentaQr: 4 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await abrirCampana();
    const panel = within(getPanel());
    expect(panel.getByText('Venta #2')).toBeInTheDocument();
    expect(panel.getByText('Venta #3')).toBeInTheDocument();
    expect(panel.getByText('Venta #4')).toBeInTheDocument();
  });

  // F9 — polls repetidos sin datos nuevos no duplican.
  it('F9: polls repetidos sin ventas nuevas no duplican notificaciones', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    // 3 ticks más, sin datos nuevos.
    await vi.advanceTimersByTimeAsync(7000);
    await vi.advanceTimersByTimeAsync(7000);
    await vi.advanceTimersByTimeAsync(7000);

    await abrirCampana();
    const panel = within(getPanel());
    expect(panel.getAllByText('Venta #2')).toHaveLength(1);
  });

  // F10 — refresh/remount conserva unread (no marcado como visto). Tras un
  // remount, YA existe cursor persistido → retoma vía listarVentasQrDesde,
  // no via el baseline de primer uso.
  it('F10: unread sobrevive a un remount porque el seen-cursor persistido no avanzó', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    const first = renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    first.unmount();
    mockListarVentasQrDesde.mockClear();
    mockObtenerUltimoIdVentaQr.mockClear();

    renderHarness();
    await esperarReanudacion(1); // retoma desde el cursor persistido (1), no desde el baseline.
    expect(mockObtenerUltimoIdVentaQr).not.toHaveBeenCalled(); // NO es primer uso.
    await waitFor(() => expect(notifDot()).not.toBeNull());
  });

  // F11 — abrir el panel NO marca como leído.
  it('F11: abrir el panel no marca nada como visto', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await abrirCampana();
    expect(window.localStorage.getItem(lastSeenKey())).toBe('1');
    expect(notifDot()).not.toBeNull();
  });

  // F12 — cerrar el panel NO marca como leído.
  it('F12: cerrar el panel no marca nada como visto', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await abrirCampana();
    await userEvent.click(screen.getByRole('button', { name: 'Cerrar notificaciones' }));

    expect(window.localStorage.getItem(lastSeenKey())).toBe('1');
    expect(notifDot()).not.toBeNull();
  });

  // F13 — "Marcar como vistas" limpia badge/unread y avanza el cursor.
  it('F13: marcar como vistas limpia el badge y avanza el cursor persistido', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }), venta({ idVentaQr: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await abrirCampana();
    await userEvent.click(screen.getByRole('button', { name: 'Marcar como vistas' }));

    expect(notifDot()).toBeNull();
    expect(within(getPanel()).getByText('No tienes notificaciones nuevas.')).toBeInTheDocument();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('3'));
  });

  // F14 — remount después de marcar como vistas no reaparece.
  it('F14: tras marcar como vistas, un remount no vuelve a mostrar las mismas ventas', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    const first = renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await abrirCampana();
    await userEvent.click(screen.getByRole('button', { name: 'Marcar como vistas' }));
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('2'));

    first.unmount();
    mockListarVentasQrDesde.mockClear();
    mockObtenerUltimoIdVentaQr.mockClear();

    renderHarness();
    await esperarReanudacion(2);
    expect(notifDot()).toBeNull();
  });

  // F15 — cambio de comercio aísla el cursor (no mezcla notificaciones).
  it('F15: cambiar idComercioExistente aísla completamente el estado de notificaciones', async () => {
    // Nota: el mock apiListar/apiUltimoId no distingue por comercio (esa
    // asignación real es server-side, ya cubierta en los tests B2/B6 de
    // backend) — este test prueba la mitad que sí vive en el cliente: el
    // namespacing por localStorage y el reseteo de estado en memoria al
    // cambiar de comercio.
    ventasState = [venta({ idVentaQr: 1 })];
    const { rerender } = renderHarness(ID_COMERCIO);
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey(ID_COMERCIO))).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    mockListarVentasQrDesde.mockClear();
    mockObtenerUltimoIdVentaQr.mockClear();
    rerender(
      <CommerceNotificationsProvider idComercioExistente={ID_COMERCIO_B}>
        <CommerceNotificationBell />
      </CommerceNotificationsProvider>,
    );

    // El comercio B nunca tuvo cursor persistido → corre su propio baseline
    // (ultimo-id), ignorando por completo lo detectado para A.
    await esperarBaseline();
    expect(notifDot()).toBeNull();
    // A nunca marcó como vistas su venta detectada — su cursor persistido
    // sigue en el valor del baseline original (1), sin que la activación de
    // B lo haya tocado en absoluto.
    expect(window.localStorage.getItem(lastSeenKey(ID_COMERCIO))).toBe('1');
  });

  // XPAY-438A §5 — guard de generación: una respuesta TARDÍA del comercio A
  // (todavía en vuelo cuando el usuario cambia a B) no debe insertar
  // notificaciones ni mover el cursor de detección de B. mountedRef NO
  // cubre este caso (el componente sigue montado, solo cambia la prop).
  it('XPAY-438A §5: una respuesta tardía de un comercio ya abandonado no contamina el comercio activo', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    // Cursor YA persistido para A → el efecto de activación entra por la
    // rama de reanudación (listarVentasQrDesde), la rama con la petición
    // que dejaremos "colgada" a propósito.
    window.localStorage.setItem(lastSeenKey(ID_COMERCIO), '1');

    let resolverA: ((v: VentaQrNotificacion[]) => void) | null = null;
    mockListarVentasQrDesde.mockImplementationOnce(
      () => new Promise<VentaQrNotificacion[]>(resolve => { resolverA = resolve; }),
    );

    const { rerender } = renderHarness(ID_COMERCIO);
    await waitFor(() => expect(mockListarVentasQrDesde).toHaveBeenCalledWith(1)); // A: petición en vuelo, sin resolver aún.

    // Cambia a B ANTES de que la petición de A responda.
    window.localStorage.setItem(lastSeenKey(ID_COMERCIO_B), '0'); // B también reanuda, no hace baseline.
    mockListarVentasQrDesde.mockImplementation(() => Promise.resolve([])); // respuestas de B: nada nuevo.
    rerender(
      <CommerceNotificationsProvider idComercioExistente={ID_COMERCIO_B}>
        <CommerceNotificationBell />
      </CommerceNotificationsProvider>,
    );
    await esperarReanudacion(0);

    // AHORA resuelve la petición tardía de A, con una venta "nueva" para A.
    resolverA!([venta({ idVentaQr: 99, valorBruto: 999_000 })]);
    await vi.advanceTimersByTimeAsync(0); // deja correr los microtasks pendientes tras el resolve.

    // La respuesta tardía de A NUNCA debe aparecer en el comercio B activo.
    expect(notifDot()).toBeNull();
    await abrirCampana();
    expect(within(getPanel()).queryByText('Venta #99')).toBeNull();
    // Tampoco debe haber contaminado el cursor persistido de B.
    expect(window.localStorage.getItem(lastSeenKey(ID_COMERCIO_B))).toBe('0');
  });

  // F16 — un error de poll conserva el unread existente (no falsos positivos, no crash).
  it('F16: un error de polling conserva el unread existente sin romper la UI', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    mockListarVentasQrDesde.mockImplementationOnce(() => Promise.reject(new Error('network down')));
    await vi.advanceTimersByTimeAsync(7000);

    // El badge/unread previamente detectado sigue intacto; el cursor de
    // visto tampoco avanzó (nunca se marcó como vista).
    expect(notifDot()).not.toBeNull();
    expect(window.localStorage.getItem(lastSeenKey())).toBe('1');
  });

  // F17 — >100 ventas entre polls se drenan por completo (paginación), ninguna se pierde.
  it('F17: más de 100 ventas nuevas se drenan completas en un solo ciclo de poll', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    for (let i = 2; i <= 151; i++) ventasState.push(venta({ idVentaQr: i, valorBruto: 1000 }));
    await vi.advanceTimersByTimeAsync(7000);

    // Página 1: listarVentasQrDesde(1) → ids 2..101 (100, tope de página);
    // como llegó exactamente al tope, drena una 2ª página desde el máximo
    // recibido (101) → ids 102..151 (50, <100 → fin del drenaje).
    await waitFor(() => expect(mockListarVentasQrDesde).toHaveBeenCalledWith(101));

    await abrirCampana();
    const panel = within(getPanel());
    expect(panel.getByText('Venta #2')).toBeInTheDocument();
    expect(panel.getByText('Venta #100')).toBeInTheDocument();
    expect(panel.getByText('Venta #151')).toBeInTheDocument();
  });

  // XPAY-438A §3 — fail-safe: >2.000 ventas NUEVAS entre polls (no en el
  // baseline). El cursor debe quedar EXACTAMENTE en la última página
  // realmente recibida (2.000), nada se salta, nada se marca como visto,
  // y el poll SIGUIENTE retoma y termina de drenar el resto.
  it('XPAY-438A §3: >2.000 ventas nuevas se drenan en varios ciclos de poll sin perder ninguna', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    for (let i = 2; i <= 2201; i++) ventasState.push(venta({ idVentaQr: i, valorBruto: 1000 })); // 2.200 nuevas.
    await vi.advanceTimersByTimeAsync(7000); // ciclo 1: tope de 20 páginas × 100 = 2.000 filas → hasta id 2001.

    await abrirCampana();
    let panel = within(getPanel());
    expect(panel.getByText('Venta #2001')).toBeInTheDocument();
    expect(panel.queryByText('Venta #2002')).toBeNull(); // todavía no llegó — el tope cortó exactamente ahí.
    expect(window.localStorage.getItem(lastSeenKey())).toBe('1'); // nada se marcó como visto por el fail-safe.
    await userEvent.click(screen.getByRole('button', { name: 'Cerrar notificaciones' }));

    await vi.advanceTimersByTimeAsync(7000); // ciclo 2: continúa desde 2001, drena el resto (2002..2201).

    await abrirCampana();
    panel = within(getPanel());
    expect(panel.getByText('Venta #2002')).toBeInTheDocument();
    expect(panel.getByText('Venta #2201')).toBeInTheDocument();
  });

  // XPAY-438A §4 — guard in-flight: una consulta lenta (>7s) no debe
  // solaparse con el siguiente tick del intervalo.
  it('XPAY-438A §4: un poll lento evita que el siguiente tick dispare una consulta solapada', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    let resolverLenta: ((v: VentaQrNotificacion[]) => void) | null = null;
    mockListarVentasQrDesde.mockImplementationOnce(
      () => new Promise<VentaQrNotificacion[]>(resolve => { resolverLenta = resolve; }),
    );

    await vi.advanceTimersByTimeAsync(7000); // dispara el poll lento (queda colgado, sin resolver).
    await waitFor(() => expect(mockListarVentasQrDesde).toHaveBeenCalledTimes(1));

    mockListarVentasQrDesde.mockClear();
    await vi.advanceTimersByTimeAsync(7000); // el poll anterior SIGUE en vuelo → este tick debe omitirse.
    expect(mockListarVentasQrDesde).not.toHaveBeenCalled();

    // Ahora se resuelve la consulta lenta, con una venta nueva.
    resolverLenta!([venta({ idVentaQr: 2 })]);
    await vi.advanceTimersByTimeAsync(0);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    // Y el ciclo de polling se recupera con normalidad en el siguiente tick.
    ventasState.push(venta({ idVentaQr: 3 }));
    await vi.advanceTimersByTimeAsync(7000);
    await abrirCampana();
    expect(within(getPanel()).getByText('Venta #3')).toBeInTheDocument();
  });

  // XPAY-438A §6 — marcar como vistas durante un poll en curso: lo ya
  // detectado antes del click queda visto; lo que llega DESPUÉS (no
  // detectado todavía al momento del click) sigue apareciendo como nuevo.
  it('XPAY-438A §6: marcar como vistas durante un poll en curso no pierde ventas que llegan después', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    // Detecta la venta #2 primero (poll normal, resuelto).
    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    // XPAY-441 — aísla el conteo de llamadas del mock: el assertion de más
    // abajo (toHaveBeenCalledTimes(1)) solo quiere confirmar que EL POLL
    // LENTO de esta sección fue disparado, no cuántas llamadas acumuló el
    // mock desde el inicio del test (el poll de la venta #2, arriba, ya
    // generó una llamada propia).
    mockListarVentasQrDesde.mockClear();

    // Arranca un poll que queda en vuelo (con la venta #3, todavía no
    // detectada en el momento del click de abajo).
    ventasState.push(venta({ idVentaQr: 3 }));
    let resolverPoll: ((v: VentaQrNotificacion[]) => void) | null = null;
    mockListarVentasQrDesde.mockImplementationOnce(
      () => new Promise<VentaQrNotificacion[]>(resolve => { resolverPoll = resolve; }),
    );
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(mockListarVentasQrDesde).toHaveBeenCalledTimes(1));

    // El usuario marca como vistas MIENTRAS ese poll sigue en vuelo — solo
    // la #2 (ya detectada) debe quedar marcada.
    await abrirCampana();
    await userEvent.click(screen.getByRole('button', { name: 'Marcar como vistas' }));
    expect(notifDot()).toBeNull();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('2'));

    // Ahora el poll en vuelo resuelve, trayendo la #3.
    resolverPoll!([venta({ idVentaQr: 3 })]);
    await vi.advanceTimersByTimeAsync(0);

    // La #3 (no detectada al momento del click) debe seguir/volver a
    // aparecer como nueva — no se perdió.
    await waitFor(() => expect(notifDot()).not.toBeNull());
    await userEvent.click(screen.getByRole('button', { name: 'Notificaciones de ventas' }));
    expect(within(getPanel()).getByText('Venta #3')).toBeInTheDocument();
    expect(within(getPanel()).queryByText('Venta #2')).toBeNull(); // la #2 ya quedó vista, no reaparece.
  });

  // F18 — cero POST financiero en todo el ciclo de vida (baseline + poll + marcar vistas).
  it('F18: ningún POST financiero se dispara en ningún momento', async () => {
    ventasState = [venta({ idVentaQr: 1 })];
    renderHarness();
    await esperarBaseline();
    await waitFor(() => expect(window.localStorage.getItem(lastSeenKey())).toBe('1'));

    ventasState.push(venta({ idVentaQr: 2 }));
    await vi.advanceTimersByTimeAsync(7000);
    await waitFor(() => expect(notifDot()).not.toBeNull());

    await abrirCampana();
    await userEvent.click(screen.getByRole('button', { name: 'Marcar como vistas' }));

    expect(mockPost).not.toHaveBeenCalled();
    expect(mockGet).not.toHaveBeenCalled(); // esta suite solo usa listarVentasQrDesde/obtenerUltimoIdVentaQr, no get() genérico.
  });
});
