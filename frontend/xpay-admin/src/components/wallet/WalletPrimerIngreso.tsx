import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { ApiError, get } from '../../api/client.ts';
import { estadoPinValido, type EstadoPin } from '../../utils/pinTransaccional.ts';
import { PinTransaccionalSection } from '../profile/PinTransaccionalSection.tsx';
import { BrebMyKeySection } from '../profile/BrebMyKeySection.tsx';
import { WalletLlaveBrebAviso } from './WalletLlaveBrebAviso.tsx';
import { preparacionWalletValida } from '../profile/preparacionWallet.ts';

type Paso = 'cargando' | 'pin' | 'llave' | 'listo' | 'error';

// El servidor sigue validando PIN/KYC por operación. Esta guía se monta solo
// para la vista Wallet, nunca para una cuenta pública limitada a Perfil.
export function WalletPrimerIngreso({ children, perfil = false, envolver }: {
  children: ReactNode;
  perfil?: boolean;
  envolver?: (contenido: ReactNode, preparando: boolean) => ReactNode;
}) {
  const [paso, setPaso] = useState<Paso>('cargando');
  const [estado, setEstado] = useState<EstadoPin | null>(null);
  const [walletDisponible, setWalletDisponible] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [identidadLlave, setIdentidadLlave] = useState<'cargando' | 'aprobada' | 'pendiente' | 'error'>('cargando');
  const generacion = useRef(0);
  const eleccionLlave = useRef<'OMITIDA' | 'REGISTRADA' | null>(null);

  const cargar = useCallback(async () => {
    const actual = ++generacion.current;
    setPaso('cargando');
    setError(null);
    setWalletDisponible(false);
    eleccionLlave.current = null;
    let walletEncontrada = false;
    try {
      const wallet = await get<{ success: boolean; data: { idWallet: number } }>('/api/wallets/mi-wallet');
      if (actual !== generacion.current) return;
      if (!wallet.success || !wallet.data?.idWallet) throw new Error('No pudimos consultar tu Wallet.');
      walletEncontrada = true;
      setWalletDisponible(true);
      const respuesta = await get<{ success: boolean; data: EstadoPin }>('/api/pin/estado');
      if (actual !== generacion.current) return;
      if (!respuesta.success || !estadoPinValido(respuesta.data)) throw new Error('No pudimos confirmar el estado de tu PIN.');
      try {
        const preparacion = await get<{ success: boolean; data: unknown }>('/api/usuarios/preparacion-wallet');
        if (actual !== generacion.current) return;
        if (!preparacion.success || !preparacionWalletValida(preparacion.data)) throw new Error('No pudimos confirmar tu registro.');
        if (preparacion.data.habilitado && preparacion.data.esRegistroPublico && preparacion.data.walletHabilitada
          && ['OMITIDA', 'REGISTRADA'].includes(preparacion.data.llave.opcion))
          eleccionLlave.current = preparacion.data.llave.opcion as 'OMITIDA' | 'REGISTRADA';
      } catch (err) { if (!(err instanceof ApiError && err.status === 404)) throw err; }
      setEstado(respuesta.data);
      setPaso(!respuesta.data.configurado || respuesta.data.requiereActualizacion ? 'pin' : 'listo');
    } catch (err) {
      if (actual !== generacion.current) return;
      // Sin Wallet no hay una preparación transaccional que exigir; la página
      // existente conserva su mensaje de Wallet no disponible.
      if (!walletEncontrada && err instanceof ApiError && err.status === 404) setPaso('listo');
      else {
        setError('No pudimos consultar la preparación de tu Wallet. Intenta actualizar.');
        setPaso('error');
      }
    }
  }, []);

  useEffect(() => {
    void cargar();
    const actualizar = () => { void cargar(); };
    window.addEventListener('xpay:pin-actualizacion-requerida', actualizar);
    return () => {
      ++generacion.current;
      window.removeEventListener('xpay:pin-actualizacion-requerida', actualizar);
    };
  }, [cargar, perfil]);

  useEffect(() => {
    if (paso !== 'llave') return;
    let cancelado = false;
    setIdentidadLlave('cargando');
    void get<{ success: boolean; data: { estadoKyc: string } }>('/api/kyc/mi-estado').then(r => {
      if (!cancelado) setIdentidadLlave(!r.success ? 'error' : r.data?.estadoKyc === 'APROBADO' ? 'aprobada' : 'pendiente');
    }).catch(() => { if (!cancelado) setIdentidadLlave('error'); });
    return () => { cancelado = true; };
  }, [paso]);

  const actualizado = useCallback((nuevo: EstadoPin) => {
    if (estadoPinValido(nuevo) && nuevo.configurado && !nuevo.requiereActualizacion && !nuevo.bloqueado) {
      setEstado(nuevo);
      setPaso(eleccionLlave.current ? 'listo' : 'llave');
    }
  }, []);

  const mostrar = (contenido: ReactNode) => envolver ? envolver(contenido, paso !== 'listo') : contenido;
  // Perfil y salida siguen disponibles mientras se prepara el PIN.
  if (perfil) return mostrar(children);
  if (paso === 'listo') return mostrar(<>{walletDisponible && eleccionLlave.current !== 'OMITIDA' && <WalletLlaveBrebAviso />}{children}</>);
  if (paso === 'cargando') return mostrar(<p role="status">Consultando la preparación de tu Wallet...</p>);
  if (paso === 'error') return mostrar(<section className="profile-section" aria-label="Preparar mi Wallet">
    <p className="breb-msg-err" role="alert">{error}</p>
    <button type="button" className="btn-breb" onClick={() => void cargar()}>Actualizar preparación</button>
  </section>);

  return mostrar(<div className="wallet-primer-ingreso">
    <h2>Prepara tu Wallet</h2>
    {paso === 'pin' && estado && <>
      <p>{estado.requiereActualizacion ? 'Actualiza tu PIN transaccional a 6 dígitos para continuar.' : 'Crea tu PIN transaccional de 6 dígitos para continuar.'}</p>
      <PinTransaccionalSection estadoInicial={estado} obligatorio onActualizado={actualizado} />
    </>}
    {paso === 'llave' && <>
      <p>Tu PIN de 6 dígitos quedó guardado.</p>
      <p>Para retirar a tu banco necesitas registrar y verificar una llave Bre-B de tu propia cuenta bancaria.
        Puedes hacerlo más tarde en Perfil. La llave no es necesaria para enviar entre Wallets ni pagar con QR.</p>
      {identidadLlave === 'cargando' && <p role="status">Consultando tu verificación de identidad...</p>}
      {identidadLlave === 'aprobada' && <BrebMyKeySection bienvenida identidadAprobada />}
      {identidadLlave === 'pendiente' && <p>Antes de registrar tu llave, completa la verificación de identidad en Mi Wallet. Tu PIN ya quedó guardado.</p>}
      {identidadLlave === 'error' && <p>No pudimos consultar tu verificación de identidad. Puedes continuar a Mi Wallet y actualizar su estado.</p>}
      <button type="button" className="btn-breb" onClick={() => setPaso('listo')}>Continuar a mi Wallet</button>
      <button type="button" className="wallet-send-link-btn" onClick={() => setPaso('listo')}>Registrar más tarde</button>
    </>}
  </div>);
}
