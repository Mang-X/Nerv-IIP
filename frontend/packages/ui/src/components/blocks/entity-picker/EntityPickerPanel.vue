<script setup lang="ts">
import { useVirtualList } from '@vueuse/core'
import { computed, nextTick, onMounted, ref, useId, watch } from 'vue'
import { CheckIcon, PlusIcon, SearchIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'
import type { EntityPickerOption } from './types'

/**
 * 内部件（不从包外导出）：NvEntityPicker 两种形态共用的「搜索框 + 实体列表」。
 * 抽出来是为了保证下拉形态和弹窗形态的行数、留白、空态、继续筛选提示完全一致 ——
 * 两种形态各写一遍模板迟早会漂移。
 */
const props = withDefaults(
  defineProps<{
    options: EntityPickerOption[]
    modelValue?: string
    searchPlaceholder?: string
    emptyText?: string
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
    /** 服务端搜索时目录的匹配总数；大于当前条数即说明还有没显示出来的，提示用户继续输入。 */
    totalCount?: number
    /** 新增入口文案（如「新增车间」）；传了才在列表下方出现入口，点击发出 `create`。 */
    createText?: string
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
  (e: 'create'): void
  /** 服务端搜索时滚到列表底部、且还有没加载的匹配项：调用方加载下一页并追加进 `options`。 */
  (e: 'load-more'): void
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

// 目录比当前显示的多时给出「继续输入」的出路，
// 免得用户以为列表就这么多、新建的条目「不见了」。
const hasMore = computed(
  () => props.serverSearch && props.totalCount != null && props.totalCount > filtered.value.length,
)

/**
 * 列表虚拟化（`useVirtualList`）：候选上百条时只渲染可视区附近的行。
 * 行高是定值（单行名称 36px，名称 + 编码两行 52px），行上直接用同一个值定高。
 * 条数少时整列渲染：开销可以忽略，读屏也能拿到完整列表。
 * 面板每次打开都是全新挂载，所以行高在挂载时按 `showCode` 定一次即可。
 */
const VIRTUALIZE_ABOVE = 100
const rowHeight = props.showCode ? 52 : 36
const virtualized = computed(() => filtered.value.length > VIRTUALIZE_ABOVE)
const {
  list: virtualRows,
  containerProps,
  wrapperProps,
  scrollTo,
} = useVirtualList(filtered, { itemHeight: rowHeight, overscan: 8 })
const rows = computed(() =>
  virtualized.value
    ? virtualRows.value.map(({ data, index }) => ({ option: data, index }))
    : filtered.value.map((option, index) => ({ option, index })),
)

// 离底部不到几行就要下一页，滚到底之前数据已经接上。
function onScroll(event: Event) {
  const el = event.currentTarget as HTMLElement
  if (hasMore.value && el.scrollTop + el.clientHeight >= el.scrollHeight - rowHeight * 5) {
    emit('load-more')
  }
}

// 换了搜索词，结果从头开始：滚动位置一起回到顶部。
watch(query, () => scrollTo(0))

/**
 * 键盘移动高亮项时把它滚进可视区。高亮项已渲染就就近滚入；
 * 虚拟化下它可能还没渲染（如从末项绕回首项），先按下标滚过去。
 */
function revealActive() {
  void nextTick(() => {
    const row = document.getElementById(optionId(activeIndex.value))
    if (row) row.scrollIntoView({ block: 'nearest' })
    else scrollTo(activeIndex.value)
  })
}

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
    revealActive()
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    activeIndex.value = items.length ? (activeIndex.value - 1 + items.length) % items.length : 0
    revealActive()
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
      v-bind="containerProps"
      role="listbox"
      :class="cn('overscroll-contain p-2', dense ? 'max-h-72' : 'max-h-80')"
      @scroll.passive="onScroll"
    >
      <div v-if="loading" class="px-3 py-8 text-center text-sm text-muted-foreground">加载中…</div>
      <div v-else v-bind="virtualized ? wrapperProps : {}">
        <button
          v-for="{ option, index } in rows"
          :id="optionId(index)"
          :key="option.value"
          type="button"
          role="option"
          :aria-selected="option.value === modelValue"
          :data-active="index === activeIndex || undefined"
          :style="{ height: `${rowHeight}px` }"
          class="flex w-full items-center gap-2.5 rounded-md px-2.5 text-left outline-none hover:bg-accent data-active:bg-accent"
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
      </div>
    </div>

    <div v-if="createText" :class="cn('border-t border-border', dense ? 'p-1.5' : 'px-4 py-2')">
      <button
        type="button"
        class="flex w-full items-center gap-2.5 rounded-md px-2.5 py-2 text-left text-sm text-primary outline-none hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring/50"
        @click="emit('create')"
      >
        <PlusIcon class="size-4 shrink-0" aria-hidden="true" />
        {{ createText }}
      </button>
    </div>

    <p
      v-if="hasMore && !loading"
      :class="
        cn('border-t border-border py-2.5 text-xs text-muted-foreground', dense ? 'px-2.5' : 'px-6')
      "
    >
      输入关键字继续筛选
    </p>
  </div>
</template>
