import { useEffect } from 'react';
import { fmtMoney, fmtDate } from '../../utils.ts';
import type { CommerceNotification } from './CommerceNotificationsContext.tsx';

interface IconProps {
  size?: number;
}
function IconClose({ size = 16 }: IconProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true"
      fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
      <path d="M6 6l12 12M18 6 6 18" />
    </svg>
  );
}

interface CommerceNotificationsPanelProps {
  notifications: CommerceNotification[];
  onMarkAllSeen: () => void;
  onClose: () => void;
}

// XPAY-438 — panel de notificación operacional de venta QR (comercio). Copia
// adaptada de NotificationsPanel.tsx (Wallet, XPAY-431) — mismo contrato de
// interacción (Escape/click-fuera cierran, "Marcar como vistas" explícito),
// payload distinto. Deliberadamente NO se generaliza en un componente
// compartido (ver auditoría XPAY-437 §9: payloads y host de UI distintos).
//
// Abrir/cerrar este panel NUNCA marca nada como visto (sección 13 del
// ticket) — eso requiere únicamente el botón "Marcar como vistas".
export function CommerceNotificationsPanel({ notifications, onMarkAllSeen, onClose }: CommerceNotificationsPanelProps) {
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return (
    <>
      {/* Overlay propio — .wallet-overlay está prefijado .wallet-shell-v2 y
          no aplica fuera de la vista wallet (mismo criterio ya usado por
          .wallet-send-success-overlay, ver comentario en wallet-shell.css). */}
      <div className="commerce-notifications-overlay" onClick={onClose} />
      <div className="commerce-notifications-panel" role="dialog" aria-label="Notificaciones" aria-modal="true">
        <div className="commerce-notifications-panel-header">
          <span className="commerce-notifications-panel-title">Notificaciones</span>
          <button className="commerce-notifications-close" type="button" onClick={onClose} aria-label="Cerrar notificaciones">
            <IconClose size={16} />
          </button>
        </div>

        {notifications.length === 0 ? (
          <p className="commerce-notifications-empty">No tienes notificaciones nuevas.</p>
        ) : (
          <ul className="commerce-notifications-list">
            {notifications.map(n => (
              <li key={n.idVentaQr} className="commerce-notifications-item">
                <span className="commerce-notifications-item-title">Pago recibido</span>
                <span className="commerce-notifications-item-amount">{fmtMoney(n.valorBruto)}</span>
                {/* XPAY-451 §10/11 — identidad mínima del pagador (fix P3
                    de XPAY-450). Siempre viene poblada por el backend
                    ("Cliente XPAY" si no hay nombre registrado). */}
                <span className="commerce-notifications-item-pagador">De: {n.pagadorDisplay}</span>
                <span className="commerce-notifications-item-venta">Venta #{n.idVentaQr}</span>
                {n.nombreTienda && (
                  <span className="commerce-notifications-item-tienda">Tienda: {n.nombreTienda}</span>
                )}
                <span className="commerce-notifications-item-date">{fmtDate(n.fechaVenta)}</span>
              </li>
            ))}
          </ul>
        )}

        <button
          type="button"
          className="commerce-notifications-mark-seen-btn"
          onClick={onMarkAllSeen}
          disabled={notifications.length === 0}
        >
          Marcar como vistas
        </button>
      </div>
    </>
  );
}
