import { useState } from 'react';
import { useCommerceNotifications } from './CommerceNotificationsContext.tsx';
import { CommerceNotificationsPanel } from './CommerceNotificationsPanel.tsx';

function IconBell({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true"
      fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
      <path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9" />
      <path d="M13.73 21a2 2 0 0 1-3.46 0" />
    </svg>
  );
}

// XPAY-438 — campana de notificación operacional de venta QR, vista
// COMERCIO. Montada en Layout.tsx (zona nav-user, junto a los controles de
// usuario/logout — sección 11 del ticket). Deliberadamente NO reutiliza
// WalletHero (esa vive sólo en la vista wallet, ver auditoría XPAY-437 §8).
//
// Abrir/cerrar este panel NUNCA marca nada como visto — sólo el botón
// explícito "Marcar como vistas" dentro del panel (sección 13).
export function CommerceNotificationBell() {
  const [open, setOpen] = useState(false);
  const { unreadCount, notifications, markAllAsSeen } = useCommerceNotifications();

  return (
    <div className="commerce-notification-bell-wrap">
      <button
        type="button"
        className="commerce-notification-bell-btn"
        onClick={() => setOpen(o => !o)}
        aria-label="Notificaciones de ventas"
      >
        <IconBell size={20} />
        {unreadCount > 0 && <span className="commerce-notification-dot" aria-hidden="true" />}
      </button>

      {open && (
        <CommerceNotificationsPanel
          notifications={notifications}
          onMarkAllSeen={markAllAsSeen}
          onClose={() => setOpen(false)}
        />
      )}
    </div>
  );
}
