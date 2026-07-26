export function fmtMoney(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return `$ ${value.toLocaleString('es-CO', { minimumFractionDigits: 0, maximumFractionDigits: 2 })}`;
}

// Formatea un timestamp (instante, no fecha de calendario) en hora de Colombia
// (America/Bogota, UTC-5), sin importar la zona horaria del navegador/SO del
// usuario. El backend siempre entrega estos timestamps marcados en UTC (con
// "Z"); esta es la única función que debe hacer la conversión a hora local de
// negocio — no repetir ajustes manuales (`addHours(-5)`) en otras pantallas.
//
// No usar esta función con fechas de calendario puras (tipo `DateOnly`,
// "yyyy-MM-dd", p.ej. la fecha operativa de un cierre diario) — esas no tienen
// componente horario y no deben pasar por conversión de zona horaria.
export function fmtDate(dateStr: string | null | undefined): string {
  if (!dateStr) return '—';
  return new Date(dateStr).toLocaleString('es-CO', {
    timeZone: 'America/Bogota',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
  });
}

export function fmtNum(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return value.toLocaleString('es-CO');
}
