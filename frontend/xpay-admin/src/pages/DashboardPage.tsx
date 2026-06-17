import { useEffect, useState } from 'react';
import { get } from '../api/client.ts';
import { fmtMoney, fmtNum } from '../utils.ts';

interface ResumenGeneral {
  wallets:  { total: number; saldoUsuarios: number; saldoComercios: number };
  ventasQr: { total: number; contingencia: number; liquidadas: number };
  retiros:  { pendientes: number; pagados: number; rechazados: number };
  ledger:   { transacciones: number };
  auditoria:{ eventos: number };
}

interface ApiResp {
  success: boolean;
  data: ResumenGeneral;
}

function Card({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="card">
      <div className="card-label">{label}</div>
      <div className="card-value">{value}</div>
    </div>
  );
}

export function DashboardPage() {
  const [data, setData] = useState<ResumenGeneral | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    get<ApiResp>('/api/reportes/operaciones/resumen-general')
      .then(r => setData(r.data))
      .catch(err => setError((err as Error).message));
  }, []);

  if (error) return <div className="error-msg">Error: {error}</div>;
  if (!data)  return <div className="loading">Cargando...</div>;

  return (
    <div className="page">
      <h2>Dashboard</h2>
      <div className="cards">
        <Card label="Total Wallets"      value={fmtNum(data.wallets.total)} />
        <Card label="Saldo Usuarios"     value={fmtMoney(data.wallets.saldoUsuarios)} />
        <Card label="Saldo Comercios"    value={fmtMoney(data.wallets.saldoComercios)} />
        <Card label="Ventas QR"          value={fmtNum(data.ventasQr.total)} />
        <Card label="QR Liquidadas"      value={fmtNum(data.ventasQr.liquidadas)} />
        <Card label="QR Contingencia"    value={fmtNum(data.ventasQr.contingencia)} />
        <Card label="Retiros Pagados"    value={fmtNum(data.retiros.pagados)} />
        <Card label="Retiros Pendientes" value={fmtNum(data.retiros.pendientes)} />
        <Card label="Retiros Rechazados" value={fmtNum(data.retiros.rechazados)} />
        <Card label="Txs Ledger"         value={fmtNum(data.ledger.transacciones)} />
        <Card label="Auditoría Eventos"  value={fmtNum(data.auditoria.eventos)} />
      </div>
    </div>
  );
}
