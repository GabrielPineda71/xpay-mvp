import type { ReactNode } from 'react';

interface ConfirmDialogProps {
  title:            string;
  children?:        ReactNode;
  confirmLabel:     string;
  confirmClassName?: string;
  busy?:            boolean;
  disabled?:        boolean;
  error?:           string | null;
  onConfirm:        () => void;
  onCancel:         () => void;
}

// Diálogo genérico de confirmación explícita — usado para iniciar cuadre y
// como base visual de los diálogos con formulario (Abrir/Corregir/Cerrar/Revisar).
export function ConfirmDialog({
  title, children, confirmLabel, confirmClassName = 'caja-btn-confirm',
  busy, disabled, error, onConfirm, onCancel,
}: ConfirmDialogProps) {
  return (
    <div className="caja-dialog-overlay" role="dialog" aria-modal="true" aria-label={title}>
      <div className="caja-dialog-box">
        <h3>{title}</h3>
        {children}
        {error && <div className="caja-error-banner">{error}</div>}
        <div className="caja-dialog-actions">
          <button type="button" className="caja-btn caja-btn-secondary" onClick={onCancel} disabled={busy}>
            Cancelar
          </button>
          <button
            type="button"
            className={`caja-btn ${confirmClassName}`}
            onClick={onConfirm}
            disabled={busy || disabled}
          >
            {busy ? 'Procesando...' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
