import { useCallback, useState } from 'react';
import { get, post } from '../api/client.ts';
import type { BrebLlave } from '../pages/UserWalletPage.tsx';

// XPAY-392 — hook dedicado a la ASOCIACIÓN LOCAL de la llave Bre-B propia
// (GET/POST /api/breb/mi-llave), pensado para Perfil (BrebMyKeySection).
//
// DELIBERADAMENTE NO incluye ni comparte:
//   - POST /api/breb/mi-llave/resolver (Resolve real contra Passport);
//   - ningún estado equivalente a `realResolveResult`.
// Esa verificación es un paso de seguridad inmediatamente previo a una
// operación financiera y pertenece EXCLUSIVAMENTE al flujo "Retirar a mi
// llave Bre-B" en UserWalletPage.tsx — nunca se ejecuta automáticamente
// al cargar Perfil, y no se convierte en estado persistente/global
// (decisión XPAY-391/392).
//
// Reutiliza el tipo `BrebLlave` ya existente (exportado desde
// UserWalletPage.tsx) para no duplicar la forma del DTO.
export type BrebKeyType = 'ID' | 'PHONE' | 'EMAIL' | 'ALPHA' | 'BCODE';

export interface BrebKeyMsg {
  ok: boolean;
  text: string;
}

export function useMyBrebKey() {
  const [llave, setLlave] = useState<BrebLlave | null>(null);
  const [loading, setLoading] = useState(false);
  const [registrando, setRegistrando] = useState(false);
  const [mensaje, setMensaje] = useState<BrebKeyMsg | null>(null);

  // GET /api/breb/mi-llave — solo lectura, mismo endpoint ya usado por
  // UserWalletPage.tsx (loadBreb). No dispara ninguna llamada a Passport.
  const cargar = useCallback(async () => {
    setLoading(true);
    try {
      const r = await get<{ success: boolean; data: BrebLlave | null }>('/api/breb/mi-llave');
      setLlave(r.data);
    } catch {
      // No crítico — se conserva el último estado local conocido, mismo
      // criterio ya usado por loadBreb() en UserWalletPage.tsx.
    } finally {
      setLoading(false);
    }
  }, []);

  // POST /api/breb/mi-llave — registra/actualiza la asociación local.
  // Misma semántica exacta que handleRegistrarLlave en UserWalletPage.tsx
  // (tab 'banco'): NO es CreateKey de Passport, es el endpoint local ya
  // existente de XPAY.
  const registrar = useCallback(async (keyType: BrebKeyType, keyValueRaw: string): Promise<boolean> => {
    const keyValue = keyValueRaw.trim();
    if (!keyValue) {
      setMensaje({ ok: false, text: 'Ingresa el valor de la llave.' });
      return false;
    }
    setRegistrando(true);
    setMensaje(null);
    try {
      const r = await post<{ success: boolean; data?: BrebLlave; message?: string }>(
        '/api/breb/mi-llave',
        { keyType, keyValue },
      );
      if (r.success && r.data) {
        setLlave(r.data);
        setMensaje({ ok: true, text: `Llave registrada: ${r.data.keyValueMasked} — estado: ${r.data.estado}` });
        return true;
      }
      setMensaje({ ok: false, text: r.message ?? 'Error registrando llave.' });
      return false;
    } catch (err) {
      setMensaje({ ok: false, text: (err as Error).message || 'Error registrando llave.' });
      return false;
    } finally {
      setRegistrando(false);
    }
  }, []);

  return { llave, loading, registrando, mensaje, cargar, registrar };
}
