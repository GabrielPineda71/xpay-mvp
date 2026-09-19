import { useCallback, useState } from 'react';
import { get, patch } from '../api/client.ts';

// XPAY-401 — forma EXACTA de MiPerfilResponseDto (backend, XPAY-399). No se
// modifica el backend: este tipo refleja el contrato ya implementado y
// probado (58/58 tests, ver XPAY-399).
export interface MiPerfil {
  usuario: string;
  primerNombre: string | null;
  segundoNombre: string | null;
  primerApellido: string | null;
  segundoApellido: string | null;
  tipoDocumento: string | null;
  numeroDocumento: string | null;
  fechaNacimiento: string | null;
  celular: string;
  email: string | null;
  direccion: string | null;
  ciudad: string | null;
  departamento: string | null;
  pais: string;
  identidadVerificada: boolean;
  estadoKycActual: string;
  emailVerificado: boolean;
  celularVerificado: boolean;
}

// XPAY-401 — mismos 5 campos editables autorizados por PerfilService
// (XPAY-399) y NINGUNO más — ni por accidente ni por conveniencia. No
// existe forma de que este tipo transporte identidad legal/KYC/IDs.
export interface ActualizarMiPerfilInput {
  celular?: string;
  email?: string;
  direccion?: string;
  ciudad?: string;
  departamento?: string;
}

interface Msg { ok: boolean; text: string; }

// XPAY-401 — hook dedicado a GET/PATCH /api/usuarios/mi-perfil. Mismo
// criterio de useMyBrebKey.ts (XPAY-392): aislado, sin mezclar estado con
// Mi llave Bre-B ni con Retirar. Nunca llama a Passport/Veriff — solo el
// endpoint self-service ya implementado en XPAY-399.
export function useMiPerfil() {
  const [perfil,    setPerfil]    = useState<MiPerfil | null>(null);
  const [loading,   setLoading]   = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [mensaje,   setMensaje]   = useState<Msg | null>(null);

  const cargar = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const r = await get<{ success: boolean; data: MiPerfil }>('/api/usuarios/mi-perfil');
      setPerfil(r.data);
    } catch (err) {
      setLoadError((err as Error).message || 'No se pudo cargar tu perfil.');
    } finally {
      setLoading(false);
    }
  }, []);

  // Envía ÚNICAMENTE los 5 campos editables (ver ActualizarMiPerfilInput) —
  // nunca IDs, nunca identidad legal, nunca KYC. Usa la respuesta del PATCH
  // (no un segundo GET) como nueva fuente de verdad, tal como pide XPAY-401.
  const actualizar = useCallback(async (cambios: ActualizarMiPerfilInput): Promise<boolean> => {
    setGuardando(true);
    setMensaje(null);
    try {
      const r = await patch<{ success: boolean; message?: string; data: MiPerfil }>('/api/usuarios/mi-perfil', cambios);
      if (r.success) {
        setPerfil(r.data);
        setMensaje({ ok: true, text: r.message ?? 'Información actualizada correctamente.' });
        return true;
      }
      setMensaje({ ok: false, text: r.message ?? 'No se pudo actualizar la información.' });
      return false;
    } catch (err) {
      setMensaje({ ok: false, text: (err as Error).message || 'No se pudo actualizar la información.' });
      return false;
    } finally {
      setGuardando(false);
    }
  }, []);

  return { perfil, loading, loadError, guardando, mensaje, setMensaje, cargar, actualizar };
}
