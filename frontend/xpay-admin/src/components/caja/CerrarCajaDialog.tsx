import { useState } from 'react';
import { ConfirmDialog } from './ConfirmDialog.tsx';
import { fmtMoney } from '../../utils/caja-format.ts';

interface CerrarCajaDialogProps {
  efectivoEsperado: number | null;
  busy?:            boolean;
  error?:           string | null;
  onConfirm:        (efectivoContado: number, observaciones?: string) => void;
  onCancel:         () => void;
}

// La diferencia mostrada aquí es solo un cálculo visual para orientar al
// cajero antes de confirmar — el backend es quien calcula y persiste el
// valor real de `diferencia` al cerrar (no se duplica esa regla).
export function CerrarCajaDialog({ efectivoEsperado, busy, error, onConfirm, onCancel }: CerrarCajaDialogProps) {
  const [contado, setContado]         = useState('');
  const [observaciones, setObservaciones] = useState('');
  const valor    = Number(contado);
  const esValido = contado.trim() !== '' && Number.isFinite(valor) && valor >= 0;
  const diferenciaPreview = esValido && efectivoEsperado != null ? valor - efectivoEsperado : null;

  return (
    <ConfirmDialog
      title="Cerrar caja"
      confirmLabel="Cerrar caja"
      confirmClassName="caja-btn-confirm"
      busy={busy}
      error={error}
      disabled={!esValido}
      onConfirm={() => onConfirm(valor, observaciones.trim() || undefined)}
      onCancel={onCancel}
    >
      {efectivoEsperado != null && (
        <div className="caja-form-preview">
          <div className="caja-form-preview-row"><span>Efectivo esperado</span><strong>{fmtMoney(efectivoEsperado)}</strong></div>
        </div>
      )}
      <div className="caja-form-field">
        <label htmlFor="caja-efectivo-contado">Efectivo contado</label>
        <input
          id="caja-efectivo-contado"
          type="text"
          inputMode="decimal"
          placeholder="0"
          value={contado}
          onChange={e => setContado(e.target.value.replace(/[^0-9.]/g, ''))}
          autoFocus
        />
      </div>
      {diferenciaPreview != null && (
        <div className="caja-form-preview">
          <div className="caja-form-preview-row">
            <span>Diferencia (vista previa)</span>
            <strong style={{ color: diferenciaPreview === 0 ? '#276749' : diferenciaPreview > 0 ? '#276749' : '#c53030' }}>
              {fmtMoney(diferenciaPreview)}
            </strong>
          </div>
        </div>
      )}
      <div className="caja-form-field">
        <label htmlFor="caja-observaciones-cierre">Observaciones {diferenciaPreview !== 0 ? '(requerido si hay diferencia)' : '(opcional)'}</label>
        <textarea
          id="caja-observaciones-cierre"
          rows={2}
          value={observaciones}
          onChange={e => setObservaciones(e.target.value)}
          placeholder="Ej: sobrante por cambio no entregado"
        />
      </div>
    </ConfirmDialog>
  );
}
