import { useEffect, useRef } from 'react';
import { Html5Qrcode } from 'html5-qrcode';

interface UseQrScannerOptions {
  // Igual que antes (envScanning && tab === 'enviar' / pagScanning && tab
  // === 'pagar') — la fuente de verdad de "¿debería estar escaneando?"
  // sigue viviendo en el estado de UserWalletPage.tsx; este hook solo
  // reacciona a este booleano.
  active: boolean;
  // ID del contenedor DOM ya renderizado condicionalmente por el caller
  // ('env-qr-reader' / 'pag-qr-reader') — fijo por cada uso del hook.
  elementId: string;
  onDecode: (text: string) => void;
  onError: (message: string) => void;
}

// XPAY-422 Parte D — ciclo de vida determinista y SERIALIZADO de un scanner
// html5-qrcode. Reemplaza los dos useEffect casi idénticos que existían para
// Enviar/Comprar con QR (XPAY-390), unificando el lifecycle en un solo lugar
// reutilizable.
//
// Causa raíz identificada en XPAY-421: `scanner.start()` es asíncrono
// (negocia getUserMedia — en móvil, tiempo variable no trivial). Si el
// efecto se invalidaba (cambio de tab) MIENTRAS start() seguía "en vuelo",
// el cleanup anterior llamaba `scanner.stop()` inmediatamente sobre una
// instancia cuyo MediaStream real aún no había terminado de negociarse —
// sin garantía de liberar la cámara a nivel de SO/navegador (stream
// "huérfano"), bloqueando un siguiente intento de abrir la cámara.
//
// Solución: TODA transición (iniciar o detener) se encola en una ÚNICA
// cadena de promesas (`chainRef`). Como la cadena ejecuta sus pasos
// estrictamente EN ORDEN, un `stop` solicitado mientras un `start` sigue
// pendiente NUNCA se ejecuta en paralelo con él — espera a que ese `start`
// termine por completo (éxito o error) antes de intentar detener, sobre una
// instancia ya en un estado conocido. Esto es control explícito del
// lifecycle vía las propias promesas reales de la librería — NO
// setTimeout/sleep/retry ciego.
//
// Ciclo conceptual: IDLE → STARTING → ACTIVE → STOPPING → IDLE. Nunca se
// permite STARTING + un segundo start() simultáneo (la cola lo serializa);
// nunca se considera "liberado" un STOP incompleto (siempre se espera el
// start() previo antes de intentar detener).
export function useQrScanner({ active, elementId, onDecode, onError }: UseQrScannerOptions) {
  const scannerRef = useRef<Html5Qrcode | null>(null);
  const chainRef = useRef<Promise<void>>(Promise.resolve());
  // Refs estables: los callbacks del caller pueden ser closures nuevas en
  // cada render (dependen de estado del componente) — nunca deben forzar
  // reconstruir la cadena de scanner en curso.
  const onDecodeRef = useRef(onDecode);
  const onErrorRef = useRef(onError);
  onDecodeRef.current = onDecode;
  onErrorRef.current = onError;

  async function stopCurrent(): Promise<void> {
    const scanner = scannerRef.current;
    scannerRef.current = null;
    if (!scanner) return;
    try {
      await scanner.stop();
    } catch (err) {
      // No visible para el usuario — sólo diagnóstico de lifecycle (D3):
      // puede ocurrir de forma esperada si la cámara nunca llegó a
      // activarse realmente (start() había fallado) o si el elemento ya
      // fue desmontado.
      console.warn('[useQrScanner] scanner.stop() falló (posiblemente ya detenido):', err);
    }
    try {
      scanner.clear();
    } catch (err) {
      console.warn('[useQrScanner] scanner.clear() falló (contenedor ya desmontado):', err);
    }
  }

  async function startNew(elementId: string, isStillWanted: () => boolean): Promise<void> {
    // Para cuando le toca el turno a este paso de la cola, puede que ya no
    // se quiera escanear (el usuario salió del tab antes de llegar aquí) —
    // no crear ninguna instancia nueva en ese caso.
    if (!isStillWanted()) return;

    const scanner = new Html5Qrcode(elementId);
    scannerRef.current = scanner;

    try {
      await scanner.start(
        { facingMode: 'environment' },
        { fps: 10, qrbox: { width: 250, height: 250 } },
        (text) => { if (isStillWanted()) onDecodeRef.current(text); },
        () => { /* per-frame decode miss — normal, ignored */ },
      );
      // start() acaba de resolver con éxito — el MediaStream ya está
      // completamente establecido. Si mientras tanto el usuario ya salió
      // del tab, es seguro detenerlo AHORA MISMO (ya no hay condición de
      // carrera: la instancia está en un estado conocido y completo).
      if (!isStillWanted()) await stopCurrent();
    } catch (err) {
      console.warn('[useQrScanner] scanner.start() falló:', err);
      // XPAY-422A — antes esta rama sólo hacía `scannerRef.current = null`
      // sin invocar clear(): un start() rechazado puede haber dejado nodos
      // DOM parciales (video/canvas) insertados por html5-qrcode dentro del
      // contenedor antes de fallar. Reutiliza stopCurrent() (best-effort,
      // nunca lanza) para limpiarlos de forma consistente, permitiendo un
      // reintento posterior limpio — sea la sesión actual la que sigue
      // vigente o una ya invalidada (en ambos casos hay el mismo residuo
      // potencial que limpiar).
      await stopCurrent();
      if (isStillWanted()) {
        onErrorRef.current('No se pudo abrir la cámara. Puedes pegar el código QR manualmente.');
      }
    }
  }

  useEffect(() => {
    let wanted = active;
    const isStillWanted = () => wanted;

    chainRef.current = chainRef.current.then(async () => {
      if (wanted) {
        await startNew(elementId, isStillWanted);
      } else {
        await stopCurrent();
      }
    });

    // El cleanup de este único efecto cubre AMBOS casos: cambio de deps
    // (active/elementId) y desmontaje completo del componente (React
    // siempre ejecuta el cleanup del efecto vigente al desmontar, sin
    // importar sus dependencias) — incluyendo un start() todavía en curso
    // durante el unmount (SCANNER TEST 5): el stop() encolado aquí espera
    // su turno en la cadena, después de que ese start() termine.
    return () => {
      wanted = false;
      chainRef.current = chainRef.current.then(() => stopCurrent());
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, elementId]);
}
