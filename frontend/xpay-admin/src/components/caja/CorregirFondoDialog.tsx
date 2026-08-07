import { useState } from 'react';
import { ConfirmDialog } from './ConfirmDialog.tsx';

interface CorregirFondoDialogProps {
  fondoActual: number;
  busy?:       boolean;
  error?:      string | null;
  onConfirm:   (fondoInicial: number, motivo: string) => void;
  onCancel:    () => void;
}

export function CorregirFondoDialog({ fondoActual, busy, error, onConfirm, onCancel }: CorregirFondoDialogProps) {
  const [fondo, setFondo]   = useState(String(fondoActual));
  const [motivo, setMotivo] = useState('');
  const valor     = Number(fondo);
  const esValido  = fondo.trim() !== '' && Number.isFinite(valor) && valor >= 0 && motivo.trim().length > 0;

  return (
    <ConfirmDialog
      title="Corregir fondo inicial"
      confirmLabel="Guardar corrección"
      confirmClassName="caja-btn-primary"
      busy={busy}
      error={error}
      disabled={!esValido}
      onConfirm={() => onConfirm(valor, motivo.trim())}
      onCancel={onCancel}
    >
      <div className="caja-form-field">
        <label htmlFor="caja-fondo-corregido">Nuevo fondo inicial</label>
        <input
          id="caja-fondo-corregido"
          type="text"
          inputMode="decimal"
          value={fondo}
          onChange={e => setFondo(e.target.value.replace(/[^0-9.]/g, ''))}
          autoFocus
        />
      </div>
      <div className="caja-form-field">
        <label htmlFor="caja-motivo-correccion">Motivo (obligatorio)</label>
        <textarea
          id="caja-motivo-correccion"
          rows={3}
          value={motivo}
          onChange={e => setMotivo(e.target.value)}
          placeholder="Ej: se contó mal el fondo inicial al abrir"
        />
        <span className="caja-form-hint">Solo puedes corregir antes de tu primera recarga.</span>
      </div>
    </ConfirmDialog>
  );
}
