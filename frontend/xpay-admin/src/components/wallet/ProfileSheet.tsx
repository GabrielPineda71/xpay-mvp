import { useEffect } from 'react';

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
function IconUser(p: IconProps) {
  return <Svg {...p}><circle cx="12" cy="8.5" r="3.5" /><path d="M4.5 20c1-3.8 4-5.8 7.5-5.8s6.5 2 7.5 5.8" /></Svg>;
}
function IconClose(p: IconProps) {
  return <Svg {...p}><path d="M6 6l12 12M18 6 6 18" /></Svg>;
}

interface ProfileSheetProps {
  userName?: string;
  onLogout: () => void;
  onClose: () => void;
  onOpenProfileDetail?: () => void;
}

// Panel de perfil — presentación pura, sin useAuth. El nombre llega por
// prop; logout y cierre son callbacks del padre (WalletShell), que a su
// vez los recibe de quien monte el shell en la app real.
export function ProfileSheet({ userName, onLogout, onClose, onOpenProfileDetail }: ProfileSheetProps) {
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
      <div className="wallet-profile-sheet" role="dialog" aria-label="Perfil" aria-modal="true">
        <button className="wallet-profile-close" type="button" onClick={onClose} aria-label="Cerrar perfil">
          <IconClose size={16} />
        </button>

        <div className="wallet-profile-avatar"><IconUser size={26} /></div>
        <div className="wallet-profile-name">{userName || 'Usuario'}</div>

        <button
          type="button"
          className="wallet-profile-action"
          onClick={onOpenProfileDetail}
          disabled={!onOpenProfileDetail}
        >
          Mi perfil
        </button>
        <button type="button" className="wallet-profile-action wallet-profile-logout" onClick={onLogout}>
          Cerrar sesión
        </button>
      </div>
    </>
  );
}
