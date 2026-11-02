/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// XPAY-375 — se agrega bloque `test` (Vitest) por primera vez en este
// proyecto: no existía ninguna infraestructura de tests de frontend antes
// de este ticket. jsdom como entorno (necesario para renderizar componentes
// React en tests) — no afecta el build de producción (`vite build`), que
// ignora este bloque.
export default defineConfig(({ command, mode }) => {
  // Todo build declara su ambiente (QA o PROD): separa los QR y las
  // leyendas de cada ambiente (src/config/ambiente.ts). Sin él, no compila.
  if (command === 'build') {
    const ambiente = loadEnv(mode, '.', 'VITE_').VITE_XPAY_ENV
    if (ambiente !== 'QA' && ambiente !== 'PROD') {
      throw new Error('VITE_XPAY_ENV debe ser QA o PROD para compilar.')
    }
  }
  return {
    plugins: [react()],
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      globals: true,
    },
  }
})
