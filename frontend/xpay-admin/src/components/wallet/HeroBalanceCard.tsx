interface HeroBalanceCardProps {
  saldoFormateado: string;
  estado:          string;
}

// Tarjeta de saldo real — reemplaza las 3 tarjetas inline (saldo/wallet/
// estado) del tab "saldo" de UserWalletPage.tsx. Solo presentación: recibe
// datos ya cargados por esa página (mismo estado `cuenta`, sin llamada API
// propia). Sin prop de tendencia — no existe dato real de tendencia en
// EstadoCuenta, y este componente no debe inventar uno.
//
// XPAY-422 A4 — se retira la prop `titulo` (nombre de usuario/wallet técnico,
// introducida en XPAY-415): el usuario ya se identifica FUERA de esta
// tarjeta (UserWalletPage.tsx, "wallet-username-label"), así que mostrarlo
// también aquí era redundante. La tarjeta conserva exclusivamente estado
// (badge ACTIVA/alerta), saldo y "Disponible" — sin tocar cálculo de saldo
// ni el campo `estado` en sí, ambos siguen viniendo intactos del backend.
export function HeroBalanceCard({ saldoFormateado, estado }: HeroBalanceCardProps) {
  const activa = estado === 'ACTIVA';
  return (
    <div className="wallet-balance-card">
      <div className="wallet-balance-top">
        <span className={`wallet-balance-badge${activa ? '' : ' wallet-balance-badge--alert'}`}>{estado}</span>
      </div>
      <div className="wallet-balance-value">{saldoFormateado}</div>
      <div className="wallet-balance-sublabel">Disponible</div>
    </div>
  );
}
