import { jsPDF } from 'jspdf';
import { fmtMoney, fmtDate } from '../utils.ts';

export interface ComprobanteCierreData {
  idCierre:                number;
  idComercio:              number;
  nombreComercio?:         string | null;
  fechaCierre:             string;
  fechaHoraCorteUtc:       string;
  codigoUnico:             string;
  estado:                  string;
  cantidadRecargas:        number;
  valorTotalRecaudado:     number;
  valorLiquidadoAlGenerar: number;
  valorPendienteAlGenerar: number;
}

// El PDF se genera 100% en el navegador a partir del snapshot que ya devolvió
// el backend — nunca recalcula valores. "Valores al momento de generación del
// cierre", dejando el espacio del QR reservado para una futura validación.
// Compartido entre MiComercioPage (comercio) y AdminWalletCierresDiariosComercioPage (XPAY).
export function generarComprobantePdfCierre(data: ComprobanteCierreData) {
  const doc = new jsPDF({ unit: 'pt', format: 'a4' });
  const left = 40;
  let y = 50;

  doc.setFontSize(16);
  doc.text('XPAY — Comprobante de Cierre Diario de Comercio', left, y);
  y += 22;

  doc.setFontSize(9);
  doc.setTextColor(120);
  doc.text('Ambiente QA · datos ficticios · sin dinero real · sin producción', left, y);
  y += 26;
  doc.setTextColor(0);

  doc.setFontSize(11);
  const rows: [string, string][] = [
    ['Código único',          data.codigoUnico],
    ['Cierre #',              String(data.idCierre)],
    ['Comercio',              data.nombreComercio ? `${data.nombreComercio} (#${data.idComercio})` : `#${data.idComercio}`],
    ['Fecha cerrada',         data.fechaCierre],
    ['Corte',                 fmtDate(data.fechaHoraCorteUtc)],
    ['Estado',                data.estado],
    ['Cantidad de recargas',  String(data.cantidadRecargas)],
    ['Valor total recaudado', fmtMoney(data.valorTotalRecaudado)],
    ['Valor liquidado',       fmtMoney(data.valorLiquidadoAlGenerar)],
    ['Valor pendiente',       fmtMoney(data.valorPendienteAlGenerar)],
  ];
  for (const [label, value] of rows) {
    doc.text(`${label}:`, left, y);
    doc.text(value, left + 190, y);
    y += 18;
  }

  y += 12;
  doc.setFontSize(9);
  doc.setTextColor(150);
  doc.text('Valores al momento de generación del cierre — no reflejan liquidaciones posteriores.', left, y);
  y += 24;
  doc.setTextColor(0);

  // Espacio reservado para QR futuro — sin validación por QR todavía.
  doc.setDrawColor(180);
  doc.rect(left, y, 90, 90);
  doc.setFontSize(8);
  doc.text('QR', left + 38, y + 48);
  doc.setFontSize(8);
  doc.setTextColor(120);
  doc.text('Espacio reservado para código QR de validación futura.', left + 105, y + 40, { maxWidth: 300 });
  doc.setTextColor(0);

  doc.save(`cierre-diario-comercio-${data.idComercio}-${data.fechaCierre}.pdf`);
}
