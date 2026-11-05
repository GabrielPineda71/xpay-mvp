import { describe, it, expect, vi, afterEach } from 'vitest';
import { generarComprobantePdfCierre, type ComprobanteCierreData } from './comprobanteCierrePdf.ts';

// jsPDF reemplazado: se registran los textos escritos y nunca se descarga nada.
const textos: string[] = [];
vi.mock('jspdf', () => ({
  jsPDF: class {
    setFontSize() {}
    setTextColor() {}
    setDrawColor() {}
    rect() {}
    text(t: string) { textos.push(t); }
    save() {}
  },
}));

const DATOS: ComprobanteCierreData = {
  idCierre: 1, idComercio: 7, nombreComercio: 'Comercio de prueba',
  fechaCierre: '2026-11-01', fechaHoraCorteUtc: '2026-11-01T23:59:00Z',
  codigoUnico: 'CIERRE-PRUEBA-1', estado: 'GENERADO',
  cantidadRecargas: 2, valorTotalRecaudado: 1000,
  valorLiquidadoAlGenerar: 600, valorPendienteAlGenerar: 400,
};

describe('generarComprobantePdfCierre — leyenda de ambiente', () => {
  afterEach(() => { vi.unstubAllEnvs(); textos.length = 0; });

  it('en QA conserva la leyenda «Ambiente QA»', () => {
    vi.stubEnv('VITE_XPAY_ENV', 'QA');
    generarComprobantePdfCierre(DATOS);
    expect(textos.some(t => t.startsWith('Ambiente QA'))).toBe(true);
  });

  it('en PROD no muestra ninguna leyenda de ambiente', () => {
    vi.stubEnv('VITE_XPAY_ENV', 'PROD');
    generarComprobantePdfCierre(DATOS);
    expect(textos.some(t => /ambiente/i.test(t))).toBe(false);
    expect(textos).toContain('XPAY — Comprobante de Cierre Diario de Comercio');
  });
});
