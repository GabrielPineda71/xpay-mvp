export function fmtMoney(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return `$ ${value.toLocaleString('es-CO', { minimumFractionDigits: 0, maximumFractionDigits: 2 })}`;
}

export function fmtDate(dateStr: string | null | undefined): string {
  if (!dateStr) return '—';
  return new Date(dateStr).toLocaleString('es-CO');
}

export function fmtNum(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  return value.toLocaleString('es-CO');
}
