import type { ComponentType } from 'react';

// Iconos propios (SVG, sin librería externa) — mismo patrón visual ya
// aprobado, redefinidos localmente porque este directorio no puede
// importar desde src/prototype/**.
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
function IconReceive(p: IconProps) {
  return <Svg {...p}><circle cx="12" cy="12" r="9" /><path d="M12 7.5v9" /><path d="M8.5 12.5 12 16l3.5-3.5" /></Svg>;
}
function IconTransfer(p: IconProps) {
  return <Svg {...p}><path d="M4 8h13" /><path d="M13 4l4 4-4 4" /><path d="M20 16H7" /><path d="M11 12l-4 4 4 4" /></Svg>;
}
function IconQr(p: IconProps) {
  return <Svg {...p}><rect x="3.5" y="3.5" width="6" height="6" rx="1" /><rect x="14.5" y="3.5" width="6" height="6" rx="1" /><rect x="3.5" y="14.5" width="6" height="6" rx="1" /><path d="M14.5 15h2.5v2.5" /><path d="M20.5 20.5h-3" /><path d="M17.5 20.5v-3" /></Svg>;
}
function IconKeySend(p: IconProps) {
  return <Svg {...p}><circle cx="7.5" cy="8.5" r="3" /><path d="M9.6 10.6 18.5 19.5" /><path d="M15.5 19.5h3v-3" /></Svg>;
}
function IconStore(p: IconProps) {
  return <Svg {...p}><path d="M3.5 9 5 4h14l1.5 5" /><path d="M4 9v10.5h16V9" /><path d="M9.5 19.5V14h5v5.5" /><path d="M3.5 9c0 1.4 1.1 2.5 2.5 2.5S8.5 10.4 8.5 9M8.5 9c0 1.4 1.1 2.5 2.5 2.5S13.5 10.4 13.5 9M13.5 9c0 1.4 1.1 2.5 2.5 2.5S18.5 10.4 18.5 9" /></Svg>;
}

export type WalletPrimaryAction = 'receive' | 'send' | 'pay-qr' | 'breb-key' | 'where-to-buy';

interface ActionDef {
  key: WalletPrimaryAction;
  label: string;
  icon: ComponentType<IconProps>;
}

const ACTIONS: ActionDef[] = [
  { key: 'receive', label: 'Recibir', icon: IconReceive },
  { key: 'send', label: 'Enviar', icon: IconTransfer },
  { key: 'pay-qr', label: 'Pagar QR', icon: IconQr },
  { key: 'breb-key', label: 'Enviar a mi llave Bre-B', icon: IconKeySend },
  { key: 'where-to-buy', label: 'Dónde comprar', icon: IconStore },
];

interface UserPrimaryActionsProps {
  onAction: (action: WalletPrimaryAction) => void;
}

// Acciones principales del hero — presentación pura. No navega ni ejecuta
// lógica real: cada botón solo notifica la intención vía onAction(key).
// La conexión real (rutas/handlers de UserWalletPage.tsx) llega en una
// fase posterior.
export function UserPrimaryActions({ onAction }: UserPrimaryActionsProps) {
  return (
    <div className="wallet-actions-row" role="group" aria-label="Acciones principales">
      {ACTIONS.map(a => {
        const Icon = a.icon;
        return (
          <button
            key={a.key}
            type="button"
            className="wallet-action-btn"
            onClick={() => onAction(a.key)}
            aria-label={a.label}
          >
            <span className="wallet-action-circle"><Icon size={20} /></span>
            <span className="wallet-action-label">{a.label}</span>
          </button>
        );
      })}
    </div>
  );
}
