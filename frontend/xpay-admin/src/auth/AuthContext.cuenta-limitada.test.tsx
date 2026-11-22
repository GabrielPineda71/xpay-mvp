import { beforeEach, describe, expect, it } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { AuthProvider, useAuth } from './AuthContext.tsx';
const titular = { idUsuario: 991, idPersona: 992, usuario: 'titular.sintetico', estado: 'ACTIVO', roles: ['USUARIO_FINAL'], token: 'sintetico', requiereCambioClave: false, requierePerfil: false };
function Estado() { const { user } = useAuth(); return <p>{user ? user.requierePerfil ? 'GUIA DE REGISTRO' : 'VISTA HABITUAL' : 'SIN SESION'}</p>; }
beforeEach(() => { localStorage.clear(); sessionStorage.clear(); });
describe('Rechazo autoritativo CUENTA_LIMITADA', () => {
  it('vuelve a la guía y persiste solo la restricción del mismo titular', () => {
    localStorage.setItem('xpay_user', JSON.stringify(titular)); localStorage.setItem('xpay_token', titular.token);
    render(<AuthProvider><Estado /></AuthProvider>);
    act(() => { window.dispatchEvent(new Event('xpay:cuenta-limitada')); });
    expect(screen.getByText('GUIA DE REGISTRO')).toBeInTheDocument();
    expect(JSON.parse(localStorage.getItem('xpay_user')!)).toEqual({ ...titular, requierePerfil: true });
    expect(localStorage.getItem('xpay_token')).toBe(titular.token); expect(sessionStorage.length).toBe(0);
  });
  it.each([['ADMIN_XPAY'], ['COMERCIO']])('no cambia una sesión con vista de otro rol %s', roles => {
    const cuenta = { ...titular, roles: [roles] };
    localStorage.setItem('xpay_user', JSON.stringify(cuenta)); render(<AuthProvider><Estado /></AuthProvider>);
    act(() => { window.dispatchEvent(new Event('xpay:cuenta-limitada')); });
    expect(screen.getByText('VISTA HABITUAL')).toBeInTheDocument(); expect(JSON.parse(localStorage.getItem('xpay_user')!)).toEqual(cuenta);
  });
});
