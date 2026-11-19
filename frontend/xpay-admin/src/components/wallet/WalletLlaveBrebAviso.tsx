import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { get } from '../../api/client.ts';
import type { BrebLlave } from '../../pages/UserWalletPage.tsx';

// Recordatorio de lectura, nunca una barrera ni una resolución automática.
// También aparece si se cerró la app después de guardar el PIN y antes de
// registrar la llave. Una llave ya validada no vuelve a ofrecer registro.
export function WalletLlaveBrebAviso() {
  const [estado, setEstado] = useState<'cargando' | 'sin-llave' | 'pendiente' | 'validada' | 'desconocido'>('cargando');
  useEffect(() => {
    let cancelado = false;
    void get<{ success: boolean; data: BrebLlave | null }>('/api/breb/mi-llave').then(r => {
      if (cancelado) return;
      setEstado(!r.success ? 'desconocido' : !r.data ? 'sin-llave' : r.data.estado === 'VALIDADA' ? 'validada' : 'pendiente');
    }).catch(() => { if (!cancelado) setEstado('desconocido'); });
    return () => { cancelado = true; };
  }, []);
  if (estado === 'cargando' || estado === 'validada') return null;
  return <aside className="profile-section" aria-label="Llave Bre-B para retiros">
    <p>{estado === 'sin-llave'
      ? 'Para retirar a tu banco, registra y verifica una llave Bre-B de tu propia cuenta bancaria.'
      : estado === 'pendiente'
        ? 'Tu llave Bre-B está registrada. Verifícala en «Retirar a mi llave Bre-B» antes de retirar al banco.'
        : 'Para retirar a tu banco, revisa tu llave Bre-B propia en Perfil.'}</p>
    <p>Puedes hacerlo más tarde. Esto no bloquea los envíos entre Wallets ni los pagos con QR.</p>
    <Link to="/mi-wallet/perfil">{estado === 'sin-llave' ? 'Registrar mi llave en Perfil' : 'Ver mi llave en Perfil'}</Link>
  </aside>;
}
