import { existsSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite-plus'
import VueRouter from 'vue-router/vite'

// DHTMLX 试用包为可选依赖。若未通过私有源安装(node_modules)且无本地 vendor 拷贝,
// 则把 `@dhx/trial-gantt` 别名到 stub,保证 dev/build 在无许可时不失败(回落 NativeEngine)。
const dhxInstalled = existsSync(
  fileURLToPath(new URL('../../node_modules/@dhx/trial-gantt/package.json', import.meta.url)),
)
const dhxVendor = fileURLToPath(
  new URL('../../packages/scheduling/vendor/dhtmlx/dhtmlxgantt.es.js', import.meta.url),
)
const dhxCssVendor = fileURLToPath(
  new URL('../../packages/scheduling/vendor/dhtmlx/dhtmlxgantt.css', import.meta.url),
)
const dhxStub = fileURLToPath(
  new URL('../../packages/scheduling/src/engine/dhtmlx/stub.ts', import.meta.url),
)
const dhxCssStub = fileURLToPath(
  new URL('../../packages/scheduling/src/engine/dhtmlx/empty.css', import.meta.url),
)
const dhxAlias = dhxInstalled
  ? {}
  : {
      // 更具体的 css 子路径必须排在前面:Vite 字符串 alias 是前缀匹配,否则会被 '@dhx/trial-gantt' 劫持。
      '@dhx/trial-gantt/codebase/dhtmlxgantt.css': existsSync(dhxCssVendor) ? dhxCssVendor : dhxCssStub,
      '@dhx/trial-gantt': existsSync(dhxVendor) ? dhxVendor : dhxStub,
    }

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
      '@nerv-iip/scheduling': fileURLToPath(
        new URL('../../packages/scheduling/src/index.ts', import.meta.url),
      ),
      ...dhxAlias,
    },
  },
  server: {
    port: 5125,
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
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
