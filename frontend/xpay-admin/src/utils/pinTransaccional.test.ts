import { describe, expect, it } from 'vitest';
import { esPinActualValido, esPinValido, soloDigitosPin } from './pinTransaccional.ts';

describe('PIN transaccional de seis dígitos', () => {
  it('admite seis dígitos, incluso un cero inicial', () => {
    expect(esPinValido('025801')).toBe(true);
  });
  it.each(['25801', '0258014'])('reserva el PIN anterior %s exclusivamente para cambiarlo', pin => {
    expect(esPinValido(pin)).toBe(false);
    expect(esPinActualValido(pin)).toBe(true);
    expect(soloDigitosPin(pin)).toBe(pin);
  });
  it('rechaza caracteres no numéricos y no recorta un PIN pegado', () => {
    expect(esPinValido('25a801')).toBe(false);
    expect(esPinActualValido('25a801')).toBe(false);
    expect(soloDigitosPin('0258014')).toBe('0258014');
  });
});
