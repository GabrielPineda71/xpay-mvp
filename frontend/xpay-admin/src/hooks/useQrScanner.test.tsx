// XPAY-422 Parte D — tests deterministas del ciclo de vida serializado de
// useQrScanner. A diferencia del mock histórico de html5-qrcode usado en el
// resto de la suite (`start() { return new Promise(() => {}); }`, que NUNCA
// resuelve), aquí cada instancia mockeada expone una promesa DIFERIDA y
// controlable por el test (`startDeferred.resolve()/.reject()`) — es lo que
// permite reproducir exactamente la condición de carrera diagnosticada en
// XPAY-421 (stop() disparado mientras start() sigue "en vuelo") de forma
// reproducible y sin timers reales/arbitrarios.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useQrScanner } from './useQrScanner.ts';

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason?: unknown) => void;
}

const { instances, callLog, makeDeferred } = vi.hoisted(() => {
  function makeDeferred<T>(): Deferred<T> {
    let resolve!: (value: T) => void;
    let reject!: (reason?: unknown) => void;
    const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
  }
  return {
    instances: [] as Array<{
      id: number;
      elementId: string;
      startDeferred: Deferred<void>;
      startArgs: unknown[];
      stopSpy: ReturnType<typeof vi.fn>;
      clearSpy: ReturnType<typeof vi.fn>;
    }>,
    callLog: [] as string[],
    makeDeferred,
  };
});

vi.mock('html5-qrcode', () => {
  class MockHtml5Qrcode {
    id: number;
    elementId: string;
    startDeferred = makeDeferred<void>();
    startArgs: unknown[] = [];
    stopSpy = vi.fn(() => Promise.resolve());
    clearSpy = vi.fn();
    constructor(elementId: string) {
      this.id = instances.length;
      this.elementId = elementId;
      callLog.push(`construct:${this.id}`);
      instances.push(this as unknown as (typeof instances)[number]);
    }
    start(...args: unknown[]) {
      callLog.push(`start:${this.id}`);
      this.startArgs = args;
      return this.startDeferred.promise;
    }
    stop() {
      callLog.push(`stop:${this.id}`);
      return this.stopSpy();
    }
    clear() {
      callLog.push(`clear:${this.id}`);
      return this.clearSpy();
    }
  }
  return { Html5Qrcode: MockHtml5Qrcode };
});

beforeEach(() => {
  instances.length = 0;
  callLog.length = 0;
});

function baseProps(overrides: Partial<Parameters<typeof useQrScanner>[0]> = {}) {
  return {
    active: false,
    elementId: 'test-qr-reader',
    onDecode: vi.fn(),
    onError: vi.fn(),
    ...overrides,
  };
}

