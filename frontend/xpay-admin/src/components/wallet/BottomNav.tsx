import type { ComponentType } from 'react';

interface IconProps {
  size?: number;
}
function Svg({ size = 19, children }: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      width={size} height={size} viewBox="0 0 24 24" aria-hidden="true"
      fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round"
    >
      {children}
    </svg>
  );
}
function IconHome(p: IconProps) {
  return <Svg {...p}><path d="M3 11.5 12 4l9 7.5" /><path d="M5.5 10v9.5h13V10" /><path d="M9.5 19.5v-6h5v6" /></Svg>;
}
function IconHistory(p: IconProps) {
  return <Svg {...p}><path d="M3.5 12a8.5 8.5 0 1 0 2.6-6.1" /><path d="M3.5 4.5v4.2h4.2" /><path d="M12 8v4.5l3 2" /></Svg>;
}
function IconQr(p: IconProps) {
  return <Svg {...p}><rect x="3.5" y="3.5" width="6" height="6" rx="1" /><rect x="14.5" y="3.5" width="6" height="6" rx="1" /><rect x="3.5" y="14.5" width="6" height="6" rx="1" /><path d="M14.5 15h2.5v2.5" /><path d="M20.5 20.5h-3" /><path d="M17.5 20.5v-3" /></Svg>;
}
function IconGrid(p: IconProps) {
  return <Svg {...p}><rect x="3.5" y="3.5" width="7.5" height="7.5" rx="1.2" /><rect x="13" y="3.5" width="7.5" height="7.5" rx="1.2" /><rect x="3.5" y="13" width="7.5" height="7.5" rx="1.2" /><rect x="13" y="13" width="7.5" height="7.5" rx="1.2" /></Svg>;
}
function IconUser(p: IconProps) {
  return <Svg {...p}><circle cx="12" cy="8.5" r="3.5" /><path d="M4.5 20c1-3.8 4-5.8 7.5-5.8s6.5 2 7.5 5.8" /></Svg>;
}

export type WalletNavItem = 'home' | 'movements' | 'qr' | 'products' | 'profile';

interface NavDef {
  key: WalletNavItem;
  label: string;
  icon: ComponentType<IconProps>;
  elevated?: boolean;
}

const NAV_ITEMS: NavDef[] = [
  { key: 'home', label: 'Inicio', icon: IconHome },
  { key: 'movements', label: 'Movimientos', icon: IconHistory },
  { key: 'qr', label: 'QR', icon: IconQr, elevated: true },
  { key: 'products', label: 'Productos', icon: IconGrid },
  { key: 'profile', label: 'Perfil', icon: IconUser },
];

interface BottomNavProps {
  activeItem?: WalletNavItem;
  onNavigate: (item: WalletNavItem) => void;
}

// Navegación inferior — presentación pura, sin router (ni NavLink ni
// useNavigate): cada botón solo notifica la intención vía
// onNavigate(key). La integración con rutas reales llega en UI-2B.
export function BottomNav({ activeItem, onNavigate }: BottomNavProps) {
  return (
    <nav className="wallet-bottom-nav" aria-label="Navegación principal">
      {NAV_ITEMS.map(item => {
        const Icon = item.icon;
        const active = activeItem === item.key;
        return (
          <button
            key={item.key}
            type="button"
            className={`wallet-bn-item${item.elevated ? ' wallet-bn-item--elevated' : ''}${active ? ' wallet-bn-item--active' : ''}`}
            onClick={() => onNavigate(item.key)}
            aria-label={item.label}
            aria-current={active ? 'page' : undefined}
          >
            <span className="wallet-bn-icon"><Icon size={item.elevated ? 22 : 19} /></span>
            <span className="wallet-bn-label">{item.label}</span>
          </button>
        );
      })}
    </nav>
  );
}
