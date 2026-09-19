// XPAY-390 FASE 1 UX — franja verde reducida a las 4 acciones principales
// (XPAY-389/390 R1). Presentación pura, sin red/backend/Passport.
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { UserPrimaryActions } from './UserPrimaryActions.tsx';

describe('UserPrimaryActions (XPAY-390 R1)', () => {
  it('A: muestra exactamente las 4 acciones, en el orden exacto pedido', () => {
    render(<UserPrimaryActions onAction={vi.fn()} />);
    const buttons = screen.getAllByRole('button');
    expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual([
      'Recibir',
      'Enviar',
      'Comprar con QR',
      'Retirar a mi llave Bre-B',
    ]);
  });

  it('A: no muestra las acciones retiradas de la franja principal', () => {
    render(<UserPrimaryActions onAction={vi.fn()} />);
    expect(screen.queryByText('Enviar a mi llave Bre-B')).not.toBeInTheDocument();
    expect(screen.queryByText('Dónde comprar')).not.toBeInTheDocument();
    expect(screen.queryByText('Retirar a mi banco')).not.toBeInTheDocument();
    expect(screen.queryByText('Pagar QR')).not.toBeInTheDocument();
  });

  it('la acción "Retirar a mi llave Bre-B" sigue emitiendo la key real \'withdraw-breb-real\'', () => {
    const onAction = vi.fn();
    render(<UserPrimaryActions onAction={onAction} />);
    screen.getByLabelText('Retirar a mi llave Bre-B').click();
    expect(onAction).toHaveBeenCalledWith('withdraw-breb-real');
  });
});
