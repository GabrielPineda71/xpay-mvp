import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { get } from '../../api/client.ts';
import type { BrebLlave } from '../../pages/UserWalletPage.tsx';

// Recordatorio de lectura, nunca una barrera ni una resolución automática.
// También aparece si se cerró la app después de guardar el PIN y antes de
// registrar la llave. Una llave ya validada no vuelve a ofrecer registro.
export function WalletLlaveBrebAviso() {
  const [estado, setEstado] = useState<'cargando' | 'identidad' | 'sin-llave' | 'pendiente' | 'validada' | 'desconocido'>('cargando');
  useEffect(() => {
    let generacion = 0;
    const cargar = async () => {
      const consulta = ++generacion;
      setEstado('cargando');
      let identidadConsultada = false;
      try {
        const kyc = await get<{ success: boolean; data: { estadoKyc: string } }>('/api/kyc/mi-estado');
        if (consulta !== generacion) return;
        if (!kyc.success || kyc.data?.estadoKyc !== 'APROBADO') {
          setEstado('identidad');
          return;
        }
        identidadConsultada = true;
        const r = await get<{ success: boolean; data: BrebLlave | null }>('/api/breb/mi-llave');
        if (consulta === generacion) setEstado(!r.success ? 'desconocido' : !r.data ? 'sin-llave' : r.data.estado === 'VALIDADA' ? 'validada' : 'pendiente');
      } catch {
        if (consulta === generacion) setEstado(identidadConsultada ? 'desconocido' : 'identidad');
      }
    };
    const actualizar = () => { void cargar(); };
    window.addEventListener('xpay:kyc-estado-actualizado', actualizar);
    actualizar();
    return () => {
      generacion++;
      window.removeEventListener('xpay:kyc-estado-actualizado', actualizar);
    };
  }, []);
  if (estado === 'cargando' || estado === 'validada') return null;
  return <aside className="profile-section" aria-label="Llave Bre-B para retiros">
    <p>{estado === 'identidad'
      ? 'Antes de registrar tu llave Bre-B, completa la verificación de identidad en Mi Wallet.'
      : estado === 'sin-llave'
      ? 'Para retirar a tu banco, registra y verifica una llave Bre-B de tu propia cuenta bancaria.'
      : estado === 'pendiente'
        ? 'Tu llave Bre-B está registrada. Verifícala en «Retirar a mi llave Bre-B» antes de retirar al banco.'
        : 'Para retirar a tu banco, revisa tu llave Bre-B propia en Perfil.'}</p>
    <p>{estado === 'identidad'
      ? 'La identidad debe estar aprobada para operar. La llave solo es necesaria para retirar al banco.'
      : 'Puedes hacerlo más tarde. La llave no bloquea los envíos entre Wallets ni los pagos con QR.'}</p>
    <Link to={estado === 'identidad' ? '/mi-wallet' : '/mi-wallet/perfil'}>{estado === 'identidad' ? 'Verificar identidad en Mi Wallet' : estado === 'sin-llave' ? 'Registrar mi llave en Perfil' : 'Ver mi llave en Perfil'}</Link>
  </aside>;
}
