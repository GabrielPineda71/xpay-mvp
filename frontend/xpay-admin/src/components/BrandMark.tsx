interface BrandMarkProps {
  variant: 'logo' | 'isotipo';
  theme: 'teal' | 'white';
  className?: string;
}

// Marca oficial XPAY — 4 archivos en public/brand/, cada uno ya completo
// (logo = símbolo + palabra "XPAY" horneada en el PNG; isotipo = símbolo
// solo), en versión teal o blanca. Este componente NO construye nada por
// texto/React — solo elige y renderiza el PNG correspondiente.
//
// public/brand/xpay-logo-teal.png · xpay-logo-white.png
// public/brand/xpay-isotipo-teal.png · xpay-isotipo-white.png
//
// El logo completo es horizontal — se controla SIEMPRE por ancho (width,
// vía className del contexto de uso), con height:auto (regla base
// .wallet-brand-mark en src/wallet-shell.css), nunca por un tamaño
// cuadrado.
export function BrandMark({ variant, theme, className }: BrandMarkProps) {
  return (
    <img
      src={`/brand/xpay-${variant}-${theme}.png`}
      alt="XPAY"
      className={`wallet-brand-mark${className ? ` ${className}` : ''}`}
    />
  );
}
