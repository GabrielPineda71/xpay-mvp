// Ambiente XPAY del build (VITE_XPAY_ENV): separa los QR y las leyendas de
// QA y PROD. Un QR generado en un ambiente se rechaza en el otro, porque su
// wallet destino es un número que en el otro ambiente pertenece a otra
// persona. Validación estricta: un valor inválido nunca se trata como QA.
export type AmbienteXpay = 'QA' | 'PROD';

const AMBIENTES_VALIDOS: readonly string[] = ['QA', 'PROD'];

// `exigirExplicito` = build de producción de Vite: ahí la variable es
// obligatoria (vite.config.ts ya impide compilar sin ella). Solo en
// desarrollo y pruebas, sin variable, se asume QA.
export function validarAmbienteXpay(valor: string | undefined, exigirExplicito: boolean): AmbienteXpay {
  if (valor === undefined || valor === '') {
    if (exigirExplicito) throw new Error('VITE_XPAY_ENV es obligatorio en un build: usa QA o PROD.');
    return 'QA';
  }
  if (AMBIENTES_VALIDOS.includes(valor)) return valor as AmbienteXpay;
  throw new Error('VITE_XPAY_ENV inválido: usa QA o PROD.');
}

// Se lee en cada llamada (no al cargar el módulo) para que las pruebas
// puedan fijar el ambiente con vi.stubEnv.
export function ambienteXpay(): AmbienteXpay {
  return validarAmbienteXpay(import.meta.env.VITE_XPAY_ENV, import.meta.env.PROD);
}
