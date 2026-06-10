import { fileURLToPath, URL } from 'node:url'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

// Test-only config: @nerv-iip/scheduling ships no bundle; its unit/contract/component
// tests run under the workspace `vp test` runner (vitest-compatible).
export default defineConfig({
  plugins: [Vue()],
  resolve: {
    alias: {
      // 测试环境没有 DHTMLX 试用包:别名到 stub,使 import('@dhx/trial-gantt') 可解析,
      // isDhtmlxAvailable() 返回 false → DhtmlxEngine 契约测试 skip。真实渲染由 Playwright 验证。
      '@dhx/trial-gantt': fileURLToPath(new URL('./src/engine/dhtmlx/stub.ts', import.meta.url)),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
