import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';

// XPAY-431 (Block C v1) — notificación persistente al RECEPTOR de una
// transferencia interna Wallet-a-Wallet. La fuente de verdad sigue siendo
// exclusivamente WalletMovimiento (backend, ya persistido en la misma
// transacción SQL que afecta el saldo — ver auditoría read-only XPAY-430).
// Este contexto NO crea ningún dato financiero nuevo: solo deriva, en el
// cliente, un estado de UX (leído/no-leído) sobre movimientos que ya
// existen. Nunca llama a ningún endpoint financiero.
//
// ── Modelo de DOS cursores (regla crítica de XPAY-431 §2, hallazgo central
//    de XPAY-430 §3) — nunca deben volver a mezclarse: ──────────────────────
// - "cargado": lo sigue manejando UserWalletPage.tsx exactamente igual que
//   antes (loadCuenta/pollRefresh) — no es responsabilidad de este contexto.
// - "visto" (lastSeen): SOLO avanza cuando el usuario ejecuta la acción
//   explícita markAllAsSeen(). Se persiste en localStorage, namespaced por
//   idWallet, y NUNCA se sobrescribe automáticamente por un simple
//   refresh/remount. La única otra vez que se escribe es el bootstrap de
//   primer uso (ver reportMovimientos), y solo si todavía no existe ningún
//   valor guardado para esa wallet — así se evita convertir TODO el
//   historial financiero previo en "no leídas" al instalar esta función.
//
// En localStorage se persiste ÚNICAMENTE el cursor numérico — ningún monto,
// nombre de contraparte, ni ningún otro dato financiero.

export interface WalletNotification {
  idMovimiento: number;
  valor: number;
  fecha: string;
  contraparte: string; // ya resuelto por el caller (username o "Wallet #N") — este contexto no conoce WALLET_USER_MAP.
}

// Forma mínima que este contexto necesita de un movimiento — deliberadamente
// más chica que la interfaz `Movimiento` completa de UserWalletPage.tsx, para
// no acoplar este archivo a esa página.
export interface MovimientoParaNotificacion {
  idMovimiento: number;
  tipoMovimiento: string;
  valor: number;
  fecha: string;
  referenciaId: number | null;
}

interface WalletNotificationsContextValue {
  unreadCount: number;
  notifications: WalletNotification[];
  markAllAsSeen: () => void;
  reportMovimientos: (
    idWallet: number,
    movimientos: ReadonlyArray<MovimientoParaNotificacion>,
    resolveContraparte: (referenciaId: number | null) => string,
  ) => void;
}

function storageKey(idWallet: number): string {
  return `xpay.wallet.${idWallet}.notifications.lastSeenMovementId`;
}

function readLastSeen(idWallet: number): number | null {
  try {
    const raw = window.localStorage.getItem(storageKey(idWallet));
    if (raw === null) return null;
    const parsed = Number(raw);
    return Number.isFinite(parsed) ? parsed : null;
  } catch {
    // localStorage puede no estar disponible (modo privado, storage
    // bloqueado, etc.) — degradar con seguridad a "sin cursor persistido",
    // nunca romper la carga de la wallet por esto.
    return null;
  }
}

function writeLastSeen(idWallet: number, value: number): void {
  try {
    window.localStorage.setItem(storageKey(idWallet), String(value));
  } catch {
    // Ignorar — la notificación sigue funcionando en memoria durante esta
    // sesión aunque no sobreviva un refresh en este dispositivo/navegador.
  }
}

// Valor por defecto SIN provider — deliberadamente no-op en vez de lanzar.
// Así, cualquier test/página que monte UserWalletPage.tsx fuera del árbol de
// WalletNotificationsProvider (toda la suite existente pre-XPAY-431) sigue
// funcionando sin cambios: Block C simplemente queda inactivo, nunca rompe
// nada ajeno a esta función.
const noopValue: WalletNotificationsContextValue = {
  unreadCount: 0,
  notifications: [],
  markAllAsSeen: () => {},
  reportMovimientos: () => {},
};

const WalletNotificationsContext = createContext<WalletNotificationsContextValue>(noopValue);

