import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

// Test-only config: @nerv-iip/ui-mobile ships no bundle, but its component tests
// run under the workspace `vp test` runner (vitest-compatible) and need SFC support.
export default defineConfig({
  plugins: [Vue()],
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
