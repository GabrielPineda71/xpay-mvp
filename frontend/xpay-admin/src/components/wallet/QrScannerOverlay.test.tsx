import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { QrScannerOverlay } from './QrScannerOverlay.tsx';

describe('QrScannerOverlay', () => {
  it('se monta en document.body, fuera del contenedor del padre, con el id del lector', () => {
    const { container } = render(
      <QrScannerOverlay readerId="env-qr-reader" title="Escanea el QR del receptor" onClose={() => {}} />,
    );
    const dialog = screen.getByRole('dialog', { name: 'Escanea el QR del receptor' });
    expect(dialog.parentElement).toBe(document.body);
    expect(container.contains(dialog)).toBe(false);
    expect(document.getElementById('env-qr-reader')).not.toBeNull();
    expect(dialog).toHaveClass('qr-fullscreen');
  });

  it('el botón Cerrar escáner llama a onClose', () => {
    const onClose = vi.fn();
    render(<QrScannerOverlay readerId="pag-qr-reader" title="Escanea el QR del comercio" onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: 'Cerrar escáner' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('Escape llama a onClose', () => {
    const onClose = vi.fn();
    render(<QrScannerOverlay readerId="pag-qr-reader" title="Escanea el QR del comercio" onClose={onClose} />);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('bloquea el scroll del body mientras está abierto y restaura el valor previo al cerrar', () => {
    document.body.style.overflow = 'auto';
    const { unmount } = render(
      <QrScannerOverlay readerId="env-qr-reader" title="Escanea el QR del receptor" onClose={() => {}} />,
    );
    expect(document.body.style.overflow).toBe('hidden');
    unmount();
    expect(document.body.style.overflow).toBe('auto');
    document.body.style.overflow = '';
  });
});
