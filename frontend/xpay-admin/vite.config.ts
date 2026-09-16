/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// XPAY-375 — se agrega bloque `test` (Vitest) por primera vez en este
// proyecto: no existía ninguna infraestructura de tests de frontend antes
// de este ticket. jsdom como entorno (necesario para renderizar componentes
// React en tests) — no afecta el build de producción (`vite build`), que
// ignora este bloque.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    globals: true,
  },
})
