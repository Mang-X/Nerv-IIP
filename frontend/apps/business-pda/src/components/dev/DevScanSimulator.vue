<script setup lang="ts">
import { ref } from 'vue'
import { ScanLine, X } from 'lucide-vue-next'
import { injectScanKeystrokes } from './dev-scan-injection'

/**
 * Dev-only 悬浮「模拟扫码」按钮（方案 §4.1 尾注的 M2 项）。
 *
 * 供人工浏览器走查在无实体扫码枪时注码：点开面板 → 输入 / 选预设 → 「注码」按
 * 扫码枪时序向 document 派发 keydown 字符流 + Enter 后缀，真实驱动 ScanBar 的
 * document 级缓冲路径（见 ./dev-scan-injection.ts 的契约说明）。
 *
 * 挂载点在 App.vue，以 `import.meta.env.DEV` 动态 import 门控 —— 生产构建整体
 * 树摇，不进包。本组件自身不再重复门控。
 */

const open = ref(false)
const code = ref('')
const injecting = ref(false)
const panelEl = ref<HTMLElement>()
const codeInputEl = ref<HTMLInputElement>()

/** 走查常用预设码（与 seed 的工单 / 库位命名风格一致，可直接改输入框内容微调）。 */
const PRESET_CODES = ['WO-2026-0001', 'LOC-A-01-01']

async function inject(value: string) {
  const trimmed = value.trim()
  if (!trimmed || injecting.value) return
  injecting.value = true
  try {
    await injectScanKeystrokes(trimmed)
  } finally {
    injecting.value = false
  }
}

// 焦点保卫：扫码页的 ScanBar 是焦点常驻组件（blur → RAF 回抢），真实浮层靠页面
// 传 `:active=false` opt-out，但本 dev 浮层对页面透明、拿不到该开关。面板打开
// 期间**只在焦点没有去向任何可编辑元素时**（relatedTarget 为 body/非可编辑元素，
// 即被 ScanBar RAF 类回抢之前焦点悬空的空档）才同步夺回，保住输入框可持续打字；
// 焦点明确去了面板外的其它可编辑元素 = 用户主动点了页面上另一个输入框，一律让位
// —— dev 浮层绝不劫持用户的真实输入意图（旧实现把「面板外任何可编辑元素」都当
// ScanBar 回抢而夺回，会把用户点击的页面输入框抢走，属误伤）。注码派发期间
// （injecting）也让位 —— 注码正需要焦点不在可编辑元素上走 document 捕获路径。
function isEditable(el: unknown): boolean {
  return (
    el instanceof HTMLInputElement ||
    el instanceof HTMLTextAreaElement ||
    el instanceof HTMLSelectElement ||
    (el instanceof HTMLElement && (el.isContentEditable || el.hasAttribute('contenteditable')))
  )
}

function onCodeInputBlur(event: FocusEvent) {
  if (!open.value || injecting.value) return
  const next = event.relatedTarget
  if (next instanceof Node && panelEl.value?.contains(next)) return
  // 焦点去向任何可编辑元素（用户主动点击其它输入框）→ 让位，不劫持。
  if (isEditable(next)) return
  // 焦点悬空（body/非可编辑元素）→ 夺回，输入框保持可打字。
  codeInputEl.value?.focus()
}
</script>

<template>
  <div
    class="pointer-events-none fixed bottom-24 right-3 z-50 flex flex-col items-end gap-2"
    data-testid="dev-scan-simulator"
  >
    <div
      v-if="open"
      ref="panelEl"
      class="pointer-events-auto w-64 rounded-lg border border-border bg-card p-3 shadow-lg"
      data-testid="dev-scan-panel"
    >
      <div class="mb-2 flex items-center justify-between">
        <span class="text-sm font-medium">模拟扫码（dev）</span>
        <button
          type="button"
          class="text-muted-foreground"
          aria-label="关闭模拟扫码面板"
          @click="open = false"
        >
          <X class="size-4" aria-hidden="true" />
        </button>
      </div>
      <div class="flex items-center gap-2">
        <input
          ref="codeInputEl"
          v-model="code"
          type="text"
          autocomplete="off"
          autocapitalize="off"
          spellcheck="false"
          placeholder="输入条码内容"
          class="w-full rounded-md border border-border bg-transparent px-2 py-1.5 text-sm outline-none placeholder:text-muted-foreground"
          data-testid="dev-scan-input"
          @blur="onCodeInputBlur"
          @keydown.enter.prevent="inject(code)"
        />
        <button
          type="button"
          class="shrink-0 rounded-md bg-brand px-3 py-1.5 text-sm text-brand-foreground disabled:opacity-50"
          :disabled="injecting || !code.trim()"
          data-testid="dev-scan-inject"
          @click="inject(code)"
        >
          注码
        </button>
      </div>
      <div class="mt-2 flex flex-wrap gap-1.5">
        <button
          v-for="preset in PRESET_CODES"
          :key="preset"
          type="button"
          class="rounded-md border border-border px-2 py-1 text-xs text-muted-foreground disabled:opacity-50"
          :disabled="injecting"
          data-testid="dev-scan-preset"
          @click="inject(preset)"
        >
          {{ preset }}
        </button>
      </div>
    </div>
    <button
      type="button"
      class="pointer-events-auto flex size-11 items-center justify-center rounded-full border border-border bg-card text-brand shadow-lg"
      aria-label="模拟扫码"
      data-testid="dev-scan-toggle"
      @click="open = !open"
    >
      <ScanLine class="size-5" aria-hidden="true" />
    </button>
  </div>
</template>
