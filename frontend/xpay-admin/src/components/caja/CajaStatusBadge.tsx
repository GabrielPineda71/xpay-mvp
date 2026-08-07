import { estadoLabel } from '../../utils/caja-format.ts';

export function CajaStatusBadge({ estado }: { estado: string }) {
  const cls = `caja-badge caja-badge-${estado.toLowerCase()}`;
  return <span className={cls}>{estadoLabel(estado)}</span>;
}
