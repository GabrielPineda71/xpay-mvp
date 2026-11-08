import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { leerTokenDeFragmento } from '../utils/tokenFragmento.ts';

// P0-B frontend — captura el token del fragmento (#token=...) UNA sola vez,
// en el primer render, y en cuanto el router lo permite (efecto) reemplaza la
// entrada actual por la misma ruta sin fragmento ni query: el token sale de
// la barra de direcciones y del historial de sesión antes de cualquier
// llamada de red. Desde ahí vive solo en memoria (estado de este
// componente): nunca en localStorage/sessionStorage/cookie, query string ni
// consola. Devuelve null si falta o no tiene el formato exacto.
export function useTokenDeFragmento(): string | null {
  const location = useLocation();
  const navigate = useNavigate();
  const [token] = useState(() => leerTokenDeFragmento(location.hash));

  useEffect(() => {
    if (location.hash !== '' || location.search !== '') {
      navigate(location.pathname, { replace: true });
    }
  }, [location.hash, location.search, location.pathname, navigate]);

  return token;
}
