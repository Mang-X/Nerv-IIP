import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'
import VueRouter from 'vue-router/vite'

const port = Number(process.env.PORT ?? '5105')

export default defineConfig({
  plugins: [
    tailwindcss(),
    VueRouter({
      routesFolder: [
        {
          src: 'src/pages',
          exclude: (excluded) =>
            excluded.concat([
              '**/components/**/*',
              '**/dialogs/**/*',
              '**/drawers/**/*',
              '**/fragments/**/*',
            ]),
        },
      ],
      dts: 'typed-router.d.ts',
    }),
    Vue(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@nerv-iip/api-client': fileURLToPath(
        new URL('../../packages/api-client/src/index.ts', import.meta.url),
      ),
      '@nerv-iip/app-shell': fileURLToPath(
        new URL('../../packages/app-shell/src/index.ts', import.meta.url),
      ),
      '@nerv-iip/ui': fileURLToPath(new URL('../../packages/ui/src/index.ts', import.meta.url)),
    },
  },
  server: {
    port,
    strictPort: true,
    proxy: {
      '/api': {
        target: process.env.NERV_IIP_GATEWAY_URL ?? 'http://127.0.0.1:5100',
        changeOrigin: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
