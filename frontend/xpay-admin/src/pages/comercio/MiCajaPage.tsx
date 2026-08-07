import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useComercioScope } from '../../auth/useComercioScope.ts';
import {
  getMiCajaActual, abrirCaja, corregirFondoInicial, iniciarCuadre, cerrarCaja,
  type CajaDto,
} from '../../api/caja.ts';
import { getApiErrorMessage } from '../../utils/caja-format.ts';
import { CajaResumenCard } from '../../components/caja/CajaResumenCard.tsx';
import { AbrirCajaDialog } from '../../components/caja/AbrirCajaDialog.tsx';
import { CorregirFondoDialog } from '../../components/caja/CorregirFondoDialog.tsx';
import { ConfirmDialog } from '../../components/caja/ConfirmDialog.tsx';
import { CerrarCajaDialog } from '../../components/caja/CerrarCajaDialog.tsx';

type DialogoAbierto = null | 'abrir' | 'corregir' | 'cuadre' | 'cerrar';

// /comercio/mi-caja — CAJERO y ADMIN_SEDE_COMERCIO. Cubre todo el ciclo de
// vida operativo de su propia caja (sin caja → ABIERTA → EN_CUADRE →
// terminal), integrando el flujo existente de recarga de Wallet en /mi-comercio.
export function MiCajaPage() {
  const navigate = useNavigate();
  const { scope } = useComercioScope();

  const [caja, setCaja]         = useState<CajaDto | null>(null);
  const [loading, setLoading]   = useState(true);
  const [refrescando, setRefrescando] = useState(false);
  const [pageError, setPageError]     = useState<string | null>(null);

  const [dialogo, setDialogo]   = useState<DialogoAbierto>(null);
  const [dialogBusy, setDialogBusy]   = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);

  const cargar = useCallback(async (mostrarSkeleton: boolean) => {
    if (mostrarSkeleton) setLoading(true); else setRefrescando(true);
    setPageError(null);
    try {
      const c = await getMiCajaActual();
      setCaja(c);
    } catch (err) {
      setPageError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
      setRefrescando(false);
    }
  }, []);

  useEffect(() => { void cargar(true); }, [cargar]);

  function cerrarDialogo() {
    setDialogo(null);
    setDialogError(null);
  }

  async function handleAbrir(fondoInicial: number) {
    setDialogBusy(true); setDialogError(null);
    try {
      const c = await abrirCaja(fondoInicial);
      setCaja(c);
      cerrarDialogo();
    } catch (err) {
      setDialogError(getApiErrorMessage(err));
    } finally {
      setDialogBusy(false);
    }
  }

  async function handleCorregirFondo(fondoInicial: number, motivo: string) {
    if (!caja) return;
    setDialogBusy(true); setDialogError(null);
    try {
      const c = await corregirFondoInicial(caja.idCaja, fondoInicial, motivo);
      setCaja(c);
      cerrarDialogo();
    } catch (err) {
      setDialogError(getApiErrorMessage(err));
    } finally {
      setDialogBusy(false);
    }
  }

  async function handleIniciarCuadre() {
    if (!caja) return;
    setDialogBusy(true); setDialogError(null);
    try {
      const c = await iniciarCuadre(caja.idCaja);
      setCaja(c);
      cerrarDialogo();
    } catch (err) {
      setDialogError(getApiErrorMessage(err));
    } finally {
      setDialogBusy(false);
    }
  }

  async function handleCerrar(efectivoContado: number, observaciones?: string) {
    if (!caja) return;
    setDialogBusy(true); setDialogError(null);
    try {
      const c = await cerrarCaja(caja.idCaja, efectivoContado, observaciones);
      setCaja(c);
      cerrarDialogo();
    } catch (err) {
      setDialogError(getApiErrorMessage(err));
    } finally {
      setDialogBusy(false);
    }
  }

  return (
    <div className="caja-page">
      <h1 className="caja-page-title">Mi Caja</h1>
      <p className="caja-page-subtitle">
        {scope?.rolComercio === 'ADMIN_SEDE_COMERCIO' ? 'Tu turno de caja como administrador de sede.' : 'Tu turno de caja de hoy.'}
      </p>

      {pageError && <div className="caja-error-banner">{pageError}</div>}

      <CajaResumenCard
        caja={caja}
        loading={loading}
        onAbrir={() => setDialogo('abrir')}
        onCorregirFondo={() => setDialogo('corregir')}
        onIrARecargar={() => navigate('/mi-comercio')}
        onIniciarCuadre={() => setDialogo('cuadre')}
        onCerrar={() => setDialogo('cerrar')}
        onRefrescar={() => void cargar(false)}
        refrescando={refrescando}
      />

      {dialogo === 'abrir' && (
        <AbrirCajaDialog
          nombreEstablecimiento={caja?.nombreEstablecimiento}
          busy={dialogBusy}
          error={dialogError}
          onConfirm={f => void handleAbrir(f)}
          onCancel={cerrarDialogo}
        />
      )}

      {dialogo === 'corregir' && caja && (
        <CorregirFondoDialog
          fondoActual={caja.fondoInicial}
          busy={dialogBusy}
          error={dialogError}
          onConfirm={(f, m) => void handleCorregirFondo(f, m)}
          onCancel={cerrarDialogo}
        />
      )}

      {dialogo === 'cuadre' && (
        <ConfirmDialog
          title="Iniciar cuadre"
          confirmLabel="Iniciar cuadre"
          confirmClassName="caja-btn-primary"
          busy={dialogBusy}
          error={dialogError}
          onConfirm={() => void handleIniciarCuadre()}
          onCancel={cerrarDialogo}
        >
          <p className="caja-form-hint">
            A partir de este momento no podrás registrar más recargas en efectivo en esta caja.
            ¿Confirmas iniciar el cuadre?
          </p>
        </ConfirmDialog>
      )}

      {dialogo === 'cerrar' && caja && (
        <CerrarCajaDialog
          efectivoEsperado={caja.efectivoEsperado}
          busy={dialogBusy}
          error={dialogError}
          onConfirm={(v, o) => void handleCerrar(v, o)}
          onCancel={cerrarDialogo}
        />
      )}
    </div>
  );
}
