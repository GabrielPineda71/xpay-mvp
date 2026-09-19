interface HeroBalanceCardProps {
  titulo:          string;
  saldoFormateado: string;
  estado:          string;
}

// Tarjeta de saldo real — reemplaza las 3 tarjetas inline (saldo/wallet/
// estado) del tab "saldo" de UserWalletPage.tsx. Solo presentación: recibe
// datos ya cargados por esa página (mismo estado `cuenta`, sin llamada API
// propia). Sin prop de tendencia — no existe dato real de tendencia en
// EstadoCuenta, y este componente no debe inventar uno.
//
// XPAY-415 — `titulo` (antes `nombreWallet`) deja de recibir el nombre
// técnico interno de la wallet (ej. "WALLET QA USUARIO UNO", valor crudo de
// EstadoCuenta.nombreWallet) y pasa a recibir el nombre de usuario, apropiado
// para cliente final — cambio puramente de presentación, sin tocar el
// contrato de EstadoCuenta ni ningún endpoint.
export function HeroBalanceCard({ titulo, saldoFormateado, estado }: HeroBalanceCardProps) {
  const activa = estado === 'ACTIVA';
  return (
    <div className="wallet-balance-card">
      <div className="wallet-balance-top">
        <span className="wallet-balance-label">{titulo}</span>
        <span className={`wallet-balance-badge${activa ? '' : ' wallet-balance-badge--alert'}`}>{estado}</span>
      </div>
      <div className="wallet-balance-value">{saldoFormateado}</div>
      <div className="wallet-balance-sublabel">Disponible</div>
    </div>
  );
}
