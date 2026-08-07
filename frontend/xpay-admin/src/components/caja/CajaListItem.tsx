import type { CajaResumenDto } from '../../api/caja.ts';
import { CajaStatusBadge } from './CajaStatusBadge.tsx';
import { fmtMoney, fmtFechaOperativa } from '../../utils/caja-format.ts';

export function CajaListItem({ caja, onClick }: { caja: CajaResumenDto; onClick: () => void }) {
  return (
    <button type="button" className="caja-list-item" onClick={onClick}>
      <div className="caja-list-item-row">
        <span className="caja-list-item-value">{caja.nombreUsuarioCajero ?? `Usuario #${caja.idUsuarioCajero}`}</span>
        <CajaStatusBadge estado={caja.estado} />
      </div>
      <div className="caja-list-item-row">
        <span className="caja-list-item-label">{fmtFechaOperativa(caja.fechaOperativa)}</span>
        {caja.nombreEstablecimiento && <span className="caja-list-item-label">{caja.nombreEstablecimiento}</span>}
      </div>
      <div className="caja-list-item-row">
        <span className="caja-list-item-label">Fondo: <strong className="caja-list-item-value">{fmtMoney(caja.fondoInicial)}</strong></span>
        {caja.diferencia != null && (
          <span className="caja-list-item-label">
            Diferencia: <strong className="caja-list-item-value">{fmtMoney(caja.diferencia)}</strong>
          </span>
        )}
      </div>
    </button>
  );
}
