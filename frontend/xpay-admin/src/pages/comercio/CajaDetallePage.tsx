import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useComercioScope } from '../../auth/useComercioScope.ts';
import { listarCajas, revisarCaja, type CajaResumenDto } from '../../api/caja.ts';
import { getApiErrorMessage, fmtMoney, fmtFechaOperativa } from '../../utils/caja-format.ts';
import { CajaStatusBadge } from '../../components/caja/CajaStatusBadge.tsx';
import { RevisarCajaDialog } from '../../components/caja/RevisarCajaDialog.tsx';

// /comercio/cajas/:id — no existe un GET individual en el backend
// (WalletCajaComercioController solo expone mi-caja-actual, abrir,
// fondo-inicial, iniciar-cuadre, cerrar, revisar y el listado paginado) —
// el detalle se deriva del listado ya auto-acotado por rol/sede en el
// servidor. Ver "gaps reales" en el informe de cierre de esta fase.
const ESTADOS_REVISABLES = ['CON_DIFERENCIA', 'CERRADA_AUTOMATICAMENTE'];

export function CajaDetallePage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { scope } = useComercioScope();
  const idCaja = Number(id);

  const [caja, setCaja]       = useState<CajaResumenDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [error, setError]     = useState<string | null>(null);

  const [mostrarRevisar, setMostrarRevisar] = useState(false);
  const [revisarBusy, setRevisarBusy]       = useState(false);
  const [revisarError, setRevisarError]     = useState<string | null>(null);

  const cargar = useCallback(async () => {
    setLoading(true); setError(null); setNotFound(false);
    try {
      const r = await listarCajas({ page: 1, pageSize: 100 });
      const encontrada = r.items.find(c => c.idCaja === idCaja);
      if (!encontrada) setNotFound(true);
      setCaja(encontrada ?? null);
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, [idCaja]);

  useEffect(() => { void cargar(); }, [cargar]);

  async function handleRevisar(observaciones: string) {
    setRevisarBusy(true); setRevisarError(null);
    try {
      await revisarCaja(idCaja, observaciones);
      setMostrarRevisar(false);
      await cargar();
    } catch (err) {
      setRevisarError(getApiErrorMessage(err));
    } finally {
      setRevisarBusy(false);
    }
  }

  const puedeRevisar =
    !!caja && !!scope &&
    ['ADMIN_SEDE_COMERCIO', 'ADMIN_COMERCIO'].includes(scope.rolComercio) &&
    ESTADOS_REVISABLES.includes(caja.estado) &&
    caja.idUsuarioCajero !== scope.idUsuario;

  return (
    <div className="caja-page">
      <button type="button" className="caja-btn-link" style={{ marginBottom: '0.5rem', paddingLeft: 0 }} onClick={() => navigate(-1)}>
        ← Volver
      </button>
      <h1 className="caja-page-title">Detalle de caja #{id}</h1>

      {error && <div className="caja-error-banner">{error}</div>}

      {loading ? (
        <div className="caja-card caja-skeleton-card" />
      ) : notFound ? (
        <div className="caja-page-empty">No se encontró esa caja dentro de tu alcance operativo.</div>
      ) : caja ? (
        <div className="caja-card">
          <div className="caja-card-header">
            <CajaStatusBadge estado={caja.estado} />
          </div>
          <div className="caja-field-grid">
            <div className="caja-field">
              <span className="caja-field-label">Cajero</span>
              <span className="caja-field-value">{caja.nombreUsuarioCajero ?? `#${caja.idUsuarioCajero}`}</span>
            </div>
            {caja.nombreEstablecimiento && (
              <div className="caja-field">
                <span className="caja-field-label">Sede</span>
                <span className="caja-field-value">{caja.nombreEstablecimiento}</span>
              </div>
            )}
            <div className="caja-field">
              <span className="caja-field-label">Fecha operativa</span>
              <span className="caja-field-value">{fmtFechaOperativa(caja.fechaOperativa)}</span>
            </div>
            <div className="caja-field">
              <span className="caja-field-label">Fondo inicial</span>
              <span className="caja-field-value caja-money">{fmtMoney(caja.fondoInicial)}</span>
            </div>
            {caja.efectivoEsperado != null && (
              <div className="caja-field">
                <span className="caja-field-label">Efectivo esperado</span>
                <span className="caja-field-value caja-money">{fmtMoney(caja.efectivoEsperado)}</span>
              </div>
            )}
            {caja.efectivoContado != null && (
              <div className="caja-field">
                <span className="caja-field-label">Efectivo contado</span>
                <span className="caja-field-value caja-money">{fmtMoney(caja.efectivoContado)}</span>
              </div>
            )}
            {caja.diferencia != null && (
              <div className="caja-field">
                <span className="caja-field-label">Diferencia</span>
                <span className={`caja-field-value caja-money ${caja.diferencia === 0 ? '' : caja.diferencia > 0 ? 'caja-diferencia-positiva' : 'caja-diferencia-negativa'}`}>
                  {fmtMoney(caja.diferencia)}
                </span>
              </div>
            )}
          </div>

          {puedeRevisar && (
            <div className="caja-acciones">
              <button type="button" className="caja-btn caja-btn-confirm" onClick={() => setMostrarRevisar(true)}>
                Marcar como revisada
              </button>
            </div>
          )}
        </div>
      ) : null}

      {mostrarRevisar && (
        <RevisarCajaDialog
          busy={revisarBusy}
          error={revisarError}
          onConfirm={o => void handleRevisar(o)}
          onCancel={() => { setMostrarRevisar(false); setRevisarError(null); }}
        />
      )}
    </div>
  );
}