describe('useQrScanner — ciclo de vida serializado (XPAY-422 Parte D)', () => {
  it('SCANNER TEST 1: start() pendiente + salida de tab → al resolver, se detiene y limpia; no queda instancia activa', async () => {
    const { rerender, unmount } = renderHook((props) => useQrScanner(props), {
      initialProps: baseProps({ active: true }),
    });

    await waitFor(() => expect(instances).toHaveLength(1));
    const scanner = instances[0];
    // El usuario abandona el tab ANTES de que start() haya resuelto —
    // exactamente la condición de carrera de XPAY-421.
    rerender(baseProps({ active: false }));

    // stop() todavía no debe llamarse: la cadena está esperando a que la
    // instancia termine de resolver su propio start() primero.
    expect(scanner.stopSpy).not.toHaveBeenCalled();

    scanner.startDeferred.resolve();
    await waitFor(() => expect(scanner.stopSpy).toHaveBeenCalledTimes(1));
    expect(scanner.clearSpy).toHaveBeenCalledTimes(1);
    // El start() se resolvió DESPUÉS de solicitarse el cierre — el orden
    // real de llamadas confirma que nunca hubo stop antes de que start()
    // completara.
    expect(callLog).toEqual(['construct:0', 'start:0', 'stop:0', 'clear:0']);

    unmount();
  });

  it('SCANNER TEST 2: start→active→stop→reopen crea una nueva instancia limpia, sin reutilizar la anterior', async () => {
    const { rerender, unmount } = renderHook((props) => useQrScanner(props), {
      initialProps: baseProps({ active: true }),
    });

    await waitFor(() => expect(instances).toHaveLength(1));
    instances[0].startDeferred.resolve();

    rerender(baseProps({ active: false }));
    await waitFor(() => expect(instances[0].stopSpy).toHaveBeenCalledTimes(1));

    rerender(baseProps({ active: true }));
    await waitFor(() => expect(instances).toHaveLength(2));
    expect(instances[1]).not.toBe(instances[0]);
    instances[1].startDeferred.resolve();
    await waitFor(() => expect(instances[1].startArgs).not.toEqual([]));

    unmount();
  });

  it('SCANNER TEST 3: toggles rápidos (activo→inactivo→activo) antes de resolver nunca crean dos instancias simultáneamente activas', async () => {
    const { rerender, unmount } = renderHook((props) => useQrScanner(props), {
      initialProps: baseProps({ active: true }),
    });
    await waitFor(() => expect(instances).toHaveLength(1));

    // El usuario entra y sale del tab varias veces, todo antes de que la
    // primera cámara siquiera termine de abrir.
    rerender(baseProps({ active: false }));
    rerender(baseProps({ active: true }));
    rerender(baseProps({ active: false }));

    // Ninguna segunda instancia debe construirse mientras la primera sigue
    // "en vuelo" — la cola serializa: cada paso espera al anterior.
    expect(instances).toHaveLength(1);
    expect(callLog).toEqual(['construct:0', 'start:0']);

    instances[0].startDeferred.resolve();
    await waitFor(() => expect(callLog).toContain('stop:0'));
    // Tras el stop de la instancia 0 (por el ÚLTIMO estado deseado:
    // inactivo), no debe quedar ninguna instancia nueva construida — el
    // último `rerender` pedía `active: false`.
    expect(instances).toHaveLength(1);
    // Orden estrictamente serializado: nunca un start antes de que el stop
    // previo haya sido despachado.
    expect(callLog).toEqual(['construct:0', 'start:0', 'stop:0', 'clear:0']);

    unmount();
  });

  it('SCANNER TEST 4: mismo hook reutilizado para Comprar con QR (elementId distinto) — comportamiento equivalente', async () => {
    const { rerender, unmount } = renderHook((props) => useQrScanner(props), {
      initialProps: baseProps({ active: true, elementId: 'pag-qr-reader' }),
    });

    await waitFor(() => expect(instances).toHaveLength(1));
    expect(instances[0].elementId).toBe('pag-qr-reader');
    instances[0].startDeferred.resolve();

    rerender(baseProps({ active: false, elementId: 'pag-qr-reader' }));
    await waitFor(() => expect(instances[0].stopSpy).toHaveBeenCalledTimes(1));
    expect(instances[0].clearSpy).toHaveBeenCalledTimes(1);

    unmount();
  });

  it('SCANNER TEST 5: desmontaje del componente mientras start() sigue pendiente igual libera el scanner', async () => {
    const { unmount } = renderHook((props) => useQrScanner(props), {
      initialProps: baseProps({ active: true }),
    });

    await waitFor(() => expect(instances).toHaveLength(1));
    const scanner = instances[0];
    expect(scanner.stopSpy).not.toHaveBeenCalled();

    unmount();
    // El cleanup del efecto encola un stop detrás del start pendiente — no
    // se ejecuta hasta que ese start resuelva.
    expect(scanner.stopSpy).not.toHaveBeenCalled();

    scanner.startDeferred.resolve();
    await waitFor(() => expect(scanner.stopSpy).toHaveBeenCalledTimes(1));
    expect(scanner.clearSpy).toHaveBeenCalledTimes(1);
  });

  it('SCANNER TEST 6: start() rechaza mientras la sesión sigue activa → limpia la instancia y SÍ notifica onError', async () => {
    const onError = vi.fn();
    const { unmount } = renderHook((props) => useQrScanner(props), {
      initialProps: baseProps({ active: true, onError }),
    });

    await waitFor(() => expect(instances).toHaveLength(1));
    const scanner = instances[0];
    scanner.startDeferred.reject(new Error('permiso de cámara denegado'));

    // XPAY-422A — antes de este fix, el catch de start() no invocaba
    // clear() en absoluto. Ahora reutiliza stopCurrent() como best-effort
    // de limpieza aunque la instancia nunca llegó a "arrancar" realmente.
    await waitFor(() => expect(scanner.clearSpy).toHaveBeenCalledTimes(1));
    expect(scanner.stopSpy).toHaveBeenCalledTimes(1);
    // La sesión seguía vigente (nunca se invalidó) → el mensaje funcional
    // SÍ debe llegar al caller.
    expect(onError).toHaveBeenCalledWith('No se pudo abrir la cámara. Puedes pegar el código QR manualmente.');
    expect(onError).toHaveBeenCalledTimes(1);

    unmount();
  });

  it('SCANNER TEST 7: start() rechaza DESPUÉS de invalidar la sesión (tab ya abandonado) → limpia pero NO notifica onError', async () => {
    const onError = vi.fn();
    const { rerender, unmount } = renderHook((props) => useQrScanner(props), {
      initialProps: baseProps({ active: true, onError }),
    });

    await waitFor(() => expect(instances).toHaveLength(1));
    const scanner = instances[0];
    // El usuario abandona el tab ANTES de que start() se asiente.
    rerender(baseProps({ active: false, onError }));
    expect(onError).not.toHaveBeenCalled();

    scanner.startDeferred.reject(new Error('permiso de cámara denegado'));

    // La limpieza física (clear del contenedor DOM) sigue ocurriendo — el
    // residuo no depende de si la sesión seguía siendo "deseada".
    await waitFor(() => expect(scanner.clearSpy).toHaveBeenCalledTimes(1));
    expect(scanner.stopSpy).toHaveBeenCalledTimes(1);
    // Pero el mensaje de error NUNCA debe publicarse: pertenece a un
    // intento ya cancelado lógicamente (Sección 2 del audit XPAY-422A).
    expect(onError).not.toHaveBeenCalled();

    unmount();
  });

  it('SCANNER TEST 8: un decode que llega DESPUÉS de invalidar la sesión no dispara onDecode', async () => {
    const onDecode = vi.fn();
    const { rerender, unmount } = renderHook((props) => useQrScanner(props), {
      initialProps: baseProps({ active: true, onDecode }),
    });

    await waitFor(() => expect(instances).toHaveLength(1));
    const scanner = instances[0];
    scanner.startDeferred.resolve();
    await waitFor(() => expect(scanner.startArgs.length).toBeGreaterThan(0));
    const qrCodeSuccessCallback = scanner.startArgs[2] as (text: string) => void;

    // El usuario sale del tab — la sesión queda invalidada — pero un último
    // frame decodificado por la cámara real puede llegar de todos modos
    // (el detector interno de html5-qrcode sigue corriendo hasta que
    // stop() complete).
    rerender(baseProps({ active: false, onDecode }));
    qrCodeSuccessCallback('XPAY_TRANSFER_TARDIO');

    await waitFor(() => expect(scanner.stopSpy).toHaveBeenCalledTimes(1));
    expect(onDecode).not.toHaveBeenCalled();

    unmount();
  });
});
