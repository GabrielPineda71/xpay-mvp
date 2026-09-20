import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { listarVentasQrDesde, obtenerUltimoIdVentaQr, type VentaQrNotificacion } from '../../api/caja.ts';

// XPAY-438/438A — notificación operacional de venta QR a nivel COMERCIO.
// Fuente de verdad: VentaQr (a través del modo commerce-wide de
// GET /api/comercio/ventas?desdeIdVentaQr=N — ver ComercioScopeService.
// ListarVentasIncrementalAsync). Contexto PEQUEÑO Y AUTOCONTENIDO, calcado
// del patrón ya validado en WalletNotificationsContext.tsx (XPAY-431/431A)
// — deliberadamente NO generaliza ese contexto ni comparte código con él
// (payload distinto, host de UI distinto — ver auditoría XPAY-437 §9).
//
// Diferencia estructural clave respecto a Wallet: UserWalletPage.tsx YA
// tenía un polling de 7s propio (pollRefresh) antes de Block C, que
// simplemente reportaba sus movimientos ya cargados. La vista comercio no
// tiene ningún polling previo — este provider posee su PROPIO ciclo de
// fetch (XPAY-437 §8/§9), independiente de MiComercioPage.tsx.
//
// ── DOS CURSORES (sección 9 de XPAY-438) — nunca deben confundirse: ───────
// - "detección" (detectionCursorRef, sólo en memoria): dónde continuar
//   pidiendo al backend (IdVentaQr > detección). Avanza en CADA página
//   recibida, para no volver a pedir lo mismo.
// - "visto" (seenCursor, persistido en localStorage): SOLO avanza con la
//   acción explícita markAllAsSeen(). Abrir/cerrar el panel NUNCA lo mueve.
// En remount, la detección arranca siempre desde el cursor de visto
// persistido — así una ausencia larga se "recupera" completa (drenaje
// multi-página, sección 3/10 — ver MAX_PAGES_PER_CYCLE debajo).
//
// En localStorage se persiste ÚNICAMENTE el cursor numérico "visto" — nunca
// monto, nombre de tienda, fecha, ni la lista de ventas.
//
// ── XPAY-438A — 3 correcciones sobre la primera versión: ──────────────────
// 1. BASELINE SIN DRENAJE (§2): la primera versión obtenía el baseline de
//    primer uso drenando desde 0 con el mismo mecanismo de páginas del
//    polling normal (tope MAX_PAGES_PER_CYCLE×PAGE_SIZE = 2.000 filas). Un
//    comercio con MÁS de 2.000 VentaQr históricas habría dejado el baseline
//    congelado en la venta #2.000 (la más antigua alcanzada), y todo lo
//    posterior (2.001..N, historial real) se habría notificado como "nuevo"
//    en los polls siguientes — exactamente el bug que este ticket pide
//    verificar. Corregido: el baseline ahora usa
//    GET /api/comercio/ventas/ultimo-id (ComercioScopeService.
//    ObtenerUltimoIdVentaQrAsync — una sola consulta MAX(id) por comercio,
//    indiferente a 0/100/2.000/100.000 filas). El drenaje por páginas
//    (drainAsync) se conserva EXCLUSIVAMENTE para el polling normal
//    posterior al baseline, donde SÍ es información real que debe mostrarse
//    (nunca se descarta, sólo puede tardar más de un ciclo en completarse
//    — sección 3).
// 2. GUARD IN-FLIGHT (§4): el intervalo de 7s ahora nunca dispara un nuevo
//    drainAsync si el anterior sigue en curso (una consulta lenta ya no
//    puede solaparse con el siguiente tick).
// 3. GUARD DE GENERACIÓN (§5): un cambio de comercio a mitad de una
//    petición en curso ya no puede insertar notificaciones ni mover el
//    cursor de detección del comercio NUEVO con datos del comercio VIEJO —
//    mountedRef solo protege un unmount real, no un cambio de prop en el
//    mismo componente montado.

