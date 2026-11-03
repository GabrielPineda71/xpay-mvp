import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SoloQa } from './SoloQa.tsx';

describe('SoloQa', () => {
  afterEach(() => { vi.unstubAllEnvs(); });

  it('muestra su contenido en QA', () => {
    vi.stubEnv('VITE_XPAY_ENV', 'QA');
    render(<SoloQa><p>Ambiente QA/Demo</p></SoloQa>);
    expect(screen.getByText('Ambiente QA/Demo')).toBeInTheDocument();
  });

  it('en PROD no muestra nada', () => {
    vi.stubEnv('VITE_XPAY_ENV', 'PROD');
    const { container } = render(<SoloQa><p>Ambiente QA/Demo</p></SoloQa>);
    expect(container).toBeEmptyDOMElement();
  });
});
