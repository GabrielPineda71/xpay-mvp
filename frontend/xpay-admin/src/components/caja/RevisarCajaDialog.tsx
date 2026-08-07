import { useState } from 'react';
import { ConfirmDialog } from './ConfirmDialog.tsx';

interface RevisarCajaDialogProps {
  busy?:     boolean;
  error?:    string | null;
  onConfirm: (observaciones: string) => void;
  onCancel:  () => void;
}

// Observaciones es obligatorio — WalletCajaComercioService.RevisarAsync
// rechaza con 400 si llega vacío/solo espacios (a pesar de que el DTO lo
// declare nullable). Se valida aquí para dar feedback inmediato, pero el
// backend sigue siendo quien decide en última instancia.
export function RevisarCajaDialog({ busy, error, onConfirm, onCancel }: RevisarCajaDialogProps) {
  const [observaciones, setObservaciones] = useState('');
  const esValido = observaciones.trim().length > 0;

  return (
    <ConfirmDialog
      title="Marcar caja como revisada"
      confirmLabel="Confirmar revisión"
      confirmClassName="caja-btn-confirm"
      busy={busy}
      error={error}
      disabled={!esValido}
      onConfirm={() => onConfirm(observaciones.trim())}
      onCancel={onCancel}
    >
      <div className="caja-form-field">
        <label htmlFor="caja-observaciones-revision">Observaciones (obligatorio)</label>
        <textarea
          id="caja-observaciones-revision"
          rows={3}
          value={observaciones}
          onChange={e => setObservaciones(e.target.value)}
          placeholder="Notas de la revisión administrativa"
          autoFocus
        />
      </div>
    </ConfirmDialog>
  );
}
