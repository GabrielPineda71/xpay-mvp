import { useEffect } from 'react';
import { fmtMoney, fmtDate } from '../../utils.ts';
import type { WalletNotification } from './WalletNotificationsContext.tsx';

interface IconProps {
  size?: number;
}
function Svg({ size = 20, children }: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      width={size} height={size} viewBox="0 0 24 24" aria-hidden="true"
      fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round"
    >
      {children}
    </svg>
  );
}
function IconClose(p: IconProps) {
  return <Svg {...p}><path d="M6 6l12 12M18 6 6 18" /></Svg>;
}

interface NotificationsPanelProps {
  notifications: WalletNotification[];
  onMarkAllSeen: () => void;
  onClose: () => void;
}

// Panel de notificaciones derivadas de movimientos (XPAY-431, Block C v1).
// Mismo patrón visual que ProfileSheet.tsx (overlay + panel anclado bajo el
// ícono del hero, Escape/click-fuera cierran) — deliberadamente NO un
// framework de modales nuevo, solo la reutilización de ese mismo patrón ya
// establecido. Presentación pura: recibe los datos ya resueltos por el
// contexto (WalletNotificationsContext), sin llamar a ningún endpoint.
//
// Abrir este panel NO marca nada como visto por sí solo — eso requiere la
// acción explícita del botón "Marcar como vistas" (ver §7 del ticket).
export function NotificationsPanel({ notifications, onMarkAllSeen, onClose }: NotificationsPanelProps) {
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return (
    <>
      <div className="wallet-overlay" onClick={onClose} />
      <div className="wallet-notifications-panel" role="dialog" aria-label="Notificaciones" aria-modal="true">
        <div className="wallet-notifications-panel-header">
          <span className="wallet-notifications-panel-title">Notificaciones</span>
          <button className="wallet-notifications-close" type="button" onClick={onClose} aria-label="Cerrar notificaciones">
            <IconClose size={16} />
          </button>
        </div>

        {notifications.length === 0 ? (
          <p className="wallet-notifications-empty">No tienes notificaciones nuevas.</p>
        ) : (
          <ul className="wallet-notifications-list">
            {notifications.map(n => (
              <li key={n.idMovimiento} className="wallet-notifications-item">
                <span className="wallet-notifications-item-text">
                  Recibiste {fmtMoney(n.valor)} de <strong>{n.contraparte}</strong>
                </span>
                <span className="wallet-notifications-item-date">{fmtDate(n.fecha)}</span>
              </li>
            ))}
          </ul>
        )}

        <button
          type="button"
          className="wallet-notifications-mark-seen-btn"
          onClick={onMarkAllSeen}
          disabled={notifications.length === 0}
        >
          Marcar como vistas
        </button>
      </div>
    </>
  );
}
