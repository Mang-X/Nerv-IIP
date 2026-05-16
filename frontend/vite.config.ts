import { fileURLToPath, URL } from 'node:url'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite-plus'

export default defineConfig({
  fmt: {
    semi: false,
    singleQuote: true,
    ignorePatterns: [
      'apps/console/dist/**',
      'apps/console/typed-router.d.ts',
      'packages/api-client/openapi/**',
      'packages/api-client/src/generated/**',
    ],
  },
  plugins: [Vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./apps/console/src', import.meta.url)),
      '@nerv-iip/api-client': fileURLToPath(
        new URL('./packages/api-client/src/index.ts', import.meta.url),
      ),
      '@nerv-iip/app-shell': fileURLToPath(
        new URL('./packages/app-shell/src/index.ts', import.meta.url),
      ),
      '@nerv-iip/ui': fileURLToPath(new URL('./packages/ui/src/index.ts', import.meta.url)),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
  },
  lint: {
    ignorePatterns: [
      'apps/console/dist/**',
      'apps/console/typed-router.d.ts',
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
        ],
        output: ['packages/api-client/src/generated/**'],
      },
      'workspace:typecheck': {
        command: 'pnpm -r --if-present typecheck',
        input: [
          'apps/**/src/**',
          'apps/**/tsconfig.json',
          'apps/**/typed-router.d.ts',
          'packages/**/src/**',
          'packages/**/tsconfig.json',
          'tsconfig.base.json',
        ],
      },
      'workspace:build': {
        command: 'pnpm --filter @nerv-iip/console build',
        dependsOn: ['workspace:typecheck'],
        input: [
          'apps/console/index.html',
          'apps/console/src/**',
          'apps/console/tsconfig.json',
          'apps/console/vite.config.ts',
          'apps/console/typed-router.d.ts',
          'packages/api-client/src/**',
          'packages/app-shell/src/**',
          'packages/ui/src/**',
          'tsconfig.base.json',
        ],
        output: ['apps/console/dist/**'],
      },
    },
  },
})
