import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { ApiError, get } from '../../api/client.ts';
import { estadoPinValido, type EstadoPin } from '../../utils/pinTransaccional.ts';
import { PinTransaccionalSection } from '../profile/PinTransaccionalSection.tsx';

type Paso = 'cargando' | 'pin' | 'listo' | 'error';

// El servidor sigue validando PIN/KYC por operación. Esta guía se monta solo
// para la vista Wallet, nunca para una cuenta pública limitada a Perfil.
export function WalletPrimerIngreso({ children, perfil = false, envolver }: {
  children: ReactNode;
  perfil?: boolean;
  envolver?: (contenido: ReactNode, preparando: boolean) => ReactNode;
}) {
  const [paso, setPaso] = useState<Paso>('cargando');
  const [estado, setEstado] = useState<EstadoPin | null>(null);
  const [error, setError] = useState<string | null>(null);
  const generacion = useRef(0);

  const cargar = useCallback(async () => {
    const actual = ++generacion.current;
    setPaso('cargando');
    setError(null);
    let walletEncontrada = false;
    try {
      const wallet = await get<{ success: boolean; data: { idWallet: number } }>('/api/wallets/mi-wallet');
      if (actual !== generacion.current) return;
      if (!wallet.success || !wallet.data?.idWallet) throw new Error('No pudimos consultar tu Wallet.');
      walletEncontrada = true;
      const respuesta = await get<{ success: boolean; data: EstadoPin }>('/api/pin/estado');
      if (actual !== generacion.current) return;
      if (!respuesta.success || !estadoPinValido(respuesta.data)) throw new Error('No pudimos confirmar el estado de tu PIN.');
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

  const actualizado = useCallback((nuevo: EstadoPin) => {
    if (estadoPinValido(nuevo) && nuevo.configurado && !nuevo.requiereActualizacion && !nuevo.bloqueado) {
      setEstado(nuevo);
      setPaso('listo');
    }
  }, []);

  const mostrar = (contenido: ReactNode) => envolver ? envolver(contenido, paso !== 'listo') : contenido;
  // Perfil y salida siguen disponibles mientras se prepara el PIN.
  if (perfil) return mostrar(children);
  if (paso === 'listo') return mostrar(children);
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
  </div>);
}
