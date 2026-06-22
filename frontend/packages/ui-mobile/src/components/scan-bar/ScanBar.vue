<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { onMounted, ref } from 'vue'
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

function refocus() {
  if (!props.active) return
  // 键盘楔入设备需要输入框始终持有焦点
  requestAnimationFrame(() => inputEl.value?.focus())
}

onMounted(() => {
  if (props.active) refocus()
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
