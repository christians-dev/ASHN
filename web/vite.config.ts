/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Aspire passes the API address as a service-discovery variable; fall back to the API's fixed dev port.
const apiTarget =
  process.env.services__api__https__0 ??
  process.env.services__api__http__0 ??
  'https://localhost:7179'

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.PORT ?? 5173),
    // Same-origin in the browser: Vite forwards API and media calls to the .NET host (research R2).
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true, secure: false },
      '/media': { target: apiTarget, changeOrigin: true, secure: false },
    },
  },
  build: {
    outDir: 'dist',
  },
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['tests/unit/**/*.test.{ts,tsx}'],
    setupFiles: ['tests/unit/setup.ts'],
  },
})
