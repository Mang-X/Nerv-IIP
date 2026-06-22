import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite-plus'
import VueRouter from 'vue-router/vite'

export default defineConfig({
  fmt: {
    semi: false,
    singleQuote: true,
    ignorePatterns: [
      'apps/console/dist/**',
      'apps/console/typed-router.d.ts',
      'apps/business-console/dist/**',
      'apps/business-console/typed-router.d.ts',
      'packages/api-client/openapi/**',
      'packages/api-client/src/generated/**',
    ],
  },
  plugins: [
    tailwindcss(),
    VueRouter({
      root: fileURLToPath(new URL('.', import.meta.url)),
      routesFolder: [
        {
          src: fileURLToPath(new URL('./apps/console/src/pages', import.meta.url)),
          exclude: (excluded) =>
            excluded.concat([
              '**/components/**/*',
              '**/dialogs/**/*',
              '**/drawers/**/*',
              '**/fragments/**/*',
            ]),
        },
      ],
      dts: fileURLToPath(new URL('./apps/console/typed-router.d.ts', import.meta.url)),
    }),
    Vue(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./apps/console/src', import.meta.url)),
      '@nerv-iip/api-client': fileURLToPath(
        new URL('./packages/api-client/src/index.ts', import.meta.url),
      ),
      '@nerv-iip/app-shell': fileURLToPath(
        new URL('./packages/app-shell/src/index.ts', import.meta.url),
      ),
      '@nerv-iip/auth': fileURLToPath(new URL('./packages/auth/src/index.ts', import.meta.url)),
      '@nerv-iip/ui': fileURLToPath(new URL('./packages/ui/src/index.ts', import.meta.url)),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: [fileURLToPath(new URL('./apps/console/src/test/setup.ts', import.meta.url))],
  },
  lint: {
    ignorePatterns: [
      'apps/console/dist/**',
      'apps/console/typed-router.d.ts',
      'apps/business-console/dist/**',
      'apps/business-console/typed-router.d.ts',
      'packages/api-client/src/generated/**',
    ],
  },
  run: {
    cache: {
      tasks: true,
      scripts: false,
    },
    tasks: {
      'workspace:generate-api': {
        command: 'pnpm --filter @nerv-iip/api-client generate',
        input: [
          'packages/api-client/openapi-ts.config.ts',
          'packages/api-client/openapi/platform-gateway.v1.json',
          'packages/api-client/openapi/business-gateway-console.v1.json',
        ],
        output: ['packages/api-client/src/generated/**'],
      },
      'workspace:typecheck': {
        command: 'pnpm -r --if-present typecheck',
        input: [
          'apps/**/src/**',
          'apps/**/tsconfig.json',
          'apps/**/typed-router.d.ts',
          // design-system's typechecked sources live under docs/.vitepress (theme
          // + config + showcase), not src/** — include them so editing the docs
          // app invalidates the typecheck cache instead of showing a stale green.
          'apps/**/docs/.vitepress/**',
          'packages/**/src/**',
          'packages/**/tsconfig.json',
          'tsconfig.base.json',
        ],
      },
      'workspace:test': {
        command: 'pnpm -r --if-present test',
        input: [
          'apps/**/src/**',
          'apps/**/vite.config.ts',
          'packages/**/src/**',
          'packages/**/tsconfig.json',
          'tsconfig.base.json',
        ],
      },
      'workspace:build': {
        command:
          'pnpm --filter @nerv-iip/console --filter @nerv-iip/business-console --filter @nerv-iip/design-system build',
        dependsOn: ['workspace:typecheck'],
        input: [
          'apps/console/index.html',
          'apps/console/src/**',
          'apps/console/tsconfig.json',
          'apps/console/vite.config.ts',
          'apps/console/typed-router.d.ts',
          'apps/business-console/index.html',
          'apps/business-console/src/**',
          'apps/business-console/tsconfig.json',
          'apps/business-console/vite.config.ts',
          'apps/business-console/typed-router.d.ts',
          // design-system is a production VitePress docs site; build it under the
          // root gate so dead links / SSR / VitePress-Rolldown breakage surface in
          // CI. It consumes docs/** (theme + config + markdown) and both UI pkgs.
          'apps/design-system/docs/**',
          'apps/design-system/package.json',
          'apps/design-system/tsconfig.json',
          'packages/api-client/src/**',
          'packages/app-shell/src/**',
          'packages/auth/src/**',
          'packages/ui/src/**',
          'packages/ui-mobile/src/**',
          'tsconfig.base.json',
        ],
        output: [
          'apps/console/dist/**',
          'apps/business-console/dist/**',
          'apps/design-system/docs/.vitepress/dist/**',
        ],
      },
    },
  },
})
