import type { ReactNode } from 'react';
import { ambienteXpay } from './ambiente.ts';

// Avisos que solo tienen sentido en QA ("Ambiente QA/Demo", datos ficticios,
// PIN sin validar en esta fase...): en PROD no se muestra nada.
export function SoloQa({ children }: { children: ReactNode }) {
  return ambienteXpay() === 'QA' ? <>{children}</> : null;
}
