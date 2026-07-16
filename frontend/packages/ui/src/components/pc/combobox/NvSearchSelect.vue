<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, nextTick, ref, useId, watch } from 'vue'
import { PopoverContent, PopoverPortal, PopoverRoot, PopoverTrigger } from 'reka-ui'
import { CheckIcon, ChevronsUpDownIcon, SearchIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — 弹出选择框：按钮触发一个可搜索的弹出列表，**只能选、不能自由录入**。
 * 用于从固定集合里选一个（技师目录、停机原因、维护结果…），比长下拉更好搜。
 * 基于 reka Popover（portal 逃逸 Sheet 的 overflow 裁剪），自带过滤 + 键盘导航。
 */
export interface SearchSelectOption {
  value: string
  label: string
  hint?: string
}

const props = withDefaults(
  defineProps<{
    modelValue?: string
    options: SearchSelectOption[]
    placeholder?: string
    searchPlaceholder?: string
    emptyText?: string
    disabled?: boolean
    loading?: boolean
    id?: string
    ariaLabel?: string
    /** 弹层内搜索框的可访问名称；缺省时从字段 ariaLabel 派生（读屏可知搜的是哪个字段）。 */
    searchAriaLabel?: string
    class?: HTMLAttributes['class']
  }>(),
  {
    placeholder: '请选择',
    searchPlaceholder: '搜索…',
    emptyText: '无匹配项',
    disabled: false,
    loading: false,
  },
)

const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

const open = ref(false)
const query = ref('')
const activeIndex = ref(0)
const inputEl = ref<HTMLInputElement>()

const selected = computed(() => props.options.find((o) => o.value === props.modelValue))
const filtered = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return props.options
  return props.options.filter((o) =>
    `${o.label} ${o.hint ?? ''} ${o.value}`.toLowerCase().includes(q),
  )
})

const searchLabel = computed(
  () =>
    props.searchAriaLabel ?? (props.ariaLabel ? `搜索${props.ariaLabel}` : props.searchPlaceholder),
)

const listboxId = useId()
const optionId = (index: number) => `${listboxId}-opt-${index}`
const activeDescendant = computed(() =>
  open.value && filtered.value.length ? optionId(activeIndex.value) : undefined,
)

watch(filtered, () => {
  activeIndex.value = 0
})
watch(open, (isOpen) => {
  if (isOpen) {
    query.value = ''
    activeIndex.value = 0
    void nextTick(() => inputEl.value?.focus())
  }
})

function pick(option: SearchSelectOption) {
  emit('update:modelValue', option.value)
  open.value = false
}
function onKeydown(e: KeyboardEvent) {
  const items = filtered.value
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    activeIndex.value = items.length ? (activeIndex.value + 1) % items.length : 0
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    activeIndex.value = items.length ? (activeIndex.value - 1 + items.length) % items.length : 0
  } else if (e.key === 'Enter') {
    e.preventDefault()
    const option = items[activeIndex.value]
    if (option) pick(option)
  }
}
</script>

<template>
  <PopoverRoot v-model:open="open">
    <PopoverTrigger as-child>
      <button
        :id="id"
        type="button"
        :aria-label="ariaLabel"
        aria-haspopup="listbox"
        :aria-expanded="open"
        :aria-controls="open ? listboxId : undefined"
        :disabled="disabled"
        :class="
          cn(
            'flex h-9 w-full items-center justify-between gap-2 rounded-md border border-input bg-card px-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30',
            props.class,
          )
        "
      >
        <span :class="cn('line-clamp-1 text-left', !selected && 'text-muted-foreground')">
          {{ selected?.label ?? (loading ? '加载中…' : placeholder) }}
        </span>
        <ChevronsUpDownIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      </button>
    </PopoverTrigger>
    <PopoverPortal>
      <PopoverContent
        align="start"
        :side-offset="4"
        class="z-50 w-(--reka-popover-trigger-width) min-w-52 overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-md outline-none data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0"
        @open-auto-focus.prevent
      >
        <div class="flex items-center gap-2 border-b border-border px-2.5">
          <SearchIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <input
            ref="inputEl"
            v-model="query"
            :placeholder="searchPlaceholder"
            :aria-label="searchLabel"
            autocomplete="off"
            role="combobox"
            aria-autocomplete="list"
            :aria-controls="listboxId"
            :aria-expanded="open"
            :aria-activedescendant="activeDescendant"
            class="h-9 w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
            @keydown="onKeydown"
          />
        </div>
        <div :id="listboxId" role="listbox" class="max-h-60 overflow-y-auto p-1">
          <button
            v-for="(option, index) in filtered"
            :id="optionId(index)"
            :key="option.value"
            type="button"
            role="option"
            :aria-selected="option.value === modelValue"
            :data-active="index === activeIndex || undefined"
            class="flex w-full items-center gap-2 rounded-md px-2.5 py-2 text-left text-sm outline-none hover:bg-accent data-active:bg-accent"
            @click="pick(option)"
            @mousemove="activeIndex = index"
          >
            <CheckIcon
              :class="
                cn('size-4 shrink-0', option.value === modelValue ? 'opacity-100' : 'opacity-0')
              "
              aria-hidden="true"
            />
            <span class="flex-1 truncate">{{ option.label }}</span>
            <span v-if="option.hint" class="text-xs text-muted-foreground">{{ option.hint }}</span>
          </button>
          <div v-if="!filtered.length" class="px-3 py-6 text-center text-sm text-muted-foreground">
            {{ emptyText }}
          </div>
        </div>
      </PopoverContent>
    </PopoverPortal>
  </PopoverRoot>
</template>
