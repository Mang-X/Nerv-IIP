<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref, useId, watch } from 'vue'
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

// 组件内 query 是过滤的唯一依据，随输入同步更新（受控 modelValue 变化时回同步）。
// 不直接读 props.modelValue 过滤——emit 后 prop 要到下一次渲染才回传，会令开关/过滤落后一拍。
const query = ref(props.modelValue ?? '')
watch(
  () => props.modelValue,
  (value) => {
    const next = value ?? ''
    if (next !== query.value) query.value = next
  },
)

const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return props.suggestions
  return props.suggestions.filter((s) =>
    `${s.label ?? s.value} ${s.hint ?? ''}`.toLowerCase().includes(q),
  )
})

const listboxId = useId()
const optionId = (index: number) => `${listboxId}-opt-${index}`
const activeDescendant = computed(() =>
  open.value && activeIndex.value >= 0 ? optionId(activeIndex.value) : undefined,
)

function onInput(e: Event) {
  query.value = (e.target as HTMLInputElement).value
  emit('update:modelValue', query.value)
  // filtered 现基于 query（已同步）计算，开关判断与本次输入一致，不再落后一拍。
  open.value = filtered.value.length > 0
  activeIndex.value = -1
}
function onFocus() {
  if (filtered.value.length) open.value = true
}
function pick(suggestion: ComboboxSuggestion) {
  query.value = suggestion.value
  emit('update:modelValue', suggestion.value)
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
        :value="query"
        :placeholder="placeholder"
        :disabled="disabled"
        autocomplete="off"
        role="combobox"
        aria-autocomplete="list"
        :aria-controls="listboxId"
        :aria-expanded="open"
        :aria-activedescendant="activeDescendant"
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
        :id="listboxId"
        role="listbox"
        align="start"
        :side-offset="4"
        class="z-50 max-h-60 w-(--reka-popover-trigger-width) min-w-52 overflow-y-auto rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-md outline-none data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0"
        @open-auto-focus.prevent
        @close-auto-focus.prevent
        @pointer-down-outside="onPointerDownOutside"
      >
        <button
          v-for="(suggestion, index) in filtered"
          :id="optionId(index)"
          :key="suggestion.value"
          type="button"
          role="option"
          :aria-selected="index === activeIndex"
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