const PAGE_SIZE          = 100; // debe coincidir con el tope defensivo del backend.
const MAX_PAGES_PER_CYCLE = 20; // fail-safe contra loops patológicos, SOLO aplica al polling normal (nunca al baseline — ver nota XPAY-438A arriba).
const POLL_INTERVAL_MS   = 7000; // mismo valor ya validado en Block C (Wallet).

export interface CommerceNotification {
  idVentaQr:    number;
  valorBruto:   number;
  fechaVenta:   string;
  idTienda:     number;
  nombreTienda: string | null;
}

interface CommerceNotificationsContextValue {
  unreadCount:   number;
  notifications: CommerceNotification[];
  markAllAsSeen: () => void;
}

function storageKey(idComercio: number): string | null {
  // Nunca construir la key con undefined/null/0 — 0 no es un IdComercio real
  // (BIGINT IDENTITY arranca en 1); tratarlo como "no resuelto todavía".
  if (!Number.isFinite(idComercio) || idComercio <= 0) return null;
  return `xpay.comercio.${idComercio}.notifications.lastSeenVentaQrId`;
}

function readSeenCursor(idComercio: number): number | null {
  const key = storageKey(idComercio);
  if (key === null) return null;
  try {
    const raw = window.localStorage.getItem(key);
    if (raw === null) return null;
    const parsed = Number(raw);
    return Number.isFinite(parsed) ? parsed : null;
  } catch {
    // localStorage puede no estar disponible — degradar con seguridad.
    return null;
  }
}

function writeSeenCursor(idComercio: number, value: number): void {
  const key = storageKey(idComercio);
  if (key === null) return;
  try {
    window.localStorage.setItem(key, String(value));
  } catch {
    // Ignorar — sigue funcionando en memoria durante la sesión.
  }
}

function mapNotification(v: VentaQrNotificacion): CommerceNotification {
  return {
    idVentaQr:    v.idVentaQr,
    valorBruto:   v.valorBruto,
    fechaVenta:   v.fechaVenta,
    idTienda:     v.idTienda,
    nombreTienda: v.nombreTienda,
  };
}

const noopValue: CommerceNotificationsContextValue = {
  unreadCount:   0,
  notifications: [],
  markAllAsSeen: () => {},
};

const CommerceNotificationsContext = createContext<CommerceNotificationsContextValue>(noopValue);

