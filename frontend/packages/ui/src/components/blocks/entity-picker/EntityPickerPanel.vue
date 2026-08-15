<script setup lang="ts">
import { computed, nextTick, onMounted, ref, useId } from 'vue'
import { CheckIcon, SearchIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'
import type { EntityPickerOption } from './types'

/**
 * 内部件（不从包外导出）：NvEntityPicker 两种形态共用的「搜索框 + 实体列表 + 来源注脚」。
 * 抽出来是为了保证下拉形态和弹窗形态的行数、留白、空态、计数完全一致 ——
 * 两种形态各写一遍模板迟早会漂移。
 */
const props = withDefaults(
  defineProps<{
    options: EntityPickerOption[]
    modelValue?: string
    searchPlaceholder?: string
    emptyText?: string
    sourceText?: string
    loading?: boolean
    /** 搜索框的可访问名称。 */
    searchAriaLabel?: string
    /** 弹窗形态给更大的搜索框（h-11），下拉形态跟控件基线一致（h-9）。 */
    dense?: boolean
    /** 是否显示编码行。`value` 是内部标识（GUID）且没有 `code` 时必须关掉。 */
    showCode?: boolean
    /** 服务端搜索时的受控搜索词。 */
    search?: string
    /** 目录由服务端按搜索词过滤：本地不再二次过滤，`options` 即当前结果。 */
    serverSearch?: boolean
    /** 服务端搜索时目录的匹配总数；大于当前条数即说明还有没显示出来的。 */
    totalCount?: number
  }>(),
  {
    searchPlaceholder: '搜索名称 / 编码…',
    emptyText: '无匹配实体',
    loading: false,
    dense: true,
    showCode: true,
    serverSearch: false,
  },
)

/** 选项上要显示的编码：优先 `code`，否则回落到 `value`。 */
function codeOf(option: EntityPickerOption): string {
  return option.code ?? option.value
}

const emit = defineEmits<{
  (e: 'pick', option: EntityPickerOption): void
  (e: 'update:search', value: string): void
}>()

const localQuery = ref('')
const activeIndex = ref(0)
const inputEl = ref<HTMLInputElement>()

// 服务端搜索时搜索词由调用方持有（要拿去发请求）；本地搜索时留在面板内部。
const query = computed({
  get: () => (props.serverSearch ? (props.search ?? '') : localQuery.value),
  set: (value: string) => {
    if (props.serverSearch) emit('update:search', value)
    else localQuery.value = value
  },
})

const filtered = computed(() => {
  // 服务端已按搜索词过滤过：再本地过滤一遍只会把「服务端命中、当前页没有」的项误删。
  if (props.serverSearch) return props.options
  const q = query.value.trim().toLowerCase()
  if (!q) return props.options
  // 搜人读编码走 `code`（没有才回落 `value`）。`value` 是 GUID 时用户不会去搜它，
  // 把 GUID 塞进匹配串只会制造误命中。
  return props.options.filter((o) =>
    `${o.label} ${o.hint ?? ''} ${codeOf(o)}`.toLowerCase().includes(q),
  )
})

// 目录比当前显示的多时如实说明还有多少，并给出「继续输入」的出路，
// 免得用户以为列表就这么多、新建的条目「不见了」。
const hasMore = computed(
  () => props.serverSearch && props.totalCount != null && props.totalCount > filtered.value.length,
)

const listboxId = useId()
const optionId = (index: number) => `${listboxId}-opt-${index}`
const activeDescendant = computed(() =>
  filtered.value.length ? optionId(activeIndex.value) : undefined,
)

// 面板每次打开都是全新挂载（v-if 控制），所以搜索词/高亮项天然重置，
// 不需要再 watch(open) 回填 —— 少一个会跟打开动画抢时序的副作用。
onMounted(() => {
  void nextTick(() => inputEl.value?.focus())
})

function pick(option: EntityPickerOption) {
  emit('pick', option)
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

defineExpose({ focus: () => inputEl.value?.focus() })
</script>

<template>
  <div class="flex min-h-0 flex-col">
    <div :class="cn('flex items-center gap-2 border-b border-border', dense ? 'px-2.5' : 'px-6')">
      <SearchIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <input
        ref="inputEl"
        v-model="query"
        :placeholder="searchPlaceholder"
        :aria-label="searchAriaLabel"
        autocomplete="off"
        role="combobox"
        aria-autocomplete="list"
        :aria-controls="listboxId"
        aria-expanded="true"
        :aria-activedescendant="activeDescendant"
        :class="
          cn(
            'w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground',
            dense ? 'h-9' : 'h-11',
          )
        "
        @keydown="onKeydown"
      />
    </div>

    <div
      :id="listboxId"
      role="listbox"
      :class="cn('overflow-y-auto overscroll-contain p-2', dense ? 'max-h-72' : 'max-h-80')"
    >
      <div v-if="loading" class="px-3 py-8 text-center text-sm text-muted-foreground">加载中…</div>
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
            <span v-if="showCode" class="block truncate font-mono text-xs text-muted-foreground">
              {{ codeOf(option) }}
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
      :class="
        cn(
          'flex items-center justify-between gap-3 border-t border-border py-2.5 text-xs text-muted-foreground',
          dense ? 'px-2.5' : 'px-6',
        )
      "
    >
      <span class="truncate">
        {{ hasMore ? '输入关键字继续筛选' : (sourceText ?? '') }}
      </span>
      <span v-if="!loading" class="shrink-0 tabular-nums">
        {{ hasMore ? `显示 ${filtered.length} / 共 ${totalCount} 条` : `共 ${filtered.length} 条` }}
      </span>
    </div>
  </div>
</template>
