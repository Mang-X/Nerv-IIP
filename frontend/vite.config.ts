import { fileURLToPath, URL } from 'node:url'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [Vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./apps/console/src', import.meta.url)),
      '@nerv-iip/api-client': fileURLToPath(new URL('./packages/api-client/src/index.ts', import.meta.url)),
      '@nerv-iip/app-shell': fileURLToPath(new URL('./packages/app-shell/src/index.ts', import.meta.url)),
      '@nerv-iip/ui': fileURLToPath(new URL('./packages/ui/src/index.ts', import.meta.url)),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