// Monta UNA sola vez, envolviendo tanto WalletShell (donde vive la campana,
// WalletHero.tsx) como el <Outlet/> ruteado (donde vive UserWalletPage.tsx)
// — ver Layout.tsx. XPAY-430 confirmó que esos dos son ramas hermanas del
// árbol de componentes sin estado compartido; este provider es la pieza
// mínima que lo resuelve, sin Redux/Zustand ni rediseñar el estado de
// Wallet.
export function WalletNotificationsProvider({ children }: { children: ReactNode }) {
  const [currentWalletId, setCurrentWalletId]     = useState<number | null>(null);
  const [lastSeenByWallet, setLastSeenByWallet]   = useState<Record<number, number>>({});
  const [unreadByWallet, setUnreadByWallet]       = useState<Record<number, WalletNotification[]>>({});

  // XPAY-431A — ref-mirror de lastSeenByWallet (mismo patrón que
  // kycEstadoRef/onDecodeRef ya usado en UserWalletPage.tsx/useQrScanner.ts):
  // reportMovimientos necesita LEER el cursor actual para decidir si hace
  // bootstrap, pero NO debe cambiar de identidad cada vez que ese cursor
  // cambia. Antes, tener `lastSeenByWallet` en las deps de su useCallback
  // hacía que, tras cada bootstrap o "marcar como vistas", reportMovimientos
  // (y por lo tanto loadCuenta/pollRefresh, que lo incluyen en sus propias
  // deps) cambiaran de identidad — lo que re-disparaba el useEffect de
  // montaje de UserWalletPage.tsx (re-fetch redundante de las 4 llamadas
  // iniciales) y reinstalaba el intervalo de polling de 7s. Un ref siempre
  // está actualizado sin necesitar aparecer en ningún arreglo de
  // dependencias — bug real encontrado y corregido en la revisión XPAY-431A.
  const lastSeenByWalletRef = useRef(lastSeenByWallet);
  lastSeenByWalletRef.current = lastSeenByWallet;

  const reportMovimientos = useCallback<WalletNotificationsContextValue['reportMovimientos']>(
    (idWallet, movimientos, resolveContraparte) => {
      const existingCursor = lastSeenByWalletRef.current[idWallet];
      let cursor: number;
      if (existingCursor !== undefined) {
        cursor = existingCursor;
      } else {
        const stored = readLastSeen(idWallet);
        if (stored !== null) {
          cursor = stored;
        } else {
          // Primer uso jamás visto para esta wallet en este dispositivo:
          // baseline seguro = el movimiento más reciente YA CARGADO, sin
          // generar notificaciones retroactivas sobre historial financiero
          // previo (XPAY-430 §3 / XPAY-431 §3).
          cursor = movimientos[0]?.idMovimiento ?? 0;
          writeLastSeen(idWallet, cursor);
        }
      }

      // Recorre TODOS los movimientos posteriores al cursor — no solo el
      // más reciente (corrige el gap de "múltiples recepciones entre dos
      // polls" encontrado en XPAY-430 §6). Se recalcula completo en cada
      // llamada a partir de la lista completa ya cargada (idempotente: no
      // acumula, no duplica entre polls sucesivos).
      const nuevas: WalletNotification[] = movimientos
        .filter(m => m.tipoMovimiento === 'TRANSFERENCIA_ENTRADA' && m.idMovimiento > cursor)
        .slice()
        .sort((a, b) => a.idMovimiento - b.idMovimiento)
        .map(m => ({
          idMovimiento: m.idMovimiento,
          valor:        m.valor,
          fecha:        m.fecha,
          contraparte:  resolveContraparte(m.referenciaId),
        }));

      setCurrentWalletId(idWallet);
      setLastSeenByWallet(prev => (prev[idWallet] === cursor ? prev : { ...prev, [idWallet]: cursor }));
      setUnreadByWallet(prev => {
        const existing = prev[idWallet] ?? [];
        // Nada cambió (caso más común: ningún crédito nuevo desde el último
        // poll) — evita un re-render de la campana/panel en cada tick.
        if (existing.length === 0 && nuevas.length === 0) return prev;
        return { ...prev, [idWallet]: nuevas };
      });
    },
    // Deliberadamente estable (sin dependencias): lee lastSeenByWalletRef,
    // no lastSeenByWallet — ver comentario arriba.
    [],
  );

  const markAllAsSeen = useCallback(() => {
    if (currentWalletId === null) return;
    const pending = unreadByWallet[currentWalletId] ?? [];
    if (pending.length === 0) return;
    const base  = lastSeenByWallet[currentWalletId] ?? 0;
    const maxId = pending.reduce((max, n) => Math.max(max, n.idMovimiento), base);
    writeLastSeen(currentWalletId, maxId);
    setLastSeenByWallet(prev => ({ ...prev, [currentWalletId]: maxId }));
    setUnreadByWallet(prev => ({ ...prev, [currentWalletId]: [] }));
  }, [currentWalletId, unreadByWallet, lastSeenByWallet]);

  const notifications = currentWalletId !== null ? unreadByWallet[currentWalletId] ?? [] : [];

  const value = useMemo<WalletNotificationsContextValue>(() => ({
    unreadCount: notifications.length,
    notifications,
    markAllAsSeen,
    reportMovimientos,
  }), [notifications, markAllAsSeen, reportMovimientos]);

  return (
    <WalletNotificationsContext.Provider value={value}>
      {children}
    </WalletNotificationsContext.Provider>
  );
}

export function useWalletNotifications(): WalletNotificationsContextValue {
  return useContext(WalletNotificationsContext);
}