// idComercioExistente: null mientras useComercioScope no ha resuelto el
// scope (o si el rol no lo tiene — ver XPAY-438 §2, ningún rol mostrado con
// campana carece de él en la práctica, pero el provider igual espera de
// forma segura). El padre (Layout.tsx) es responsable de pasar el valor ya
// resuelto — este provider NUNCA llama a /api/comercio/mi-scope por sí
// mismo, para no duplicar esa llamada (Layout.tsx ya la hace).
export function CommerceNotificationsProvider(
  { idComercioExistente, children }: { idComercioExistente: number | null; children: ReactNode },
) {
  const [notifications, setNotifications] = useState<CommerceNotification[]>([]);
  const [seenCursor,    setSeenCursor]    = useState<number | null>(null);

  // ref-mirror (mismo patrón que lastSeenByWalletRef en XPAY-431A) — el
  // ciclo de polling necesita SIEMPRE el valor más fresco sin forzar que el
  // efecto de montaje del intervalo dependa de notifications/seenCursor.
  const detectionCursorRef = useRef<number | null>(null);
  const notificationsRef   = useRef<CommerceNotification[]>([]);
  notificationsRef.current = notifications;
  const seenCursorRef      = useRef<number | null>(null);
  seenCursorRef.current    = seenCursor;
  const idComercioRef      = useRef<number | null>(idComercioExistente);
  idComercioRef.current    = idComercioExistente;

  // Guard "no state update after unmount" — drainAsync es un loop async con
  // awaits; sin esto, un unmount a mitad de drenaje seguiría llamando
  // setNotifications al volver del último await.
  const mountedRef = useRef(true);
  useEffect(() => () => { mountedRef.current = false; }, []);

  // XPAY-438A §5 — guard de generación: cada activación (mount o cambio de
  // idComercioExistente) incrementa generationRef. Toda petición async en
  // curso captura SU generación al iniciar y, tras cada await, comprueba
  // que sigue siendo la generación vigente antes de tocar cualquier estado
  // — así una respuesta tardía del comercio A nunca puede escribir en el
  // estado del comercio B ya activo.
  const generationRef = useRef(0);

  // XPAY-438A §4 — guard in-flight: el intervalo de polling nunca dispara
  // un nuevo ciclo si uno anterior sigue en curso (consulta >7s). NO
  // bloquea la llamada de baseline/reanudación del efecto de activación
  // (ésa debe ejecutarse siempre exactamente una vez por generación) — sólo
  // el intervalo la consulta antes de decidir si dispara un nuevo tick.
  const inFlightRef = useRef(false);

  // Drena páginas de 100 desde `desde` hasta agotar lo nuevo (o el tope de
  // páginas por ciclo). Usado EXCLUSIVAMENTE por el polling normal
  // (colectando unread real) y por la reanudación tras un cursor
  // persistido (mismo caso: hay que recuperar lo pendiente, sea 1 o miles
  // de ventas — nunca se descarta, sólo puede tardar varios ciclos).
  const drainAsync = useCallback(async (desde: number, collectUnread: boolean, generation: number): Promise<number> => {
    let cursor = desde;
    inFlightRef.current = true;
    try {
      for (let intento = 0; intento < MAX_PAGES_PER_CYCLE; intento++) {
        let pagina: VentaQrNotificacion[];
        try {
          pagina = await listarVentasQrDesde(cursor);
        } catch (err) {
          // Sección 15 (XPAY-438) — un error de polling no borra unread ni
          // avanza el cursor de visto; el progreso YA aplicado de páginas
          // previas en este mismo ciclo se conserva (no se deshace).
          console.warn('[CommerceNotifications] error en poll, se conserva estado previo', err);
          break;
        }
        if (!mountedRef.current) break;               // desmontado mientras esperábamos.
        if (generation !== generationRef.current) break; // comercio cambió mientras esperábamos (XPAY-438A §5).
        if (pagina.length === 0) break;

        const maxDePagina = pagina.reduce((m, v) => Math.max(m, v.idVentaQr), cursor);
        if (collectUnread) {
          setNotifications(prev => {
            const vistos = new Set(prev.map(n => n.idVentaQr));
            const nuevas = pagina.filter(v => !vistos.has(v.idVentaQr)).map(mapNotification);
            if (nuevas.length === 0) return prev;
            return [...prev, ...nuevas].sort((a, b) => a.idVentaQr - b.idVentaQr);
          });
        }
        cursor = maxDePagina;
        detectionCursorRef.current = cursor;

        if (pagina.length < PAGE_SIZE) break; // ya no hay más páginas pendientes.
        // pagina.length === PAGE_SIZE → puede haber más, continuar drenando
        // dentro del mismo ciclo (tope MAX_PAGES_PER_CYCLE como fail-safe —
        // XPAY-438A §3: al alcanzarlo, el cursor queda EXACTAMENTE en la
        // última página realmente recibida y aplicada; nada se salta, nada
        // se marca como visto, y el siguiente poll continúa desde ahí).
      }
    } finally {
      inFlightRef.current = false;
    }
    return cursor;
  }, []);

  // Reinicia/activa el provider cuando cambia el comercio resuelto (login,
  // cambio de sesión, o resolución tardía de useComercioScope). Nunca mezcla
  // notificaciones entre comercios (sección 14, XPAY-438).
  useEffect(() => {
    generationRef.current += 1;
    const myGeneration = generationRef.current;

    setNotifications([]);
    detectionCursorRef.current = null; // = señal "baseline aún no resuelto" hasta que se fije abajo.

    if (idComercioExistente === null) { setSeenCursor(null); return; }

    (async () => {
      const persistido = readSeenCursor(idComercioExistente);
      if (persistido !== null) {
        // No es primer uso: retoma desde el cursor de visto persistido y
        // drena de inmediato lo que haya quedado pendiente (offline/ventas
        // >100 mientras el comercio estuvo cerrado — sección 10, XPAY-438).
        if (myGeneration !== generationRef.current) return;
        setSeenCursor(persistido);
        detectionCursorRef.current = persistido;
        await drainAsync(persistido, /* collectUnread */ true, myGeneration);
      } else {
        // XPAY-438A §2 — primer uso real para este comercio en este
        // dispositivo: baseline = IdVentaQr más reciente visible, obtenido
        // con UNA sola consulta (nunca drenando el historial completo —
        // ver ObtenerUltimoIdVentaQrAsync/ventas/ultimo-id). unread = 0,
        // nunca convertir historial en notificaciones (sección 4/8).
        let maxId: number;
        try {
          maxId = await obtenerUltimoIdVentaQr();
        } catch (err) {
          console.warn('[CommerceNotifications] error obteniendo baseline, se reintentará en el próximo activación', err);
          return; // sin cursor persistido: el próximo remount/activación lo reintenta igual que ahora.
        }
        if (myGeneration !== generationRef.current) return; // comercio cambió mientras esperábamos.
        writeSeenCursor(idComercioExistente, maxId);
        setSeenCursor(maxId);
        detectionCursorRef.current = maxId;
      }
    })();
  }, [idComercioExistente, drainAsync]);

  // Ciclo de polling propio — depende ÚNICAMENTE de idComercioExistente (se
  // reinstala en cambio de comercio, se detiene si no hay comercio
  // resuelto). detectionCursorRef.current === null es la señal de "baseline
  // aún no resuelto" — deliberadamente NO se usó un estado `ready` para
  // esto: habría entrado en las deps de este efecto y reinstalado el
  // intervalo (reseteando la cuenta de 7s) justo cuando el baseline
  // termina, la misma clase de bug que XPAY-431A corrigió en Wallet
  // (identidad inestable por incluir en deps un estado que cambia una sola
  // vez). Lee todo lo demás vía refs.
  useEffect(() => {
    if (idComercioExistente === null) return;

    const id = setInterval(() => {
      if (inFlightRef.current) return;               // XPAY-438A §4 — ya hay un drenaje en curso, este tick se omite.
      if (detectionCursorRef.current === null) return; // baseline aún en curso.
      void drainAsync(detectionCursorRef.current, /* collectUnread */ true, generationRef.current);
    }, POLL_INTERVAL_MS);

    return () => clearInterval(id);
  }, [idComercioExistente, drainAsync]);

  const markAllAsSeen = useCallback(() => {
    const idComercio = idComercioRef.current;
    if (idComercio === null) return;
    const pendientes = notificationsRef.current;
    if (pendientes.length === 0) return;
    const base  = seenCursorRef.current ?? 0;
    const maxId = pendientes.reduce((m, n) => Math.max(m, n.idVentaQr), base);
    writeSeenCursor(idComercio, maxId);
    setSeenCursor(maxId);
    setNotifications([]);
    // Deliberadamente NO toca detectionCursorRef — la detección ya está, en
    // el peor caso, igual o más adelantada que maxId (nunca retrocede). Esto
    // es también lo que hace segura la carrera "marcar como vistas durante
    // un poll en curso" (XPAY-438A §6): el poll en vuelo arrancó con un
    // cursor <= detectionCursorRef.current actual, así que cualquier venta
    // que todavía traiga en su respuesta es, por construcción, más nueva
    // que todo lo que este click acaba de marcar como visto — se añade
    // sobre la lista ya vaciada, nunca se pierde ni se re-marca como vista.
  }, []);

  const value = useMemo<CommerceNotificationsContextValue>(() => ({
    unreadCount: notifications.length,
    notifications,
    markAllAsSeen,
  }), [notifications, markAllAsSeen]);

  return (
    <CommerceNotificationsContext.Provider value={value}>
      {children}
    </CommerceNotificationsContext.Provider>
  );
}

export function useCommerceNotifications(): CommerceNotificationsContextValue {
  return useContext(CommerceNotificationsContext);
}
