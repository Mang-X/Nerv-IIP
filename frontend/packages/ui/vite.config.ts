import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'
import wasm from 'vite-plugin-wasm'

// Test-only config: @nerv-iip/ui ships no bundle, but its contract tests + a few
// component tests run under the workspace `vp test` runner (vitest-compatible).
export default defineConfig({
  plugins: [wasm(), Vue()],
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
