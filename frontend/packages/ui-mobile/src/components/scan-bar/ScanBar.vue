<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ScanLine } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

// `active` 是焦点抢夺的 opt-out 开关：键盘楔入设备需要输入框常驻焦点，
// 但当消费方打开 BottomSheet/Dialog 等浮层时应传 `active=false`，
// 让 ScanBar 停止自动重聚焦，避免把焦点从浮层抢回、破坏 focus-trap。
const props = withDefaults(
  defineProps<{ placeholder?: string; class?: HTMLAttributes['class']; active?: boolean }>(),
  {
    placeholder: '扫描条码 / 二维码',
    active: true,
  },
)
const emit = defineEmits<{ scan: [value: string] }>()

const inputEl = ref<HTMLInputElement>()
const buffer = ref('')

function submit() {
  const value = buffer.value.trim()
  if (!value) return
  emit('scan', value)
  buffer.value = ''
}

// --- 焦点回抢（含浮层竞态防护）------------------------------------------------
// blur 后下一帧回焦；RAF 回调内必须复查 `active`：
// blur 已排入 RAF 后浮层才打开（active 变 false）的场景，回调不得再抢焦。
let pendingRefocusRaf: number | null = null

function cancelPendingRefocus() {
  if (pendingRefocusRaf === null) return
  cancelAnimationFrame(pendingRefocusRaf)
  pendingRefocusRaf = null
}

function refocus() {
  if (!props.active) return
  cancelPendingRefocus()
  // 键盘楔入设备需要输入框始终持有焦点
  pendingRefocusRaf = requestAnimationFrame(() => {
    pendingRefocusRaf = null
    if (props.active) inputEl.value?.focus()
  })
}

watch(
  () => props.active,
  (active) => {
    // false：取消尚未执行的回焦；true：重新武装焦点（浮层关闭后恢复常驻）。
    if (active) {
      refocus()
    } else {
      cancelPendingRefocus()
      // 浮层打开时丢弃 document 捕获路径尚未消费的半次扫码残片：
      // 残片不得与浮层关闭后的下一枪拼接。快照守卫保证只清捕获路径
      // 写入的内容，不动用户经聚焦 input 手工键入的草稿。
      if (lastDocCaptureAt !== null && buffer.value === docCaptureSnapshot) buffer.value = ''
      resetDocCapture()
    }
  },
)

// --- document 级扫码缓冲（S2 首字符竞态的产品修复）----------------------------
// blur → RAF 回焦的窗口内，扫码枪突发字符没有接收者会丢首字符。
// 挂载期间在 document capture 阶段兜底：焦点不在本 input、也不在其它可编辑
// 元素上时，把可打印单字符收进 buffer（Enter 触发提交），字符流不落地即不丢。
//
// 时序判别：扫码枪楔入突发的字符间隔实测约 10–30ms，据此区分扫码流与
// 人手误按，防止残片无限期滞留缓冲、进而吞掉正常键盘交互（P0-1）。
// 阈值只约束 document 捕获路径；input 聚焦时的原生输入路径行为不变。

// 同一枪内相邻字符的最大间隔。人手连击很难低于 ~150ms，扫码枪远低于
// 100ms，取 100ms：超过即视为不属于同一突发，缓冲重置为当前字符。
const SCAN_BURST_GAP_MS = 100
// Enter 后缀相对最后一个捕获字符的新鲜度上限，同时兼作空闲清理延迟：
// 超时仍未等到 Enter 即视为误按残片丢弃。取 300ms，留足扫码枪发送
// Enter 后缀的余量（同批突发内间隔 <30ms，3 倍冗余仍远低于人手节奏）。
const SCAN_FRESHNESS_MS = 300

// document 捕获路径的「上次入缓冲时刻」；null = 当前缓冲无未消费的捕获残片。
let lastDocCaptureAt: number | null = null
// 最近一次捕获后的缓冲快照：清理前比对，缓冲被 input 路径改动过则不动用户内容。
let docCaptureSnapshot = ''
let idleCleanupTimer: ReturnType<typeof setTimeout> | null = null

function clearIdleCleanup() {
  if (idleCleanupTimer === null) return
  clearTimeout(idleCleanupTimer)
  idleCleanupTimer = null
}

function resetDocCapture() {
  lastDocCaptureAt = null
  docCaptureSnapshot = ''
  clearIdleCleanup()
}

