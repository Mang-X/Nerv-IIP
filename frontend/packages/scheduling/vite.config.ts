import { fileURLToPath, URL } from 'node:url'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

// Test-only config: @nerv-iip/scheduling ships no bundle; its unit/contract/component
// tests run under the workspace `vp test` runner (vitest-compatible).
export default defineConfig({
  plugins: [Vue()],
  resolve: {
    alias: {
      // css 子路径在前(Vite 字符串 alias 前缀匹配,避免被 '@dhx/trial-gantt' 劫持)。
      '@dhx/trial-gantt/codebase/dhtmlxgantt.css': fileURLToPath(
        new URL('./vendor/dhtmlx/dhtmlxgantt.css', import.meta.url),
      ),
      '@dhx/trial-gantt': fileURLToPath(
        new URL('./vendor/dhtmlx/dhtmlxgantt.es.js', import.meta.url),
      ),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
