import { existsSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite-plus'
import VueRouter from 'vue-router/vite'

// DHTMLX 试用包为可选依赖。若未通过私有源安装(node_modules)且无本地 vendor 拷贝,
// 则把 `@dhx/trial-gantt` 别名到 stub,保证 dev/build 在无许可时不失败(排程组件此时显示占位)。
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
// 测试环境(vitest/jsdom)无论 vendor/私有源是否可用都走 stub:jsdom 承载不了真实 DHTMLX
// 渲染,组件树应稳定落到 readonly-schedule-timeline 降级路径,断言才有确定性;真实引擎
// 渲染由 Playwright/浏览器验证。做法与 packages/scheduling/vite.config.ts 的测试别名同款。
const dhxAlias = process.env.VITEST
  ? {
      // 更具体的 css 子路径必须排在前面:Vite 字符串 alias 是前缀匹配,否则会被 '@dhx/trial-gantt' 劫持。
      '@dhx/trial-gantt/codebase/dhtmlxgantt.css': dhxCssStub,
      '@dhx/trial-gantt': dhxStub,
    }
  : dhxInstalled
    ? {}
    : {
        // 更具体的 css 子路径必须排在前面:Vite 字符串 alias 是前缀匹配,否则会被 '@dhx/trial-gantt' 劫持。
        '@dhx/trial-gantt/codebase/dhtmlxgantt.css': existsSync(dhxCssVendor)
          ? dhxCssVendor
          : dhxCssStub,
        '@dhx/trial-gantt': existsSync(dhxVendor) ? dhxVendor : dhxStub,
      }

/**
 * DHTMLX 基础样式表头部有 6 条 `@font-face`,`src` 直指 `https://fonts.gstatic.com/…`(Inter
 * 300–800)。私有化 / 离线部署取不到,字体静默降级到 Helvetica/Arial;现场网络抖动则是加载
 * 中途换字体的闪烁。这是交付物里唯一的外网强依赖,必须剥掉(#1399 M3)。
 *
 * 做成构建期 transform 而不是手改 vendor 文件,理由:vendor 产物是**要被整体替换**的
 * (买到正式 license 就换一份 dhtmlxgantt.css),手改会在下次替换时被无声还原。按文件名匹配
 * 而不是按 vendor 绝对路径,私有源安装的 `@dhx/trial-gantt` 走同一条剥离。
 *
 * 配套:`packages/scheduling/src/styles/scheduling.css` 已把 `--dhx-gantt-font-family` 重映射
 * 到 `var(--font-sans)`,所以剥掉这 6 条之后甘特用的是控制台同一套字体栈(含中文字族),
 * 不存在"剥完没字体"的问题。
 */
const stripDhxRemoteFonts = {
  name: 'nerv-dhx-strip-remote-fonts',
  enforce: 'pre' as const,
  transform(code: string, id: string) {
    if (!id.split('?')[0].endsWith('dhtmlxgantt.css')) return null
    const stripped = code.replace(/@font-face\s*\{[^{}]*fonts\.gstatic\.com[^{}]*\}/g, '')
    return stripped === code ? null : { code: stripped, map: null }
  },
}

const configuredPort = process.env.NERV_IIP_VITE_PORT
const port = Number(configuredPort ?? '5125')
if (!Number.isInteger(port) || port < 1 || port > 65_535) {
  throw new Error(
    `NERV_IIP_VITE_PORT must be an integer from 1 through 65535; received '${configuredPort}'.`,
  )
}

export default defineConfig({
  plugins: [
    stripDhxRemoteFonts,
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
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
