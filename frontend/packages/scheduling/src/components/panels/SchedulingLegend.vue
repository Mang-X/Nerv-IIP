<script setup lang="ts">
// 图例:讲清当前视图的视觉语言。分色与条形共用 --nv-scheduling-category-* 全局变量,保证图例与条形一致。
// 事实源是当前模型:每一项都由 deriveLegendSemantics 从 ScheduleModel 推导——
// 图上没有的语义(没带日历就没有班次边界、方案里没有换型窗口)绝不列进图例。
// 视图感知:资源排产板隐藏甘特专属(计划基线/依赖/里程碑),改讲齐套/换型/瓶颈。
// 结构:按分类分组、每类一行(分类标题 + 该类图例项)。每类可折叠(本地 ref)、可显隐(emit + 自身淡化)。
import { ChevronRightIcon, EyeIcon, EyeOffIcon } from '@lucide/vue'
import { computed, reactive } from 'vue'
import { BLOCK_LABELS, BLOCK_TOKENS } from '../../model/blocks'
import { deriveLegendSemantics, FULL_LEGEND_SEMANTICS } from '../../model/legend'
import type { ScheduleModel } from '../../model/types'

const props = withDefaults(
  defineProps<{
    categories?: { key: string; label: string }[]
    view?: 'order' | 'resource'
    /** 当前图上的模型。给出时图例只列模型里真实存在的语义;不给时按"全部可能"展示(文档/演示)。 */
    model?: ScheduleModel
  }>(),
  { view: 'order' },
)

// 对消费方的联动钩子:图例内只 emit + 维护自身视觉态(淡化该行),不直接操作引擎。
const emit = defineEmits<{ 'toggle-category': [payload: { key: string; visible: boolean }] }>()

// 分组主键(稳定 key,与视图无关);工序分色一行的项由 props.categories 动态填充。
type GroupKey = 'category' | 'gantt' | 'card' | 'status' | 'block' | 'calendar'

/** 图例项的画法。每种画法对应图上一个真实存在的视觉语言。 */
type ItemKind =
  | 'category'
  | 'baseline'
  | 'link'
  | 'milestone'
  | 'priority'
  | 'rush'
  | 'kitting'
  | 'changeover'
  | 'bottleneck'
  | 'conflict'
  | 'locked'
  | 'block'
  | 'nonWorking'
  | 'shift'
  | 'now'

interface LegendItem {
  key: string
  label: string
  kind: ItemKind
  /** category / block 用:色板变量名。 */
  token?: string
}

// 有模型就照模型说话;没有模型(组件库文档 / 演示挂载)才用包内的「全部可能」常量,
// 组件自己不再手写一份语义形状。
const semantics = computed(() =>
  props.model ? deriveLegendSemantics(props.model) : FULL_LEGEND_SEMANTICS,
)

const groups = computed<{ key: GroupKey; label: string; items: LegendItem[] }[]>(() => {
  const s = semantics.value
  const isOrder = props.view === 'order'
  const all: { key: GroupKey; label: string; items: LegendItem[] }[] = [
    {
      key: 'category',
      label: '工序分色',
      items: (props.categories ?? []).map((c) => ({
        key: c.key,
        label: c.label,
        kind: 'category' as const,
        token: `--nv-scheduling-category-${c.key}`,
      })),
    },
    {
      key: 'gantt',
      label: '甘特语义',
      // TODO(MAN-675 / #1242,关键路径):模型无 critical-path 字段、引擎不渲染,故图例不列该项;
      // 后端补 APS 关键路径标记 + 引擎着色后,在这里加一项并同步 deriveLegendSemantics。
      items: isOrder
        ? ([
            s.gantt.baseline && { key: 'baseline', label: '计划基线', kind: 'baseline' },
            s.gantt.link && { key: 'link', label: '依赖箭头', kind: 'link' },
            s.gantt.milestone && { key: 'milestone', label: '里程碑', kind: 'milestone' },
          ].filter(Boolean) as LegendItem[])
        : [],
    },
    {
      key: 'card',
      label: '卡片',
      items: isOrder
        ? []
        : ([
            s.card.priority && { key: 'priority', label: '优先级', kind: 'priority' },
            s.card.rush && { key: 'rush', label: '插单', kind: 'rush' },
            s.card.kitting && { key: 'kitting', label: '齐套 足 / 缺 / 危', kind: 'kitting' },
            s.card.changeover && { key: 'changeover', label: '换型耗时', kind: 'changeover' },
            s.card.bottleneck && { key: 'bottleneck', label: '资源过载', kind: 'bottleneck' },
          ].filter(Boolean) as LegendItem[]),
    },
    {
      key: 'status',
      label: '状态',
      items: [
        s.status.conflict && { key: 'conflict', label: '冲突', kind: 'conflict' },
        s.status.locked && { key: 'locked', label: '锁定', kind: 'locked' },
      ].filter(Boolean) as LegendItem[],
    },
    {
      key: 'block',
      label: '阻塞',
      items: s.blocks.map((kind) => ({
        key: kind,
        label: BLOCK_LABELS[kind],
        kind: 'block' as const,
        token: BLOCK_TOKENS[kind],
      })),
    },
    {
      key: 'calendar',
      label: '日历',
      items: [
        s.calendar.nonWorking && { key: 'nonWorking', label: '非工作时段', kind: 'nonWorking' },
        s.calendar.shift && { key: 'shift', label: '班次边界', kind: 'shift' },
        s.calendar.now && { key: 'now', label: '现在', kind: 'now' },
      ].filter(Boolean) as LegendItem[],
    },
  ]
  return all.filter((g) => g.items.length > 0)
})

