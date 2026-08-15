import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite-plus'
import VueRouter from 'vue-router/vite'

// `components/ui/` 是 shadcn 原版冻结区（byte-for-byte 零改动），因此整体排除 fmt + lint；
// 但其中混住着若干 Nerv 自研 / 自行补丁过的受治理源码，必须逐条 negation 重新纳管，
// 否则它们会静默豁免格式与 lint 门禁（NERV-794）。
//
// 写法约束，改动前务必读完，不要「简化」：
// 1. 基础模式必须是 `**/*` 而不是 `**`：`**` 会同时匹配目录条目本身，
//    被排除的目录不会再被遍历，后续 negation 永远没有机会命中里面的文件。
// 2. 每个重新纳管的目录需要**两条** negation：先放开目录条目（结尾带 `/`）
//    以恢复遍历，再放开目录内递归文件。只写其中一条都会把目录重新冻住。
// 3. 单文件纳管同理：先放开其所在目录条目，再放开该文件本身
//    （`sonner/` 只有 `index.ts` 是 Nerv 补丁，`Sonner.vue` 仍是原版，保持冻结）。
const frozenShadcnSourceIgnorePatterns = [
  'packages/ui/src/components/ui/**/*',
  // 自研目录：中文产品文案 + 自带单测，非 shadcn 原版
  '!packages/ui/src/components/ui/file-preview/',
  '!packages/ui/src/components/ui/file-preview/**',
  '!packages/ui/src/components/ui/file-upload/',
  '!packages/ui/src/components/ui/file-upload/**',
  '!packages/ui/src/components/ui/date-picker/',
  '!packages/ui/src/components/ui/date-picker/**',
  // 原版目录中的单个 Nerv 补丁文件：仅 barrel 被改写（显式引入 vue-sonner 样式）
  '!packages/ui/src/components/ui/sonner/',
  '!packages/ui/src/components/ui/sonner/index.ts',
] as const

export default defineConfig({
  fmt: {
    semi: false,
    singleQuote: true,
    ignorePatterns: [
      'apps/console/dist/**',
      'apps/console/typed-router.d.ts',
      'apps/business-console/dist/**',
      'apps/business-console/typed-router.d.ts',
      'apps/docs/docs/.vitepress/dist/**',
      'packages/api-client/openapi/**',
      'packages/api-client/src/generated/**',
      ...frozenShadcnSourceIgnorePatterns,
      'packages/scheduling/vendor/dhtmlx/**',
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
      'apps/docs/docs/.vitepress/dist/**',
      'packages/api-client/src/generated/**',
      ...frozenShadcnSourceIgnorePatterns,
      'packages/scheduling/vendor/dhtmlx/**',
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
          // Exclude build outputs under the same tree: they are rewritten by every
          // workspace:build and would self-poison this cache into perpetual misses.
          'apps/**/docs/.vitepress/**',
          '!apps/**/docs/.vitepress/dist/**',
          '!apps/**/docs/.vitepress/cache/**',
          'packages/**/src/**',
          'packages/**/tsconfig.json',
          'packages/**/vite.config.ts',
          'tsconfig.base.json',
        ],
      },
      'workspace:test': {
        command: 'pnpm -r --if-present test',
        input: [
          'apps/**/src/**',
          'apps/docs/docs/**',
          '!apps/docs/docs/.vitepress/dist/**',
          '!apps/docs/docs/.vitepress/cache/**',
          'apps/**/vite.config.ts',
          'packages/**/src/**',
          'packages/**/tsconfig.json',
          'tsconfig.base.json',
        ],
      },
      'workspace:build': {
        command:
          'pnpm --filter @nerv-iip/console --filter @nerv-iip/business-console --filter @nerv-iip/business-pda --filter @nerv-iip/screen --filter @nerv-iip/design-system --filter @nerv-iip/docs build',
        dependsOn: ['workspace:typecheck'],
        input: [
          'apps/console/index.html',
          'apps/console/package.json',
          'apps/console/src/**',
          'apps/console/tsconfig.json',
          'apps/console/vite.config.ts',
          'apps/console/typed-router.d.ts',
          'apps/business-console/index.html',
          'apps/business-console/package.json',
          'apps/business-console/src/**',
          'apps/business-console/tsconfig.json',
          'apps/business-console/vite.config.ts',
          'apps/business-console/typed-router.d.ts',
          'apps/business-pda/index.html',
          'apps/business-pda/package.json',
          'apps/business-pda/src/**',
          'apps/business-pda/tsconfig.json',
          'apps/business-pda/vite.config.ts',
          'apps/business-pda/typed-router.d.ts',
          'apps/screen/index.html',
          'apps/screen/package.json',
          'apps/screen/src/**',
          'apps/screen/tsconfig.json',
          'apps/screen/vite.config.ts',
          'apps/screen/typed-router.d.ts',
          // design-system is a production VitePress docs site; build it under the
          // root gate so dead links / SSR / VitePress-Rolldown breakage surface in
          // CI. It consumes docs/** (theme + config + markdown) and both UI pkgs.
          // Exclude both VitePress apps' own build outputs (they are listed in
          // `output` below): leaving them inside the input globs self-poisons the
          // cache — every build rewrites its own inputs, so it never hits.
          'apps/design-system/docs/**',
          '!apps/design-system/docs/.vitepress/dist/**',
          '!apps/design-system/docs/.vitepress/cache/**',
          'apps/design-system/package.json',
          'apps/design-system/tsconfig.json',
          'apps/docs/docs/**',
          '!apps/docs/docs/.vitepress/dist/**',
          '!apps/docs/docs/.vitepress/cache/**',
          'apps/docs/package.json',
          'apps/docs/tsconfig.json',
          'packages/api-client/src/**',
          'packages/app-shell/src/**',
          'packages/auth/src/**',
          'packages/business-core/src/**',
          'packages/ui/src/**',
          'packages/ui-mobile/src/**',
          'tsconfig.base.json',
        ],
        output: [
          'apps/console/dist/**',
          'apps/business-console/dist/**',
          'apps/business-pda/dist/**',
          'apps/screen/dist/**',
          'apps/design-system/docs/.vitepress/dist/**',
          'apps/docs/docs/.vitepress/dist/**',
        ],
      },
    },
  },
})
