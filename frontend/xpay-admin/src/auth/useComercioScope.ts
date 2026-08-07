import { useEffect, useState } from 'react';
import { getMiScope, type ComercioScope } from '../api/caja.ts';

// Resuelve el scope operativo real (rol_comercio/sede) vía /api/comercio/mi-scope.
// El JWT solo trae el rol global COMERCIO — el rol_comercio fino se resuelve
// server-side por cada usuario. Mismo patrón ya usado en MiComercioPage.tsx,
// extraído aquí para reutilizarlo en las páginas nuevas de Caja/Cuadre sin
// duplicar la llamada tres veces.
// `enabled=false` evita el fetch por completo — necesario en lugares como
// Layout.tsx, que renderiza para todas las vistas (admin/comercio/empresa/
// wallet) y no debe llamar a /api/comercio/mi-scope (Authorize Roles=COMERCIO)
// cuando el usuario ni siquiera tiene ese rol global.
export function useComercioScope(enabled = true) {
  const [scope, setScope]     = useState<ComercioScope | null>(null);
  const [loading, setLoading] = useState(enabled);
  const [error, setError]     = useState<string | null>(null);

  useEffect(() => {
    if (!enabled) { setLoading(false); return; }
    let cancelado = false;
    setLoading(true);
    setError(null);
    getMiScope()
      .then(s => { if (!cancelado) setScope(s); })
      .catch(err => { if (!cancelado) setError((err as Error).message || 'No fue posible resolver tu acceso operativo.'); })
      .finally(() => { if (!cancelado) setLoading(false); });
    return () => { cancelado = true; };
  }, [enabled]);

  return { scope, loading, error };
}
