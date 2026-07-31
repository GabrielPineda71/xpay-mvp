import { BrandMark } from '../BrandMark.tsx';
import { UserPrimaryActions, type WalletPrimaryAction } from './UserPrimaryActions.tsx';

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
function IconBell(p: IconProps) {
  return <Svg {...p}><path d="M6 10a6 6 0 1 1 12 0c0 4 1.5 5.5 1.5 5.5h-15S6 14 6 10Z" /><path d="M10 19a2 2 0 0 0 4 0" /></Svg>;
}
function IconUser(p: IconProps) {
  return <Svg {...p}><circle cx="12" cy="8.5" r="3.5" /><path d="M4.5 20c1-3.8 4-5.8 7.5-5.8s6.5 2 7.5 5.8" /></Svg>;
}

interface WalletHeroProps {
  hasNotifications?: boolean;
  onOpenProfile: () => void;
  onAction: (action: WalletPrimaryAction) => void;
}

// Franja verde: marca + notificaciones + perfil + acciones principales.
// El panel de perfil (ProfileSheet) NO vive aquí — lo monta WalletShell,
// una sola instancia, para que este botón y el de BottomNav abran
// siempre el mismo panel sin duplicarlo.
export function WalletHero({ hasNotifications, onOpenProfile, onAction }: WalletHeroProps) {
  return (
    <div className="wallet-hero">
      <div className="wh-topbar">
        <BrandMark variant="logo" theme="white" className="wallet-hero-logo" />
        <div className="wh-actions-icons">
          <button className="wh-icon-btn" type="button" aria-label="Notificaciones">
            <IconBell size={18} />
            {hasNotifications && <span className="wh-notif-dot" aria-hidden="true" />}
          </button>
          <button className="wh-profile-btn" type="button" onClick={onOpenProfile} aria-label="Abrir perfil">
            <IconUser size={16} />
          </button>
        </div>
      </div>

      <UserPrimaryActions onAction={onAction} />
    </div>
  );
}
