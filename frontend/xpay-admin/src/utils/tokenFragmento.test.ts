// P0-B frontend — leerTokenDeFragmento: solo fragmento, solo el formato
// exacto del backend (43 caracteres base64url sin relleno). Token sintético.
import { describe, it, expect } from 'vitest';
import { leerTokenDeFragmento } from './tokenFragmento.ts';

const TOKEN = 'Ab3_-'.repeat(8) + 'Xy9';

describe('leerTokenDeFragmento (P0-B frontend)', () => {
  it('el token sintético de prueba tiene 43 caracteres', () => {
    expect(TOKEN).toHaveLength(43);
  });

  it('acepta un token de 43 caracteres [A-Za-z0-9_-] en el fragmento', () => {
    expect(leerTokenDeFragmento('#token=' + TOKEN)).toBe(TOKEN);
  });

  it('ignora otros parámetros del fragmento', () => {
    expect(leerTokenDeFragmento('#token=' + TOKEN + '&otro=1')).toBe(TOKEN);
  });

  it.each([
    ['vacío', ''],
    ['solo almohadilla', '#'],
    ['sin almohadilla (no es fragmento)', 'token=' + TOKEN],
    ['query string', '?token=' + TOKEN],
    ['42 caracteres', '#token=' + TOKEN.slice(1)],
    ['44 caracteres', '#token=' + TOKEN + 'A'],
    ['carácter +', '#token=' + TOKEN.slice(1) + '+'],
    ['carácter /', '#token=' + TOKEN.slice(1) + '/'],
    ['relleno =', '#token=' + TOKEN.slice(1) + '='],
    ['espacio', '#token=' + TOKEN.slice(1) + ' '],
    ['otro parámetro sin token', '#otro=' + TOKEN],
    ['token duplicado', '#token=' + TOKEN + '&token=' + TOKEN],
    ['token vacío', '#token='],
  ])('rechaza: %s', (_caso, hash) => {
    expect(leerTokenDeFragmento(hash)).toBeNull();
  });
});
