interface HeroBalanceCardProps {
  nombreWallet:    string;
  saldoFormateado: string;
  estado:          string;
}

// Tarjeta de saldo real — reemplaza las 3 tarjetas inline (saldo/wallet/
// estado) del tab "saldo" de UserWalletPage.tsx. Solo presentación: recibe
// datos ya cargados por esa página (mismo estado `cuenta`, sin llamada API
// propia). Sin prop de tendencia — no existe dato real de tendencia en
// EstadoCuenta, y este componente no debe inventar uno.
export function HeroBalanceCard({ nombreWallet, saldoFormateado, estado }: HeroBalanceCardProps) {
  const activa = estado === 'ACTIVA';
  return (
    <div className="wallet-balance-card">
      <div className="wallet-balance-top">
        <span className="wallet-balance-label">{nombreWallet}</span>
        <span className={`wallet-balance-badge${activa ? '' : ' wallet-balance-badge--alert'}`}>{estado}</span>
      </div>
      <div className="wallet-balance-value">{saldoFormateado}</div>
      <div className="wallet-balance-sublabel">Disponible</div>
    </div>
  );
}
