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
    if (active) refocus()
    else cancelPendingRefocus()
  },
)

// --- document 级扫码缓冲（S2 首字符竞态的产品修复）----------------------------
// blur → RAF 回焦的窗口内，扫码枪突发字符没有接收者会丢首字符。
// 挂载期间在 document capture 阶段兜底：焦点不在本 input、也不在其它可编辑
// 元素上时，把可打印单字符收进 buffer（Enter 触发提交），字符流不落地即不丢。
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
  // 多实例保险：另一个 ScanBar 已消费该事件时不再双写。
  if (event.defaultPrevented) return
  const own = inputEl.value
  if (!own) return
  // 本 input 已聚焦（或事件目标是本 input）时走原生 input 路径，避免双写。
  if (event.target === own || document.activeElement === own) return
  // 其它可编辑元素持有焦点/作为目标时不吞按键（浮层里的输入框优先）。
  if (isOtherEditable(document.activeElement) || isOtherEditable(event.target)) return

  if (event.key === 'Enter') {
    // 仅当缓冲里已有扫码字符时才把 Enter 当扫码后缀消费，
    // 否则放行（焦点在按钮上的回车激活等正常键盘交互不受影响）。
    if (!buffer.value.trim()) return
    event.preventDefault()
    submit()
    return
  }
  if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
    event.preventDefault()
    buffer.value += event.key
  }
}

onMounted(() => {
  document.addEventListener('keydown', onDocumentKeydown, true)
  if (props.active) refocus()
})

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onDocumentKeydown, true)
  cancelPendingRefocus()
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
