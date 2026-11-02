import { describe, it, expect, vi, afterEach } from 'vitest';
import { ambienteXpay, validarAmbienteXpay } from './ambiente.ts';

describe('validarAmbienteXpay', () => {
  it('acepta QA y PROD', () => {
    expect(validarAmbienteXpay('QA', true)).toBe('QA');
    expect(validarAmbienteXpay('PROD', true)).toBe('PROD');
  });

  it('sin variable, en desarrollo y pruebas asume QA', () => {
    expect(validarAmbienteXpay(undefined, false)).toBe('QA');
    expect(validarAmbienteXpay('', false)).toBe('QA');
  });

  it('sin variable, en un build de producción falla', () => {
    expect(() => validarAmbienteXpay(undefined, true)).toThrow('VITE_XPAY_ENV es obligatorio');
    expect(() => validarAmbienteXpay('', true)).toThrow('VITE_XPAY_ENV es obligatorio');
  });

  it('un valor inválido nunca se trata como QA', () => {
    for (const valor of ['prod', 'qa', 'PRD', 'DEV', ' PROD']) {
      expect(() => validarAmbienteXpay(valor, false)).toThrow('VITE_XPAY_ENV inválido');
      expect(() => validarAmbienteXpay(valor, true)).toThrow('VITE_XPAY_ENV inválido');
    }
  });
});

describe('ambienteXpay', () => {
  afterEach(() => { vi.unstubAllEnvs(); });

  it('lee VITE_XPAY_ENV en cada llamada', () => {
    vi.stubEnv('VITE_XPAY_ENV', 'PROD');
    expect(ambienteXpay()).toBe('PROD');
    vi.stubEnv('VITE_XPAY_ENV', 'QA');
    expect(ambienteXpay()).toBe('QA');
  });
});
