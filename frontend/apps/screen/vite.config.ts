import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite-plus'
import VueRouter from 'vue-router/vite'

const configuredPort = process.env.NERV_IIP_VITE_PORT
const port = Number(configuredPort ?? '5128')
if (!Number.isInteger(port) || port < 1 || port > 65_535) {
  throw new Error(`NERV_IIP_VITE_PORT must be an integer from 1 through 65535; received '${configuredPort}'.`)
}

export default defineConfig({
  plugins: [
    tailwindcss(),
    VueRouter({
      routesFolder: [
        {
          src: 'src/pages',
          exclude: (excluded) => excluded.concat(['**/components/**/*']),
        },
      ],
      dts: 'typed-router.d.ts',
    }),
    Vue(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@nerv-iip/api-client': fileURLToPath(new URL('../../packages/api-client/src/index.ts', import.meta.url)),
      '@nerv-iip/auth': fileURLToPath(new URL('../../packages/auth/src/index.ts', import.meta.url)),
      '@nerv-iip/ui': fileURLToPath(new URL('../../packages/ui/src/index.ts', import.meta.url)),
    },
  },
  server: {
    port,
    strictPort: true,
    proxy: {
      '/api/business-console': {
        target: process.env.NERV_IIP_BUSINESS_GATEWAY_URL ?? 'http://127.0.0.1:5119',
        changeOrigin: true,
      },
      '/api/console': {
        target: process.env.NERV_IIP_PLATFORM_GATEWAY_URL ?? 'http://127.0.0.1:5100',
        changeOrigin: true,
      },
    },
  },
  test: { globals: true, environment: 'jsdom', setupFiles: ['./src/test/setup.ts'] },
})
