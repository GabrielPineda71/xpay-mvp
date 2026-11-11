// D10 — la vista empresa depende de la asociación real (esUsuarioEmpresa del
// login), no del nombre fijo qa.empresa1.
import { describe, it, expect } from 'vitest';
import { getViewForUser, type AuthUser } from './AuthContext.tsx';

const base: AuthUser = {
  idUsuario: 1, idPersona: 1, usuario: 'cliente', estado: 'ACTIVO',
  roles: ['USUARIO_FINAL'], token: 't', requiereCambioClave: false,
};

describe('getViewForUser — vista empresa (D10)', () => {
  it('usuario con asociación de empresa activa → empresa', () => {
    expect(getViewForUser({ ...base, usuario: 'operador.miempresa', esUsuarioEmpresa: true })).toBe('empresa');
  });

  it('el nombre qa.empresa1 ya no basta sin asociación', () => {
    expect(getViewForUser({ ...base, usuario: 'qa.empresa1', esUsuarioEmpresa: false })).toBe('wallet');
    expect(getViewForUser({ ...base, usuario: 'qa.empresa1' })).toBe('wallet');
  });

  it('sesión guardada sin el campo → wallet', () => {
    expect(getViewForUser(base)).toBe('wallet');
  });

  it('los roles administrativos siguen teniendo prioridad', () => {
    expect(getViewForUser({ ...base, roles: ['SUPERUSUARIO'], esUsuarioEmpresa: true })).toBe('admin');
  });
});
