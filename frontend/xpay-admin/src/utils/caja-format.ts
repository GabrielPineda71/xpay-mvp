export function fmtMoney(v: number | null | undefined): string {
  if (v == null) return '—';
  return v.toLocaleString('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
}

export function fmtDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString('es-CO', { dateStyle: 'short', timeStyle: 'short' });
  } catch {
    return iso;
  }
}

export function fmtFechaOperativa(iso: string | null | undefined): string {
  if (!iso) return '—';
  // fechaOperativa llega como DateOnly ("yyyy-MM-dd") — no aplicar Date() con
  // hora local para no correr un día por desfase de zona horaria.
  const [y, m, d] = iso.split('-');
  if (!y || !m || !d) return iso;
  return `${d}/${m}/${y}`;
}

// El backend (WalletCajaComercioService/WalletRecargaComercioService/
// WalletCierreDiarioComercioController) ya devuelve `message` en español,
// listo para mostrar tal cual — no se reinterpreta el significado, solo se
// extrae el texto del Error lanzado por api/client.ts.
export function getApiErrorMessage(err: unknown): string {
  if (err instanceof Error && err.message) return err.message;
  return 'Ocurrió un error inesperado. Intenta nuevamente.';
}

const ESTADO_LABELS: Record<string, string> = {
  ABIERTA:                   'Abierta',
  EN_CUADRE:                 'En cuadre',
  CERRADA:                   'Cerrada',
  CON_DIFERENCIA:            'Cerrada con diferencia',
  CERRADA_AUTOMATICAMENTE:   'Cerrada automáticamente',
  REVISADA:                  'Revisada',
};

export function estadoLabel(estado: string): string {
  return ESTADO_LABELS[estado] ?? estado;
}
