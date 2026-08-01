<script setup lang="ts">
import {
  NvButton,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '@nerv-iip/ui'
import {
  CalendarClockIcon,
  CheckIcon,
  ChevronDownIcon,
  ChevronUpIcon,
  LockIcon,
  LockOpenIcon,
  MaximizeIcon,
  RefreshCwIcon,
  Redo2Icon,
  SearchIcon,
  Undo2Icon,
  XIcon,
  ZoomInIcon,
  ZoomOutIcon,
} from '@lucide/vue'
import { computed } from 'vue'
import type { TimeScale } from '../../engine/engine'
import type { SchedulingDimension } from '../../model/types'

const props = withDefaults(
  defineProps<{
    scale: TimeScale
    readOnly: boolean
    canUndo: boolean
    canRedo: boolean
    dirty: boolean
    busy: boolean
    canRepreview?: boolean
    canRelease?: boolean
    /**
     * 是否显示编辑簇(撤销 / 重做 / 只读锁)。只读面(如方案「甘特图」页签)传 false:
     * 那里本来就改不了东西,摆一个「允许编辑」的锁按钮是在骗人——点了也不会变成可拖拽。
     */
    canEdit?: boolean
    /** 是否显示搜索框。待排池 500 行、甘特 431 道工序,没有搜索调度员就回 Excel。 */
    searchable?: boolean
    search?: string
    /** 命中总数与当前命中序号(1 基;0 表示尚未定位)。 */
    matchCount?: number
    matchIndex?: number
    searchPlaceholder?: string
    groupDimensions?: SchedulingDimension[]
    groupBy?: string
  }>(),
  {
    canRepreview: true,
    canRelease: true,
    canEdit: true,
    searchable: false,
    search: '',
    matchCount: 0,
    matchIndex: 0,
    searchPlaceholder: '搜工单 / 工序 / 资源',
    groupDimensions: () => [],
    groupBy: 'workCenter',
  },
)

const emit = defineEmits<{
  scaleChange: [scale: TimeScale]
  zoomIn: []
  zoomOut: []
  today: []
  fit: []
  undo: []
  redo: []
  repreview: []
  release: []
  toggleReadOnly: []
  'update:search': [value: string]
  searchPrev: []
  searchNext: []
  groupChange: [groupBy: string]
}>()

const scaleModel = computed({
  get: () => props.scale,
  set: (v) => emit('scaleChange', v as TimeScale),
})

const searchModel = computed({
  get: () => props.search,
  set: (v) => emit('update:search', v),
})

const groupModel = computed({
  get: () => props.groupBy,
  set: (value) => emit('groupChange', value),
})

const hasQuery = computed(() => props.search.trim().length > 0)
/** 搜索读数:有输入才说话,且「没找到」必须说出来——静默 0 结果会让人以为搜索坏了。 */
const searchStatus = computed(() => {
  if (!hasQuery.value) return ''
  if (!props.matchCount) return '无匹配'
  return `${props.matchIndex || 1}/${props.matchCount}`
})
</script>

<template>
  <div
    class="flex flex-wrap items-center gap-2 border-b border-border/60 bg-card/80 px-5 py-3 backdrop-blur-sm"
  >
    <NvSelect v-model="scaleModel">
      <NvSelectTrigger class="h-8 w-24 border-border/70" aria-label="时间刻度"
        ><NvSelectValue
      /></NvSelectTrigger>
      <NvSelectContent>
        <NvSelectItem value="auto">自适应</NvSelectItem>
        <NvSelectItem value="hour">小时</NvSelectItem>
        <NvSelectItem value="day">日</NvSelectItem>
        <NvSelectItem value="week">周</NvSelectItem>
        <NvSelectItem value="month">月</NvSelectItem>
      </NvSelectContent>
    </NvSelect>

    <NvSelect v-if="groupDimensions.length" v-model="groupModel">
      <NvSelectTrigger class="h-8 w-36 border-border/70" aria-label="分组维度"
        ><NvSelectValue
      /></NvSelectTrigger>
      <NvSelectContent>
        <NvSelectItem
          v-for="dimension in groupDimensions"
          :key="dimension.key"
          :value="dimension.key"
        >
          {{ dimension.label }}
        </NvSelectItem>
      </NvSelectContent>
    </NvSelect>

    <span class="mx-1 h-5 w-px bg-border/60" aria-hidden="true" />

    <div class="flex items-center gap-0.5">
      <NvButton size="icon" variant="ghost" class="size-8" aria-label="放大" @click="emit('zoomIn')"
        ><ZoomInIcon aria-hidden="true"
      /></NvButton>
      <NvButton
        size="icon"
        variant="ghost"
        class="size-8"
        aria-label="缩小"
        @click="emit('zoomOut')"
        ><ZoomOutIcon aria-hidden="true"
      /></NvButton>
      <NvButton
        size="icon"
        variant="ghost"
        class="size-8"
        aria-label="定位到当前"
        @click="emit('today')"
        ><CalendarClockIcon aria-hidden="true"
      /></NvButton>
      <NvButton
        size="icon"
        variant="ghost"
        class="size-8"
        aria-label="适配窗口"
        @click="emit('fit')"
        ><MaximizeIcon aria-hidden="true"
      /></NvButton>
    </div>

    <template v-if="canEdit">
      <span class="mx-1 h-5 w-px bg-border/60" aria-hidden="true" />

      <div class="flex items-center gap-0.5">
        <NvButton
          size="icon"
          variant="ghost"
          class="size-8"
          aria-label="撤销"
          :disabled="!canUndo"
          @click="emit('undo')"
          ><Undo2Icon aria-hidden="true"
        /></NvButton>
        <NvButton
          size="icon"
          variant="ghost"
          class="size-8"
          aria-label="重做"
          :disabled="!canRedo"
          @click="emit('redo')"
          ><Redo2Icon aria-hidden="true"
        /></NvButton>
        <NvButton
          size="icon"
          variant="ghost"
          class="size-8"
          :aria-label="readOnly ? '允许编辑' : '锁定为只读'"
          @click="emit('toggleReadOnly')"
        >
          <LockIcon v-if="readOnly" aria-hidden="true" />
          <LockOpenIcon v-else aria-hidden="true" />
        </NvButton>
      </div>
    </template>

    <template v-if="searchable">
      <span class="mx-1 h-5 w-px bg-border/60" aria-hidden="true" />

      <!-- Enter 找下一个 / Shift+Enter 找上一个 / Esc 清空:与浏览器 Ctrl+F 的肌肉记忆一致。 -->
      <div class="flex items-center gap-1">
        <div class="relative">
          <SearchIcon
            class="pointer-events-none absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground"
            aria-hidden="true"
          />
          <!-- 用 type="text" 而不是 "search":Chrome 会给 search 输入框加一个原生清除 ×,
               与我们这个带无障碍名的清除按钮并排出现两个 ×(实测截图可见)。Esc 清空我们自己接了。 -->
          <NvInput
            v-model="searchModel"
            type="text"
            class="h-8 w-56 border-border/70 pr-8 pl-8 text-xs"
            aria-label="搜索工序"
            :placeholder="searchPlaceholder"
            @keydown.enter.exact.prevent="emit('searchNext')"
            @keydown.enter.shift.prevent="emit('searchPrev')"
            @keydown.esc.prevent="emit('update:search', '')"
          />
          <NvButton
            v-if="hasQuery"
            size="icon"
            variant="ghost"
            class="absolute top-1/2 right-0.5 size-7 -translate-y-1/2 text-muted-foreground"
            aria-label="清空搜索"
            @click="emit('update:search', '')"
          >
            <XIcon class="size-3.5" aria-hidden="true" />
          </NvButton>
        </div>
        <span
          v-if="searchStatus"
          class="min-w-14 text-xs tabular-nums"
          :class="matchCount ? 'text-muted-foreground' : 'text-warning'"
          role="status"
          >{{ searchStatus }}</span
        >
        <NvButton
          size="icon"
          variant="ghost"
          class="size-8"
          aria-label="上一个匹配"
          :disabled="!matchCount"
          @click="emit('searchPrev')"
        >
          <ChevronUpIcon aria-hidden="true" />
        </NvButton>
        <NvButton
          size="icon"
          variant="ghost"
          class="size-8"
          aria-label="下一个匹配"
          :disabled="!matchCount"
          @click="emit('searchNext')"
        >
          <ChevronDownIcon aria-hidden="true" />
        </NvButton>
      </div>
    </template>

    <div class="ml-auto flex items-center gap-2.5">
      <span v-if="dirty" class="flex items-center gap-1.5 text-xs font-medium text-warning">
        <span class="size-1.5 rounded-full bg-warning" aria-hidden="true" />
        有未应用的调整
      </span>
      <NvButton
        v-if="canRepreview"
        size="sm"
        variant="outline"
        class="border-border/70"
        :disabled="!dirty || busy"
        @click="emit('repreview')"
      >
        <RefreshCwIcon aria-hidden="true" />
        重新排程
      </NvButton>
      <NvButton v-if="canRelease" size="sm" :disabled="busy" @click="emit('release')">
        <CheckIcon aria-hidden="true" />
        发布计划
      </NvButton>
    </div>
  </div>
</template>
