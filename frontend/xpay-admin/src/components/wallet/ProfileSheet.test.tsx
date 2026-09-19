// XPAY-392 — A: ProfileSheet permite abrir Mi perfil (deja de estar
// deshabilitado y notifica al padre vía onOpenProfileDetail). Presentación
// pura, sin red/backend/Passport.
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ProfileSheet } from './ProfileSheet.tsx';

describe('ProfileSheet (XPAY-392 R-A)', () => {
  it('A: "Mi perfil" está habilitado y llama a onOpenProfileDetail al hacer clic', () => {
    const onOpenProfileDetail = vi.fn();
    render(
      <ProfileSheet
        userName="qa.usuario1"
        onLogout={vi.fn()}
        onClose={vi.fn()}
        onOpenProfileDetail={onOpenProfileDetail}
      />,
    );
    const boton = screen.getByRole('button', { name: 'Mi perfil' });
    expect(boton).not.toBeDisabled();
    boton.click();
    expect(onOpenProfileDetail).toHaveBeenCalledTimes(1);
  });

  it('"Mi perfil" permanece deshabilitado si no se provee onOpenProfileDetail (comportamiento previo intacto)', () => {
    render(<ProfileSheet userName="qa.usuario1" onLogout={vi.fn()} onClose={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Mi perfil' })).toBeDisabled();
  });

  it('conserva "Cerrar sesión"', () => {
    const onLogout = vi.fn();
    render(<ProfileSheet userName="qa.usuario1" onLogout={onLogout} onClose={vi.fn()} onOpenProfileDetail={vi.fn()} />);
    screen.getByRole('button', { name: 'Cerrar sesión' }).click();
    expect(onLogout).toHaveBeenCalledTimes(1);
  });
});