// 折叠态(默认全部展开)与显隐态(默认全部可见),按 GroupKey 各自维护。
const collapsed = reactive<Record<string, boolean>>({})
const hidden = reactive<Record<string, boolean>>({})

function toggleCollapse(key: GroupKey) {
  collapsed[key] = !collapsed[key]
}
function toggleVisible(key: GroupKey) {
  const next = !(hidden[key] ?? false)
  hidden[key] = next
  emit('toggle-category', { key, visible: !next })
}
</script>

<template>
  <div class="border-t border-border/50 bg-card/60 text-xs text-muted-foreground">
    <!-- 每个分类一行:左标题(折叠指示 + 名称 + 显隐开关),右图例项。行间发丝分隔。 -->
    <div
      v-for="group in groups"
      :key="group.key"
      class="nerv-leg-row"
      :class="{ 'nerv-leg-row-hidden': hidden[group.key] }"
    >
      <button
        type="button"
        class="nerv-leg-head"
        :aria-expanded="!collapsed[group.key]"
        @click="toggleCollapse(group.key)"
      >
        <ChevronRightIcon
          class="nerv-leg-chevron"
          :class="{ 'nerv-leg-chevron-open': !collapsed[group.key] }"
          aria-hidden="true"
        />
        <span class="nerv-leg-title">{{ group.label }}</span>
      </button>
      <button
        type="button"
        class="nerv-leg-eye"
        :aria-pressed="!!hidden[group.key]"
        :aria-label="hidden[group.key] ? `显示 ${group.label}` : `隐藏 ${group.label}`"
        @click="toggleVisible(group.key)"
      >
        <component
          :is="hidden[group.key] ? EyeOffIcon : EyeIcon"
          class="size-3.5"
          aria-hidden="true"
        />
      </button>
      <div v-show="!collapsed[group.key]" class="nerv-leg-items">
        <span
          v-for="item in group.items"
          :key="item.key"
          class="inline-flex items-center gap-1.5"
          :class="{ 'gap-1': item.kind === 'rush' || item.kind === 'kitting' }"
          :style="item.kind === 'rush' ? { color: 'var(--nv-scheduling-rush)' } : undefined"
        >
          <!-- ① 工序分色:与条形同色 -->
          <span
            v-if="item.kind === 'category'"
            class="h-2.5 w-6 rounded-[3px]"
            :style="{ background: `var(${item.token})` }"
          ></span>
          <!-- ② 甘特语义 -->
          <span
            v-else-if="item.kind === 'baseline'"
            class="h-2.5 w-6 rounded-[3px] border border-dashed border-muted-foreground/50 bg-muted-foreground/15"
          ></span>
          <svg
            v-else-if="item.kind === 'link'"
            width="20"
            height="10"
            viewBox="0 0 20 10"
            aria-hidden="true"
            class="text-muted-foreground"
          >
            <path d="M1 5h13" fill="none" stroke="currentColor" stroke-width="1.4" />
            <path
              d="M12 2l4 3-4 3"
              fill="none"
              stroke="currentColor"
              stroke-width="1.4"
              stroke-linejoin="round"
            />
          </svg>
          <span
            v-else-if="item.kind === 'milestone'"
            class="size-2.5 rotate-45 rounded-[2px] bg-brand"
          ></span>
          <!-- ③ 卡片 -->
          <span
            v-else-if="item.kind === 'priority'"
            class="rounded bg-destructive/15 px-1 py-px text-[0.58rem] font-bold text-destructive"
            >高</span
          >
          <svg
            v-else-if="item.kind === 'rush'"
            viewBox="0 0 24 24"
            width="12"
            height="12"
            fill="none"
            stroke="currentColor"
            stroke-width="2.1"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path
              d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"
            />
          </svg>
          <template v-else-if="item.kind === 'kitting'">
            <span
              class="size-1.5 rounded-full"
              style="background: var(--nv-scheduling-kit-ok)"
            ></span>
            <span
              class="size-1.5 rounded-full"
              style="background: var(--nv-scheduling-kit-warn)"
            ></span>
            <span
              class="size-1.5 rounded-full"
              style="background: var(--nv-scheduling-kit-bad)"
            ></span>
          </template>
          <span
            v-else-if="item.kind === 'changeover'"
            class="rounded bg-foreground/10 px-1.5 py-px text-[0.58rem] font-semibold"
            >换型</span
          >
          <span
            v-else-if="item.kind === 'bottleneck'"
            class="rounded-[3px] bg-destructive/15 px-1.5 py-px text-[0.58rem] font-bold text-destructive"
            >瓶颈</span
          >
          <!-- ④ 状态 -->
          <span
            v-else-if="item.kind === 'conflict'"
            class="h-2.5 w-6 rounded-[3px] border-2 border-destructive bg-destructive/20"
          ></span>
          <span
            v-else-if="item.kind === 'locked'"
            class="h-2.5 w-6 rounded-[3px] border border-dashed border-brand/70"
          ></span>
          <!-- ⑤ 阻塞:斜纹按块类型着色 -->
          <span
            v-else-if="item.kind === 'block'"
            class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]"
            :style="{ '--h': `var(${item.token})` }"
          ></span>
          <!-- ⑥ 日历 -->
          <span
            v-else-if="item.kind === 'nonWorking'"
            class="h-2.5 w-6 rounded-[3px] bg-foreground/[0.06]"
          ></span>
          <span
            v-else-if="item.kind === 'shift'"
            class="h-3.5 w-0 border-l border-dashed border-foreground/40"
          ></span>
          <span v-else-if="item.kind === 'now'" class="h-3.5 w-0.5 rounded-full bg-brand"></span>
          {{ item.label }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  /* 每类一行:标题 | 显隐开关 | 图例项。行与行之间清晰发丝分隔(比 border/50 更实,应用户"分割线不明显")。 */
  .nerv-leg-row {
    display: grid;
    grid-template-columns: auto auto 1fr;
    align-items: start;
    gap: 0.5rem 0.75rem;
    padding: 0.4rem 1rem;
    transition: opacity var(--nv-duration-base) var(--nv-ease-out-expo);
  }
  .nerv-leg-row + .nerv-leg-row {
    border-top: 1px solid color-mix(in oklch, var(--border), transparent 40%);
  }
  /* 已隐藏该类:整行淡化(视觉态钩子,消费方在图上真正隐藏) */
  .nerv-leg-row-hidden {
    opacity: 0.4;
  }

  /* 分类标题:可点击折叠,chevron 旋转过渡走统一缓动 */
  .nerv-leg-head {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    min-width: 5.5rem;
    padding: 0.1rem 0;
    color: var(--foreground);
    font-weight: 600;
    letter-spacing: 0.01em;
    cursor: pointer;
    transition: color var(--nv-duration-base) var(--nv-ease-out-expo);
  }
  .nerv-leg-head:hover {
    color: var(--nv-brand);
  }
  .nerv-leg-head:focus-visible {
    outline: 2px solid var(--ring);
    outline-offset: 2px;
    border-radius: 4px;
  }
  .nerv-leg-title {
    white-space: nowrap;
  }
  .nerv-leg-chevron {
    width: 0.85rem;
    height: 0.85rem;
    flex: none;
    transition: transform var(--nv-duration-base) var(--nv-ease-out-expo);
  }
  .nerv-leg-chevron-open {
    transform: rotate(90deg);
  }

  /* 显隐开关:小图标按钮,弱化默认、hover/激活提亮 */
  .nerv-leg-eye {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    align-self: center;
    color: var(--muted-foreground);
    cursor: pointer;
    transition: color var(--nv-duration-base) var(--nv-ease-out-expo);
  }
  .nerv-leg-eye:hover,
  .nerv-leg-eye[aria-pressed='true'] {
    color: var(--foreground);
  }
  .nerv-leg-eye:focus-visible {
    outline: 2px solid var(--ring);
    outline-offset: 2px;
    border-radius: 4px;
  }

  /* 该类图例项:窄屏可 wrap,分类分行结构不变 */
  .nerv-leg-items {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.375rem 1.25rem;
    min-width: 0;
    padding-top: 0.05rem;
  }

  /* 资源时间块斜纹 swatch(复用原画法) */
  .nerv-leg-hatch {
    background-color: color-mix(in srgb, var(--h) 12%, transparent);
    background-image: repeating-linear-gradient(
      -45deg,
      transparent 0,
      transparent 2px,
      color-mix(in srgb, var(--h) 50%, transparent) 2px,
      color-mix(in srgb, var(--h) 50%, transparent) 3px
    );
    border: 1px solid color-mix(in srgb, var(--h) 45%, transparent);
  }

  @media (prefers-reduced-motion: reduce) {
    .nerv-leg-row,
    .nerv-leg-head,
    .nerv-leg-chevron,
    .nerv-leg-eye {
      transition: none;
    }
  }
}
</style>
