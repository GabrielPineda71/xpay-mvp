// P0-B frontend — validarPoliticaClave replica AuthService.ValidarPoliticaClave
// (mismos mensajes, mismo orden, misma semántica Unicode por unidad UTF-16).
import { describe, it, expect } from 'vitest';
import { CARACTERES_ESPECIALES_CLAVE, validarPoliticaClave } from './politicaClave.ts';

const LONGITUD    = 'La contraseña debe tener entre 8 y 128 caracteres.';
const MAYUSCULA   = 'La contraseña debe incluir al menos una letra mayúscula.';
const MINUSCULA   = 'La contraseña debe incluir al menos una letra minúscula.';
const DIGITO      = 'La contraseña debe incluir al menos un dígito.';
const ESPECIAL    = 'La contraseña debe incluir al menos un carácter especial (!@#$%^&*()-_=+).';

describe('validarPoliticaClave (P0-B frontend)', () => {
  it('una clave que cumple todo devuelve null', () => {
    expect(validarPoliticaClave('ClaveNueva1!')).toBeNull();
  });

  it('límites de longitud: 7 y 129 fallan; 8 y 128 cumplen', () => {
    expect(validarPoliticaClave('Abcde1!')).toBe(LONGITUD);
    expect(validarPoliticaClave('Abcdef1!')).toBeNull();
    expect(validarPoliticaClave('Ab1!' + 'x'.repeat(124))).toBeNull();
    expect(validarPoliticaClave('Ab1!' + 'x'.repeat(125))).toBe(LONGITUD);
    expect(validarPoliticaClave('')).toBe(LONGITUD);
  });

  it('cada regla con el mensaje exacto del backend y en su orden', () => {
    expect(validarPoliticaClave('clavenueva1!')).toBe(MAYUSCULA);
    expect(validarPoliticaClave('CLAVENUEVA1!')).toBe(MINUSCULA);
    expect(validarPoliticaClave('ClaveNueva!!')).toBe(DIGITO);
    expect(validarPoliticaClave('ClaveNueva12')).toBe(ESPECIAL);
  });

  it.each(CARACTERES_ESPECIALES_CLAVE.split(''))('el especial %s cumple', especial => {
    expect(validarPoliticaClave('ClaveNueva1' + especial)).toBeNull();
  });

  it('un carácter fuera de la lista (".") no cuenta como especial', () => {
    expect(validarPoliticaClave('ClaveNueva1.')).toBe(ESPECIAL);
  });

  it('Unicode del plano básico cuenta igual que char.IsUpper/IsLower/IsDigit', () => {
    expect(validarPoliticaClave('ñandú123!')).toBe(MAYUSCULA);
    expect(validarPoliticaClave('Ñandú123!')).toBeNull();
    // U+0663 (dígito arábigo-índico, categoría Nd).
    expect(validarPoliticaClave('Clave٣!x')).toBeNull();
  });

  it('una mayúscula fuera del plano básico (par sustituto) no cuenta, como en .NET', () => {
    // U+1D400 (MATHEMATICAL BOLD CAPITAL A): .NET evalúa cada sustituto por separado.
    expect(validarPoliticaClave('clave1!\u{1D400}')).toBe(MAYUSCULA);
  });

  it('la longitud se mide en unidades UTF-16, como string.Length en .NET', () => {
    // 4 caracteres BMP + 2 pares sustitutos = 8 unidades.
    expect('Ab1!\u{1D400}\u{1D400}').toHaveLength(8);
    expect(validarPoliticaClave('Ab1!\u{1D400}\u{1D400}')).toBeNull();
  });
});
