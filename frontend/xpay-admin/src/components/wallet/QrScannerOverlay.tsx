import { useEffect } from 'react';
import { createPortal } from 'react-dom';

function IconClose({ size = 22 }: { size?: number }) {
  return (
    <svg
      width={size} height={size} viewBox="0 0 24 24" aria-hidden="true"
      fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"
    >
      <path d="M6 6l12 12M18 6 6 18" />
    </svg>
  );
}

interface QrScannerOverlayProps {
  // ID del contenedor donde html5-qrcode inyecta el vídeo — el mismo que
  // useQrScanner recibe como elementId ('env-qr-reader' / 'pag-qr-reader').
  readerId: string;
  title: string;
  onClose: () => void;
}

// Lector QR a pantalla completa — presentación pura. La cámara la abre y la
// cierra useQrScanner (UserWalletPage); este componente solo aporta el
// contenedor del vídeo, el marco de lectura y el botón de cierre. Se monta en
// document.body con un portal para escapar del layout del wallet shell.
// Cerrar o leer un QR solo cambia el estado de escaneo del padre: nunca
// ejecuta un pago ni una transferencia.
export function QrScannerOverlay({ readerId, title, onClose }: QrScannerOverlayProps) {
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  // Bloquea el scroll del fondo mientras la cámara ocupa la pantalla y
  // restaura el valor previo al cerrar.
  useEffect(() => {
    const previo = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = previo; };
  }, []);

  return createPortal(
    <div className="qr-fullscreen" role="dialog" aria-modal="true" aria-label={title}>
      <div id={readerId} className="qr-fullscreen-reader" />
      <div className="qr-fullscreen-frame" aria-hidden="true" />
      <div className="qr-fullscreen-header">
        <p className="qr-fullscreen-title">{title}</p>
        <button className="qr-fullscreen-close" type="button" onClick={onClose} aria-label="Cerrar escáner">
          <IconClose />
        </button>
      </div>
      <p className="qr-fullscreen-hint">Centra el código QR dentro del marco</p>
    </div>,
    document.body,
  );
}
