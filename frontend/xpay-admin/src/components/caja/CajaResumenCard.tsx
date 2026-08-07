import type { CajaDto } from '../../api/caja.ts';
import { CajaStatusBadge } from './CajaStatusBadge.tsx';
import { fmtMoney, fmtFechaOperativa } from '../../utils/caja-format.ts';

interface CajaResumenCardProps {
  caja:              CajaDto | null;
  loading:           boolean;
  onAbrir:           () => void;
  onCorregirFondo:   () => void;
  onIrARecargar:     () => void;
  onIniciarCuadre:   () => void;
  onCerrar:          () => void;
  onRefrescar:       () => void;
  refrescando:       boolean;
}

export function CajaResumenCard({
  caja, loading, onAbrir, onCorregirFondo, onIrARecargar, onIniciarCuadre, onCerrar, onRefrescar, refrescando,
}: CajaResumenCardProps) {
  if (loading) {
    return (
      <div className="caja-card caja-skeleton-card">
        <div className="caja-skeleton caja-skeleton-line" style={{ width: '40%' }} />
        <div className="caja-skeleton caja-skeleton-line" style={{ width: '70%' }} />
        <div className="caja-skeleton caja-skeleton-line" style={{ width: '55%' }} />
      </div>
    );
  }

  if (!caja) {
    return (
      <div className="caja-empty-state">
        <div className="caja-empty-state-icon">🗄️</div>
        <p className="caja-empty-state-text">No tienes una caja abierta hoy.</p>
        <button type="button" className="caja-btn caja-btn-primary" onClick={onAbrir}>
          Abrir caja
        </button>
      </div>
    );
  }

  const esAbierta   = caja.estado === 'ABIERTA';
  const esEnCuadre  = caja.estado === 'EN_CUADRE';

  return (
    <div className="caja-card">
      <div className="caja-card-header">
        <CajaStatusBadge estado={caja.estado} />
        <button type="button" className="caja-btn-link" onClick={onRefrescar} disabled={refrescando}>
          {refrescando ? 'Actualizando...' : 'Actualizar'}
        </button>
      </div>

      <div className="caja-field-grid">
        <div className="caja-field">
          <span className="caja-field-label">Fecha operativa</span>
          <span className="caja-field-value">{fmtFechaOperativa(caja.fechaOperativa)}</span>
        </div>
        {caja.nombreEstablecimiento && (
          <div className="caja-field">
            <span className="caja-field-label">Sede</span>
            <span className="caja-field-value">{caja.nombreEstablecimiento}</span>
          </div>
        )}
        <div className="caja-field">
          <span className="caja-field-label">Fondo inicial</span>
          <span className="caja-field-value caja-money">{fmtMoney(caja.fondoInicial)}</span>
        </div>

        {(esEnCuadre || !esAbierta) && caja.efectivoEsperado != null && (
          <div className="caja-field">
            <span className="caja-field-label">Efectivo esperado</span>
            <span className="caja-field-value caja-money">{fmtMoney(caja.efectivoEsperado)}</span>
          </div>
        )}
        {!esAbierta && !esEnCuadre && caja.efectivoContado != null && (
          <div className="caja-field">
            <span className="caja-field-label">Efectivo contado</span>
            <span className="caja-field-value caja-money">{fmtMoney(caja.efectivoContado)}</span>
          </div>
        )}
        {!esAbierta && !esEnCuadre && caja.diferencia != null && (
          <div className="caja-field">
            <span className="caja-field-label">Diferencia</span>
            <span className={`caja-field-value caja-money ${caja.diferencia === 0 ? '' : caja.diferencia > 0 ? 'caja-diferencia-positiva' : 'caja-diferencia-negativa'}`}>
              {fmtMoney(caja.diferencia)}
            </span>
          </div>
        )}
        {caja.observacionesCajero && (
          <div className="caja-field">
            <span className="caja-field-label">Observaciones</span>
            <span className="caja-field-value" style={{ fontWeight: 400, fontSize: '0.9rem' }}>{caja.observacionesCajero}</span>
          </div>
        )}
      </div>

      <div className="caja-acciones">
        {esAbierta && (
          <>
            {caja.acciones.puedeCorregirFondoInicial && (
              <button type="button" className="caja-btn caja-btn-secondary" onClick={onCorregirFondo}>
                Corregir fondo inicial
              </button>
            )}
            <button type="button" className="caja-btn caja-btn-secondary" onClick={onIrARecargar}>
              Registrar recarga en efectivo
            </button>
            {caja.acciones.puedeIniciarCuadre && (
              <button type="button" className="caja-btn caja-btn-primary" onClick={onIniciarCuadre}>
                Iniciar cuadre
              </button>
            )}
          </>
        )}
        {esEnCuadre && caja.acciones.puedeCerrar && (
          <button type="button" className="caja-btn caja-btn-confirm" onClick={onCerrar}>
            Cerrar caja
          </button>
        )}
      </div>
    </div>
  );
}