function armIdleCleanup() {
  clearIdleCleanup()
  idleCleanupTimer = setTimeout(() => {
    idleCleanupTimer = null
    // input 已聚焦：缓冲归原生 input 路径所有（用户可能正在续写/编辑），
    // 绝不清掉手工键入的内容，只撤销捕获状态。
    if (document.activeElement === inputEl.value) {
      lastDocCaptureAt = null
      docCaptureSnapshot = ''
      return
    }
    // 新鲜度窗口内没等到 Enter：这段捕获是误按残片，清掉。
    // 快照不一致说明缓冲已被 input 路径改动过，同样不动。
    if (buffer.value === docCaptureSnapshot) buffer.value = ''
    lastDocCaptureAt = null
    docCaptureSnapshot = ''
  }, SCAN_FRESHNESS_MS)
}

function isOtherEditable(el: unknown): boolean {
  if (!(el instanceof HTMLElement) || el === inputEl.value) return false
  if (
    el instanceof HTMLInputElement ||
    el instanceof HTMLTextAreaElement ||
    el instanceof HTMLSelectElement
  ) {
    return true
  }
  return el.isContentEditable || el.hasAttribute('contenteditable')
}

function onDocumentKeydown(event: KeyboardEvent) {
  if (!props.active) return
  // 多实例简化仲裁：capture 阶段最早注册者先 preventDefault 抢占，后注册者
  // 看到 defaultPrevented 即让位，避免双写。已知限制：若页面还有其它更早
  // preventDefault 的 capture listener，扫码字符会被它吞掉——当前 PDA
  // 单 ScanBar 页面下该假设成立。
  if (event.defaultPrevented) return
  // IME 组合期的按键属输入法内部状态，一律放行不捕获。
  if (event.isComposing) return
  const own = inputEl.value
  if (!own) return
  // 本 input 已聚焦（或事件目标是本 input）时走原生 input 路径，避免双写。
  if (event.target === own || document.activeElement === own) return
  // 其它可编辑元素持有焦点/作为目标时不吞按键（浮层里的输入框优先）。
  if (isOtherEditable(document.activeElement) || isOtherEditable(event.target)) return

  const now = Date.now()
  if (event.key === 'Enter') {
    // 仅当缓冲确由 document 捕获路径新鲜写入（非空且距最后捕获字符
    // < 新鲜度阈值）才把 Enter 当扫码后缀消费；陈旧残片或手工内容上的
    // Enter 一律放行——焦点在按钮上的回车激活等正常键盘交互不受影响。
    if (!buffer.value.trim()) return
    if (lastDocCaptureAt === null || now - lastDocCaptureAt > SCAN_FRESHNESS_MS) return
    event.preventDefault()
    resetDocCapture()
    submit()
    return
  }
  // Space 一律放行不捕获：焦点在按钮上按空格是激活操作，不得被缓冲吞掉。
  // 已知限制：本仓条码值不含空格（扫码枪楔入流程），放弃捕获空格无损。
  if (event.key === ' ') return
  if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
    event.preventDefault()
    // 突发时序判别：距上次捕获超过突发间隔 → 之前的捕获是陈旧残片
    // （人手误按或半途中断的枪），重置为当前字符再继续。
    if (lastDocCaptureAt !== null && now - lastDocCaptureAt > SCAN_BURST_GAP_MS) {
      buffer.value = event.key
    } else {
      buffer.value += event.key
    }
    lastDocCaptureAt = now
    docCaptureSnapshot = buffer.value
    armIdleCleanup()
  }
}

onMounted(() => {
  document.addEventListener('keydown', onDocumentKeydown, true)
  if (props.active) refocus()
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onDocumentKeydown, true)
  cancelPendingRefocus()
  clearIdleCleanup()
})
</script>

<template>
  <div
    :class="
      cn(
        'flex items-center gap-2 rounded-lg border border-border bg-card px-3 min-h-touch',
        $props.class,
      )
    "
  >
    <ScanLine class="size-5 shrink-0 text-brand" aria-hidden="true" />
    <input
      ref="inputEl"
      v-model="buffer"
      type="text"
      inputmode="none"
      autocomplete="off"
      autocapitalize="off"
      spellcheck="false"
      :placeholder="placeholder"
      class="w-full bg-transparent py-2 text-base outline-none placeholder:text-muted-foreground"
      @keydown.enter.prevent="submit"
      @blur="refocus"
    />
  </div>
</template>
