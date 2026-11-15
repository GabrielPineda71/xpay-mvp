import { useEffect, useState } from 'react';
import { get } from '../api/client.ts';
import { ambienteXpay } from '../config/ambiente.ts';

export const CONFIGURACION_REGISTRO_URL = '/api/usuarios/registro/configuracion';

export interface DocumentoRegistro {
  version: string;
  fecha: string;
  url: string;
}

export interface ConfiguracionRegistroPublico {
  terminos: DocumentoRegistro;
  politicaDatos: DocumentoRegistro;
}

type EstadoConfiguracion =
  | { estado: 'cargando' | 'cerrado' }
  | { estado: 'disponible'; documentos: ConfiguracionRegistroPublico };

function documentoValido(documento?: DocumentoRegistro): boolean {
  if (!documento || typeof documento.version !== 'string' || !documento.version.trim() ||
      typeof documento.fecha !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(documento.fecha) ||
      !Number.isFinite(Date.parse(documento.fecha)) || typeof documento.url !== 'string') return false;
  try {
    const url = new URL(documento.url);
    return url.protocol === 'https:' && !url.username && !url.password && !url.search && !url.hash;
  } catch {
    return false;
  }
}

export function useConfiguracionRegistroPublico(): EstadoConfiguracion {
  const esQa = ambienteXpay() === 'QA';
  const [configuracion, setConfiguracion] = useState<EstadoConfiguracion>({ estado: 'cargando' });

  useEffect(() => {
    if (!esQa) return;
    let vigente = true;
    async function consultar() {
      try {
        const respuesta = await get<{ data?: ConfiguracionRegistroPublico }>(CONFIGURACION_REGISTRO_URL);
        if (!vigente) return;
        const documentos = respuesta?.data;
        setConfiguracion(documentos && documentoValido(documentos.terminos) && documentoValido(documentos.politicaDatos)
          ? { estado: 'disponible', documentos }
          : { estado: 'cerrado' });
      } catch {
        if (vigente) setConfiguracion({ estado: 'cerrado' });
      }
    }
    void consultar();
    return () => { vigente = false; };
  }, [esQa]);

  return esQa ? configuracion : { estado: 'cerrado' };
}
