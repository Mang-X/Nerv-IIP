<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, nextTick, ref, useId, watch } from 'vue'
import { DialogDescription, DialogRoot, DialogTitle, DialogTrigger } from 'reka-ui'
import { CheckIcon, ChevronsUpDownIcon, SearchIcon, XIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'
import NvDialogContent from '../../pc/dialog/NvDialogContent.vue'

/**
 * Blocks — 实体选择弹窗：按钮触发一个可搜索的实体选择对话框，**只能选、不能自由录入**。
 * 用于从主数据目录（物料 / SKU / 设备 / 质量特性…）里挑一个实体：相比
 * NvSearchSelect 的弹出列表，对话框给出更大的展示空间（名称 + 编码 + 辅助信息），
 * 适合上百条的目录；`sourceText` 在底部注明数据来源，空态不留悬念。
 */
export interface EntityPickerOption {
  /** 实体的人读业务编码（选中后回传的值）。 */
  value: string
  /** 实体名称（展示主文案）。 */
  label: string
  /** 辅助识别信息（分类 / 单位 / 状态…）。 */
  hint?: string
}

const props = withDefaults(
  defineProps<{
    modelValue?: string
    options: EntityPickerOption[]
    /** 对话框标题，如「选择物料」。 */
    title: string
    placeholder?: string
    searchPlaceholder?: string
    emptyText?: string
    /** 底部数据来源说明，如「数据来自物料主数据」。 */
    sourceText?: string
    loading?: boolean
    disabled?: boolean
    /** 允许清除已选值（触发按钮右侧出现清除叉）。 */
    clearable?: boolean
    id?: string
    ariaLabel?: string
    class?: HTMLAttributes['class']
  }>(),
  {
    placeholder: '请选择',
    searchPlaceholder: '搜索名称 / 编码…',
    emptyText: '无匹配实体',
    loading: false,
    disabled: false,
    clearable: false,
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

function pick(option: EntityPickerOption) {
  emit('update:modelValue', option.value)
  open.value = false
}
function clear() {
  emit('update:modelValue', '')
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
  <DialogRoot v-model:open="open">
    <div :class="cn('relative flex w-full items-center', props.class)" data-slot="nv-entity-picker">
      <DialogTrigger as-child>
        <button
          :id="id"
          type="button"
          :aria-label="ariaLabel"
          aria-haspopup="dialog"
          :disabled="disabled"
          class="flex h-9 w-full items-center justify-between gap-2 rounded-md border border-input bg-card px-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30"
        >
          <span
            :class="cn('line-clamp-1 text-left', !selected && 'text-muted-foreground')"
            :title="selected ? `${selected.label}（${selected.value}）` : undefined"
          >
            <template v-if="selected">
              {{ selected.label }}
              <span class="text-muted-foreground">（{{ selected.value }}）</span>
            </template>
            <template v-else>{{ loading ? '加载中…' : placeholder }}</template>
          </span>
          <span class="flex shrink-0 items-center gap-1">
            <ChevronsUpDownIcon class="size-4 text-muted-foreground" aria-hidden="true" />
          </span>
        </button>
      </DialogTrigger>
      <button
        v-if="clearable && selected && !disabled"
        type="button"
        class="absolute right-8 flex size-5 items-center justify-center rounded-sm text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
        :aria-label="`清除${ariaLabel ?? '所选实体'}`"
        @click.stop="clear"
      >
        <XIcon class="size-3.5" aria-hidden="true" />
      </button>
    </div>
    <NvDialogContent class="max-w-lg gap-0 p-0" @open-auto-focus.prevent="inputEl?.focus()">
      <div class="border-b border-border px-6 py-4">
        <DialogTitle class="text-base leading-none font-semibold">{{ title }}</DialogTitle>
        <DialogDescription class="sr-only">
          {{ sourceText ?? `搜索并选择${title.replace(/^选择/, '')}` }}
        </DialogDescription>
      </div>
      <div class="flex items-center gap-2 border-b border-border px-6">
        <SearchIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <input
          ref="inputEl"
          v-model="query"
          :placeholder="searchPlaceholder"
          :aria-label="`搜索${ariaLabel ?? title}`"
          autocomplete="off"
          role="combobox"
          aria-autocomplete="list"
          :aria-controls="listboxId"
          :aria-expanded="open"
          :aria-activedescendant="activeDescendant"
          class="h-11 w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
          @keydown="onKeydown"
        />
      </div>
      <div :id="listboxId" role="listbox" class="max-h-80 overflow-y-auto p-2">
        <div v-if="loading" class="px-3 py-8 text-center text-sm text-muted-foreground">
          加载中…
        </div>
        <template v-else>
          <button
            v-for="(option, index) in filtered"
            :id="optionId(index)"
            :key="option.value"
            type="button"
            role="option"
            :aria-selected="option.value === modelValue"
            :data-active="index === activeIndex || undefined"
            class="flex w-full items-center gap-2.5 rounded-md px-2.5 py-2 text-left outline-none hover:bg-accent data-active:bg-accent"
            @click="pick(option)"
            @mousemove="activeIndex = index"
          >
            <CheckIcon
              :class="
                cn('size-4 shrink-0', option.value === modelValue ? 'opacity-100' : 'opacity-0')
              "
              aria-hidden="true"
            />
            <span class="min-w-0 flex-1">
              <span class="block truncate text-sm">{{ option.label }}</span>
              <span class="block truncate font-mono text-xs text-muted-foreground">
                {{ option.value }}
              </span>
            </span>
            <span v-if="option.hint" class="shrink-0 text-xs text-muted-foreground">
              {{ option.hint }}
            </span>
          </button>
          <div v-if="!filtered.length" class="px-3 py-8 text-center text-sm text-muted-foreground">
            {{ emptyText }}
          </div>
        </template>
      </div>
      <div
        class="flex items-center justify-between border-t border-border px-6 py-2.5 text-xs text-muted-foreground"
      >
        <span>{{ sourceText ?? '' }}</span>
        <span v-if="!loading">共 {{ filtered.length }} 条</span>
      </div>
    </NvDialogContent>
  </DialogRoot>
</template>
