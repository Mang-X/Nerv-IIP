<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref } from 'vue'
import { PopoverAnchor, PopoverContent, PopoverPortal, PopoverRoot } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — 输入联想框：文本输入即过滤建议，**允许自由录入**（值 = 输入的文本，
 * 建议只是快速填入）。用于设备编号、测量特性这类"有历史候选、也可能新填"的字段。
 * 基于 reka Popover（portal 逃逸 Sheet 的 overflow 裁剪），输入框作为 anchor，
 * 弹层只做建议展示，不抢焦、不因点击输入框而误关。
 */
export interface ComboboxSuggestion {
  value: string
  label?: string
  hint?: string
}

const props = withDefaults(
  defineProps<{
    modelValue?: string
    suggestions: ComboboxSuggestion[]
    placeholder?: string
    emptyText?: string
    disabled?: boolean
    id?: string
    class?: HTMLAttributes['class']
  }>(),
  { placeholder: '', emptyText: '无匹配建议', disabled: false },
)

const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

const open = ref(false)
const activeIndex = ref(-1)
const inputEl = ref<HTMLInputElement>()

const filtered = computed(() => {
  const q = (props.modelValue ?? '').trim().toLowerCase()
  if (!q) return props.suggestions
  return props.suggestions.filter((s) =>
    `${s.label ?? s.value} ${s.hint ?? ''}`.toLowerCase().includes(q),
  )
})

function setValue(value: string) {
  emit('update:modelValue', value)
}
function onInput(e: Event) {
  setValue((e.target as HTMLInputElement).value)
  // 仅在有匹配建议时展开，避免自由录入时"无匹配建议"每次弹出打扰。
  open.value = filtered.value.length > 0
  activeIndex.value = -1
}
function onFocus() {
  if (filtered.value.length) open.value = true
}
function pick(suggestion: ComboboxSuggestion) {
  setValue(suggestion.value)
  open.value = false
}
function onKeydown(e: KeyboardEvent) {
  if ((e.key === 'ArrowDown' || e.key === 'ArrowUp') && !open.value) {
    if (filtered.value.length) {
      open.value = true
      e.preventDefault()
    }
    return
  }
  if (!open.value) return
  const items = filtered.value
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    activeIndex.value = items.length ? (activeIndex.value + 1) % items.length : -1
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    activeIndex.value = items.length ? (activeIndex.value - 1 + items.length) % items.length : -1
  } else if (e.key === 'Enter') {
    if (activeIndex.value >= 0 && items[activeIndex.value]) {
      e.preventDefault()
      pick(items[activeIndex.value])
    }
  } else if (e.key === 'Escape') {
    open.value = false
  }
}
// 点击落在 anchor 输入框上时保持弹层打开（否则 reka 会当成 outside 而关闭）。
function onPointerDownOutside(e: Event) {
  const target = (e as CustomEvent<{ originalEvent: Event }>).detail?.originalEvent?.target
  if (target instanceof Node && inputEl.value?.contains(target)) e.preventDefault()
}
</script>

<template>
  <PopoverRoot v-model:open="open">
    <PopoverAnchor as-child>
      <input
        :id="id"
        ref="inputEl"
        :value="modelValue"
        :placeholder="placeholder"
        :disabled="disabled"
        autocomplete="off"
        role="combobox"
        :aria-expanded="open"
        :class="
          cn(
            'border-input focus-visible:border-ring focus-visible:ring-ring/50 dark:bg-input/30 h-8 w-full rounded-lg border bg-transparent px-2.5 py-1 text-base outline-none transition-colors focus-visible:ring-3 placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50 md:text-sm',
            props.class,
          )
        "
        @input="onInput"
        @focus="onFocus"
        @keydown="onKeydown"
      />
    </PopoverAnchor>
    <PopoverPortal>
      <PopoverContent
        align="start"
        :side-offset="4"
        class="z-50 max-h-60 w-(--reka-popover-trigger-width) min-w-52 overflow-y-auto rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-md outline-none data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0"
        @open-auto-focus.prevent
        @close-auto-focus.prevent
        @pointer-down-outside="onPointerDownOutside"
      >
        <button
          v-for="(suggestion, index) in filtered"
          :key="suggestion.value"
          type="button"
          :data-active="index === activeIndex || undefined"
          class="flex w-full items-center gap-2 rounded-md px-2.5 py-2 text-left text-sm outline-none hover:bg-accent data-active:bg-accent"
          @click="pick(suggestion)"
          @mousemove="activeIndex = index"
        >
          <span class="flex-1 truncate">{{ suggestion.label ?? suggestion.value }}</span>
          <span v-if="suggestion.hint" class="text-xs text-muted-foreground">{{
            suggestion.hint
          }}</span>
        </button>
        <div v-if="!filtered.length" class="px-3 py-6 text-center text-sm text-muted-foreground">
          {{ emptyText }}
        </div>
      </PopoverContent>
    </PopoverPortal>
  </PopoverRoot>
</template>
