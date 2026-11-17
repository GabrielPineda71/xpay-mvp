import { useEffect, useRef, useState } from 'react';
import QRCode from 'qrcode';
import { get, post, HttpUncertainError } from '../../api/client.ts';
import { fmtDate, fmtMoney } from '../../utils.ts';
import './wallet-recarga-breb.css';

interface RecargaBreb {
  idRecarga: number;
  valor: number;
  fecha: string;
  estado: string;
}

interface RecargaQr {
  idRecargaQr: number;
  referencia: string;
  qrCodeData: string | null;
  estado: string;
  recargas: RecargaBreb[];
}

interface RespuestaRecargaQr {
  success: boolean;
  data: RecargaQr | null;
  message?: string;
}

interface WalletRecargaBrebProps {
  onActualizarSaldo: () => Promise<void>;
}

const ENDPOINT = '/api/wallets/mi-wallet/recarga-breb';

export function WalletRecargaBreb({ onActualizarSaldo }: WalletRecargaBrebProps) {
  const [recarga, setRecarga] = useState<RecargaQr | null>(null);
  const [cargando, setCargando] = useState(true);
  const [generando, setGenerando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [qrSrc, setQrSrc] = useState<string | null>(null);
  const [qrError, setQrError] = useState(false);
  const [intentoMostrar, setIntentoMostrar] = useState(0);
  const solicitud = useRef(0);

  useEffect(() => {
    const actual = ++solicitud.current;
    void get<RespuestaRecargaQr>(ENDPOINT)
      .then(respuesta => {
        if (solicitud.current !== actual) return;
        if (!respuesta.success) throw new Error(respuesta.message ?? 'No se pudo consultar tu QR de recarga.');
        setRecarga(respuesta.data);
      })
      .catch(err => {
        if (solicitud.current === actual) setError((err as Error).message);
      })
      .finally(() => {
        if (solicitud.current === actual) setCargando(false);
      });
    return () => { solicitud.current++; };
  }, []);

  useEffect(() => {
    let cancelado = false;
    setQrSrc(null);
    setQrError(false);
    if (recarga?.qrCodeData) {
      void QRCode.toDataURL(recarga.qrCodeData, { width: 384, margin: 4, errorCorrectionLevel: 'M' })
        .then(src => { if (!cancelado) setQrSrc(src); })
        .catch(() => { if (!cancelado) setQrError(true); });
    }
    return () => { cancelado = true; };
  }, [recarga?.qrCodeData, intentoMostrar]);

  async function consultar(crear: boolean) {
    if (cargando || generando) return;
    const actual = ++solicitud.current;
    setError(null);
    setCargando(true);
    setGenerando(crear);
    try {
      const respuesta = crear
        ? await post<RespuestaRecargaQr>(ENDPOINT, {}, undefined, 45_000)
        : await get<RespuestaRecargaQr>(ENDPOINT);
      if (solicitud.current !== actual) return;
      if (!respuesta.success) throw new Error(respuesta.message ?? 'No se pudo obtener tu QR de recarga.');
      setRecarga(respuesta.data);
      if (!crear) await onActualizarSaldo();
    } catch (err) {
      if (solicitud.current !== actual) return;
      setError(err instanceof HttpUncertainError
        ? 'La generación sigue pendiente de confirmar. Pulsa Actualizar estado para consultar tu QR.'
        : (err as Error).message);
    } finally {
      if (solicitud.current === actual) {
        setCargando(false);
        setGenerando(false);
      }
    }
  }

  return (
    <section className="wallet-recharge" aria-label="Recargar desde mi banco" aria-busy={cargando}>
      <h3 className="wallet-receive-title">Recargar desde mi banco</h3>
      <p>Escanea este QR desde la app de tu banco e indica el valor que deseas recargar.</p>
      <p>El pago se recibe en XPAY y se abona a tu Wallet cuando el banco lo confirme.</p>

      {qrSrc && (
        <div className="wallet-recharge-qr">
          <img src={qrSrc} alt="QR Bre-B para recargar mi Wallet desde mi banco" width={384} height={384} />
          <a className="wallet-receive-download-btn" href={qrSrc} download="xpay-recarga-breb.png">
            Descargar QR PNG
          </a>
          <p>Si tu banco permite cargar imágenes, puedes usar el QR descargado desde este mismo celular.</p>
        </div>
      )}
      {recarga?.qrCodeData && !qrSrc && !qrError && <p role="status">Preparando el QR…</p>}
      {qrError && (
        <div role="alert">
          <p>No se pudo mostrar el QR.</p>
          <button type="button" className="wallet-send-secondary-btn" onClick={() => setIntentoMostrar(intento => intento + 1)}>
            Reintentar mostrar QR
          </button>
        </div>
      )}
      {cargando && <p role="status">{generando ? 'Generando tu QR…' : 'Consultando recargas…'}</p>}
      {error && <p role="alert" className="wallet-receive-error">{error}</p>}
      {!recarga?.qrCodeData && !cargando && !error && (
        <button type="button" className="wallet-receive-generate-btn" onClick={() => void consultar(true)}>
          Generar mi QR de recarga
        </button>
      )}
      <button type="button" className="wallet-send-secondary-btn" disabled={cargando}
        onClick={() => void consultar(false)}>
        Actualizar estado
      </button>

      {!!recarga?.recargas.length && (
        <div className="wallet-recharge-history">
          <h4>Últimas recargas</h4>
          <ul>
            {recarga.recargas.map(pago => (
              <li key={pago.idRecarga}>
                <strong>{fmtMoney(pago.valor)}</strong>
                <span>{fmtDate(pago.fecha)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}
