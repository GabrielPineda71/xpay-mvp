import { describe, expect, it } from 'vitest';
import { validarSesionActualizada, type AuthUser } from './AuthContext.tsx';
const actual: AuthUser = { idUsuario: 991, idPersona: 992, usuario: 'titular.sintetico', estado: 'ACTIVO', roles: ['USUARIO_FINAL'], token: 'previo', requiereCambioClave: false, requierePerfil: true };
const nueva = { ...actual, token: 'nuevo-servidor', requierePerfil: false };
describe('Adopción de sesión de Wallet', () => {
  it('acepta respuesta completa de servidor para el mismo actor sin mutar la anterior', () => {
    expect(validarSesionActualizada(nueva, actual)).toEqual(nueva); expect(actual.requierePerfil).toBe(true);
  });
  it.each([{ idUsuario: 1 }, { idPersona: 1 }, { usuario: 'otro' }, { token: '' }, { requierePerfil: true }, { estado: 'INACTIVO' }, { requiereCambioClave: true }, { roles: null }, { roles: [] }, { roles: ['ADMIN_XPAY'] }, { esUsuarioEmpresa: true }])('rechaza sesiones incompletas, ajenas o limitadas (%s)', cambio => {
    expect(() => validarSesionActualizada({ ...nueva, ...cambio }, actual)).toThrow('Vuelve a ingresar');
  });
});
