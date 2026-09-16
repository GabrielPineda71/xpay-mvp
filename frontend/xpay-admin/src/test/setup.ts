// XPAY-375 — setup global de Vitest. Sólo extiende expect con los
// matchers de @testing-library/jest-dom (toBeInTheDocument, etc.) — nada
// de red real ni mocks globales aquí, cada test mockea explícitamente lo
// que necesita.
import '@testing-library/jest-dom/vitest';
