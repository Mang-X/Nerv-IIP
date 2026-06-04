import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

// Test-only config: @nerv-iip/ui ships no bundle, but its contract tests + a few
// component tests run under the workspace `vp test` runner (vitest-compatible).
export default defineConfig({
  plugins: [Vue()],
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
