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
function IconBank(p: IconProps) {
  return <Svg {...p}><path d="M3.5 9.5 12 4l8.5 5.5" /><path d="M4.5 9.5v9.5h15V9.5" /><path d="M8 19v-6M12 19v-6M16 19v-6" /><path d="M3.5 19h17" /></Svg>;
}

export type WalletPrimaryAction =
  | 'receive' | 'send' | 'pay-qr' | 'breb-key' | 'where-to-buy' | 'withdraw-bank'
  // XPAY-375 — retiro Bre-B REAL (dinero real), deliberadamente distinto de
  // 'breb-key' (envío a llave de terceros, sin ruta/backend real todavía) y
  // de 'withdraw-bank' (retiro simulado, tab 'banco').
  | 'withdraw-breb-real';

interface ActionDef {
  key: WalletPrimaryAction;
  label: string;
  icon: ComponentType<IconProps>;
}

// XPAY-390 FASE 1 — franja verde reducida a las 4 acciones principales
// (XPAY-389/390): 'breb-key' (envío a llave de terceros, sin ruta/backend
// real todavía), 'where-to-buy' y 'withdraw-bank' (retiro simulado a banco)
// quedan FUERA de la franja principal por decisión de producto — su
// lógica/rutas/tabs en Layout.tsx y UserWalletPage.tsx NO se eliminan, solo
// dejan de ser alcanzables desde este menú. 'pay-qr' se renombra a "Comprar
// con QR" (mismo endpoint/handler, solo texto).
const ACTIONS: ActionDef[] = [
  { key: 'receive', label: 'Recibir', icon: IconReceive },
  { key: 'send', label: 'Enviar', icon: IconTransfer },
  { key: 'pay-qr', label: 'Comprar con QR', icon: IconQr },
  // XPAY-375 — retiro Bre-B REAL; único botón de retiro visible aquí.
  { key: 'withdraw-breb-real', label: 'Retirar a mi llave Bre-B', icon: IconBank },
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
