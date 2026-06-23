import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'
import VueRouter from 'vue-router/vite'

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
      '@nerv-iip/ui': fileURLToPath(new URL('../../packages/ui/src/index.ts', import.meta.url)),
      '@nerv-iip/ui-mobile': fileURLToPath(
        new URL('../../packages/ui-mobile/src/index.ts', import.meta.url),
      ),
    },
  },
  server: {
    port: 5180,
  },
})
