import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

// Test-only config: @nerv-iip/app-shell composes Vue SFCs from @nerv-iip/ui,
// so the official Vitest runner needs the Node-backed Vue compiler plugin.
export default defineConfig({
  plugins: [Vue()],
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
