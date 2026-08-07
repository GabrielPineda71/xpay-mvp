import { useState } from 'react';
import { ConfirmDialog } from './ConfirmDialog.tsx';

interface AbrirCajaDialogProps {
  nombreEstablecimiento?: string | null;
  busy?:      boolean;
  error?:     string | null;
  onConfirm:  (fondoInicial: number) => void;
  onCancel:   () => void;
}

export function AbrirCajaDialog({ nombreEstablecimiento, busy, error, onConfirm, onCancel }: AbrirCajaDialogProps) {
  const [fondo, setFondo] = useState('');
  const valor = Number(fondo);
  const esValido = fondo.trim() !== '' && Number.isFinite(valor) && valor >= 0;

  return (
    <ConfirmDialog
      title="Abrir caja"
      confirmLabel="Abrir caja"
      confirmClassName="caja-btn-primary"
      busy={busy}
      error={error}
      disabled={!esValido}
      onConfirm={() => onConfirm(valor)}
      onCancel={onCancel}
    >
      {nombreEstablecimiento && (
        <p className="caja-form-hint" style={{ marginBottom: '0.75rem' }}>
          Sede: <strong>{nombreEstablecimiento}</strong>
        </p>
      )}
      <div className="caja-form-field">
        <label htmlFor="caja-fondo-inicial">Fondo inicial</label>
        <input
          id="caja-fondo-inicial"
          type="text"
          inputMode="decimal"
          placeholder="0"
          value={fondo}
          onChange={e => setFondo(e.target.value.replace(/[^0-9.]/g, ''))}
          autoFocus
        />
        <span className="caja-form-hint">Efectivo con el que abres tu turno hoy.</span>
      </div>
    </ConfirmDialog>
  );
}
