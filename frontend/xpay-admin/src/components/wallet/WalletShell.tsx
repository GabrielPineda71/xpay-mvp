import { useState, type ReactNode } from 'react';
import { WalletHero } from './WalletHero.tsx';
import { BottomNav, type WalletNavItem } from './BottomNav.tsx';
import { ProfileSheet } from './ProfileSheet.tsx';
import type { WalletPrimaryAction } from './UserPrimaryActions.tsx';
import '../../wallet-shell.css';

export type { WalletNavItem, WalletPrimaryAction };

interface WalletShellProps {
  children: ReactNode;
  userName?: string;
  activeNav?: WalletNavItem;
  hasNotifications?: boolean;
  onLogout: () => void;
  onAction: (action: WalletPrimaryAction) => void;
  onNavigate: (item: WalletNavItem) => void;
  onOpenProfileDetail?: () => void;
}

// Shell visual del usuario final XPAY — completamente aislado de
// administración/comercio/empresa, autenticación, router y backend.
// Todo llega por props; el único estado local es la visibilidad del
// panel de perfil (una sola instancia, compartida por el botón del
// hero y el ítem "Perfil" de BottomNav, para no duplicar el panel).
//
// Montado en src/components/Layout.tsx únicamente cuando
// getViewForUser(user) === 'wallet' (UI-2B). admin/comercio/empresa
// siguen usando el <div className="layout"> original, sin cambios.
export function WalletShell({
  children,
  userName,
  activeNav,
  hasNotifications,
  onLogout,
  onAction,
  onNavigate,
  onOpenProfileDetail,
}: WalletShellProps) {
  const [profileOpen, setProfileOpen] = useState(false);

  function handleNavigate(item: WalletNavItem) {
    if (item === 'profile') setProfileOpen(true);
    onNavigate(item);
  }

  return (
    <div className="wallet-shell-v2">
      <WalletHero
        hasNotifications={hasNotifications}
        onOpenProfile={() => setProfileOpen(true)}
        onAction={onAction}
      />

      <div className="wallet-shell-content">
        {children}
      </div>

      <BottomNav activeItem={activeNav} onNavigate={handleNavigate} />

      {profileOpen && (
        <ProfileSheet
          userName={userName}
          onLogout={onLogout}
          onClose={() => setProfileOpen(false)}
          onOpenProfileDetail={onOpenProfileDetail}
        />
      )}
    </div>
  );
}
