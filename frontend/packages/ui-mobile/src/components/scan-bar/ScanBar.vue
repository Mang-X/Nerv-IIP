<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ScanLine } from 'lucide-vue-next'
import { cn } from '../../lib/utils'

withDefaults(defineProps<{ placeholder?: string; class?: string; autofocus?: boolean }>(), {
  placeholder: '扫描条码 / 二维码',
  autofocus: true,
})
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
  // 键盘楔入设备需要输入框始终持有焦点
  requestAnimationFrame(() => inputEl.value?.focus())
}

onMounted(() => {
  if (inputEl.value && (inputEl.value as HTMLInputElement).autofocus !== false) refocus()
})
</script>

<template>
  <div :class="cn('flex items-center gap-2 rounded-lg border border-border bg-card px-3 min-h-touch', $props.class)">
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
