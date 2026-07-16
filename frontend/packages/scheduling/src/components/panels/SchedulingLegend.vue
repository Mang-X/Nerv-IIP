<script setup lang="ts">
// 图例:讲清当前视图的视觉语言。分色与条形共用 --nv-scheduling-category-* 全局变量,保证图例与条形一致。
// 视图感知:资源排产板隐藏甘特专属(计划基线/依赖/里程碑),改讲齐套/换型/瓶颈。
// 结构:按分类分组、每类一行(分类标题 + 该类图例项)。每类可折叠(本地 ref)、可显隐(emit + 自身淡化)。
import { ChevronRightIcon, EyeIcon, EyeOffIcon } from '@lucide/vue'
import { reactive } from 'vue'

const props = withDefaults(
  defineProps<{ categories?: { key: string; label: string }[]; view?: 'order' | 'resource' }>(),
  { view: 'order' },
)

// 对消费方的联动钩子:图例内只 emit + 维护自身视觉态(淡化该行),不直接操作引擎。
const emit = defineEmits<{ 'toggle-category': [payload: { key: string; visible: boolean }] }>()

// 分组主键(稳定 key,与视图无关);工序分色一行的项由 props.categories 动态填充。
type GroupKey = 'category' | 'gantt' | 'card' | 'status' | 'block' | 'calendar'

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

    <!-- ① 工序分色(与条形同色) -->
    <div
      v-if="categories?.length"
      class="nerv-leg-row"
      :class="{ 'nerv-leg-row-hidden': hidden.category }"
    >
      <button type="button" class="nerv-leg-head" :aria-expanded="!collapsed.category" @click="toggleCollapse('category')">
        <ChevronRightIcon class="nerv-leg-chevron" :class="{ 'nerv-leg-chevron-open': !collapsed.category }" aria-hidden="true" />
        <span class="nerv-leg-title">工序分色</span>
      </button>
      <button
        type="button"
        class="nerv-leg-eye"
        :aria-pressed="!!hidden.category"
        :aria-label="hidden.category ? '显示 工序分色' : '隐藏 工序分色'"
        @click="toggleVisible('category')"
      >
        <component :is="hidden.category ? EyeOffIcon : EyeIcon" class="size-3.5" aria-hidden="true" />
      </button>
      <div v-show="!collapsed.category" class="nerv-leg-items">
        <span v-for="c in categories" :key="c.key" class="inline-flex items-center gap-1.5">
          <span class="h-2.5 w-6 rounded-[3px]" :style="{ background: `var(--nv-scheduling-category-${c.key})` }"></span>
          {{ c.label }}
        </span>
      </div>
    </div>

    <!-- ====================== 工单甘特 ====================== -->
    <template v-if="view === 'order'">
      <!-- ② 甘特语言:计划基线 / 依赖箭头 / 里程碑 -->
      <div class="nerv-leg-row" :class="{ 'nerv-leg-row-hidden': hidden.gantt }">
        <button type="button" class="nerv-leg-head" :aria-expanded="!collapsed.gantt" @click="toggleCollapse('gantt')">
          <ChevronRightIcon class="nerv-leg-chevron" :class="{ 'nerv-leg-chevron-open': !collapsed.gantt }" aria-hidden="true" />
          <span class="nerv-leg-title">甘特语言</span>
        </button>
        <button
          type="button"
          class="nerv-leg-eye"
          :aria-pressed="!!hidden.gantt"
          :aria-label="hidden.gantt ? '显示 甘特语言' : '隐藏 甘特语言'"
          @click="toggleVisible('gantt')"
        >
          <component :is="hidden.gantt ? EyeOffIcon : EyeIcon" class="size-3.5" aria-hidden="true" />
        </button>
        <div v-show="!collapsed.gantt" class="nerv-leg-items">
          <span class="inline-flex items-center gap-1.5">
            <span class="h-2.5 w-6 rounded-[3px] border border-dashed border-muted-foreground/50 bg-muted-foreground/15"></span>计划基线
          </span>
          <span class="inline-flex items-center gap-1.5">
            <svg width="20" height="10" viewBox="0 0 20 10" aria-hidden="true" class="text-muted-foreground">
              <path d="M1 5h13" fill="none" stroke="currentColor" stroke-width="1.4" />
              <path d="M12 2l4 3-4 3" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round" />
            </svg>
            依赖箭头
          </span>
          <!-- TODO(关键路径):模型无 critical-path 字段、引擎不渲染,故图例暂不列该项;
               后端补 APS 关键路径标记 + 引擎着色后再恢复对应图例。 -->
          <span class="inline-flex items-center gap-1.5">
            <span class="size-2.5 rotate-45 rounded-[2px] bg-brand"></span>里程碑
          </span>
        </div>
      </div>

      <!-- ③ 状态:冲突 / 锁定 -->
      <div class="nerv-leg-row" :class="{ 'nerv-leg-row-hidden': hidden.status }">
        <button type="button" class="nerv-leg-head" :aria-expanded="!collapsed.status" @click="toggleCollapse('status')">
          <ChevronRightIcon class="nerv-leg-chevron" :class="{ 'nerv-leg-chevron-open': !collapsed.status }" aria-hidden="true" />
          <span class="nerv-leg-title">状态</span>
        </button>
        <button
          type="button"
          class="nerv-leg-eye"
          :aria-pressed="!!hidden.status"
          :aria-label="hidden.status ? '显示 状态' : '隐藏 状态'"
          @click="toggleVisible('status')"
        >
          <component :is="hidden.status ? EyeOffIcon : EyeIcon" class="size-3.5" aria-hidden="true" />
        </button>
        <div v-show="!collapsed.status" class="nerv-leg-items">
          <span class="inline-flex items-center gap-1.5">
            <span class="h-2.5 w-6 rounded-[3px] border-2 border-destructive bg-destructive/25"></span>冲突
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="h-2.5 w-6 rounded-[3px] border border-dashed border-brand/70"></span>锁定
          </span>
        </div>
      </div>

      <!-- ④ 工作日历:非工作·夜班 / 现在线 -->
      <div class="nerv-leg-row" :class="{ 'nerv-leg-row-hidden': hidden.calendar }">
        <button type="button" class="nerv-leg-head" :aria-expanded="!collapsed.calendar" @click="toggleCollapse('calendar')">
          <ChevronRightIcon class="nerv-leg-chevron" :class="{ 'nerv-leg-chevron-open': !collapsed.calendar }" aria-hidden="true" />
          <span class="nerv-leg-title">工作日历</span>
        </button>
        <button
          type="button"
          class="nerv-leg-eye"
          :aria-pressed="!!hidden.calendar"
          :aria-label="hidden.calendar ? '显示 工作日历' : '隐藏 工作日历'"
          @click="toggleVisible('calendar')"
        >
          <component :is="hidden.calendar ? EyeOffIcon : EyeIcon" class="size-3.5" aria-hidden="true" />
        </button>
        <div v-show="!collapsed.calendar" class="nerv-leg-items">
          <span class="inline-flex items-center gap-1.5">
            <span class="h-2.5 w-6 rounded-[3px] bg-muted"></span>非工作 / 夜班
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="h-3.5 w-0.5 rounded-full bg-brand"></span>现在
          </span>
        </div>
      </div>
    </template>

    <!-- ====================== 资源排产板 ====================== -->
    <template v-else>
      <!-- ② 卡片状态:优先级 / 插单 / 齐套 足缺危 / 换型 / 瓶颈 -->
      <div class="nerv-leg-row" :class="{ 'nerv-leg-row-hidden': hidden.card }">
        <button type="button" class="nerv-leg-head" :aria-expanded="!collapsed.card" @click="toggleCollapse('card')">
          <ChevronRightIcon class="nerv-leg-chevron" :class="{ 'nerv-leg-chevron-open': !collapsed.card }" aria-hidden="true" />
          <span class="nerv-leg-title">卡片状态</span>
        </button>
        <button
          type="button"
          class="nerv-leg-eye"
          :aria-pressed="!!hidden.card"
          :aria-label="hidden.card ? '显示 卡片状态' : '隐藏 卡片状态'"
          @click="toggleVisible('card')"
        >
          <component :is="hidden.card ? EyeOffIcon : EyeIcon" class="size-3.5" aria-hidden="true" />
        </button>
        <div v-show="!collapsed.card" class="nerv-leg-items">
          <span class="inline-flex items-center gap-1.5">
            <span class="rounded bg-destructive/15 px-1 py-px text-[0.58rem] font-bold text-destructive">高</span>优先级
          </span>
          <span class="inline-flex items-center gap-1" style="color: var(--nv-scheduling-rush)">
            <svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z" /></svg>
            <span class="text-muted-foreground">插单</span>
          </span>
          <span class="inline-flex items-center gap-1">
            <span class="size-1.5 rounded-full" style="background: var(--nv-scheduling-kit-ok)"></span>
            <span class="size-1.5 rounded-full" style="background: var(--nv-scheduling-kit-warn)"></span>
            <span class="size-1.5 rounded-full" style="background: var(--nv-scheduling-kit-bad)"></span>
            齐套 足 / 缺 / 危
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="rounded bg-foreground/10 px-1.5 py-px text-[0.58rem] font-semibold">换型</span>换型耗时
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="rounded-[3px] bg-destructive/15 px-1.5 py-px text-[0.58rem] font-bold text-destructive">瓶颈</span>资源过载
          </span>
        </div>
      </div>

      <!-- ③ 状态:冲突 / 锁定 -->
      <div class="nerv-leg-row" :class="{ 'nerv-leg-row-hidden': hidden.status }">
        <button type="button" class="nerv-leg-head" :aria-expanded="!collapsed.status" @click="toggleCollapse('status')">
          <ChevronRightIcon class="nerv-leg-chevron" :class="{ 'nerv-leg-chevron-open': !collapsed.status }" aria-hidden="true" />
          <span class="nerv-leg-title">状态</span>
        </button>
        <button
          type="button"
          class="nerv-leg-eye"
          :aria-pressed="!!hidden.status"
          :aria-label="hidden.status ? '显示 状态' : '隐藏 状态'"
          @click="toggleVisible('status')"
        >
          <component :is="hidden.status ? EyeOffIcon : EyeIcon" class="size-3.5" aria-hidden="true" />
        </button>
        <div v-show="!collapsed.status" class="nerv-leg-items">
          <span class="inline-flex items-center gap-1.5">
            <span class="h-2.5 w-6 rounded-[3px] border-2 border-destructive bg-destructive/15"></span>冲突
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="h-2.5 w-6 rounded-[3px] border border-dashed border-muted-foreground/70"></span>锁定(虚线框)
          </span>
        </div>
      </div>

      <!-- ④ 资源时间块:维护 / 停机 / 换线 / 换型(斜纹) -->
      <div class="nerv-leg-row" :class="{ 'nerv-leg-row-hidden': hidden.block }">
        <button type="button" class="nerv-leg-head" :aria-expanded="!collapsed.block" @click="toggleCollapse('block')">
          <ChevronRightIcon class="nerv-leg-chevron" :class="{ 'nerv-leg-chevron-open': !collapsed.block }" aria-hidden="true" />
          <span class="nerv-leg-title">资源时间块</span>
        </button>
        <button
          type="button"
          class="nerv-leg-eye"
          :aria-pressed="!!hidden.block"
          :aria-label="hidden.block ? '显示 资源时间块' : '隐藏 资源时间块'"
          @click="toggleVisible('block')"
        >
          <component :is="hidden.block ? EyeOffIcon : EyeIcon" class="size-3.5" aria-hidden="true" />
        </button>
        <div v-show="!collapsed.block" class="nerv-leg-items">
          <span class="inline-flex items-center gap-1.5">
            <span class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]" style="--h: var(--nv-scheduling-block-maintenance)"></span>设备维护
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]" style="--h: var(--nv-scheduling-block-downtime)"></span>计划停机
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]" style="--h: var(--nv-scheduling-block-linechange)"></span>换线窗口
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="nerv-leg-hatch h-2.5 w-6 rounded-[3px]" style="--h: var(--nv-scheduling-block-changeover)"></span>换型窗口
          </span>
        </div>
      </div>

      <!-- ⑤ 工作日历:非工作·夜班 / 现在线 -->
      <div class="nerv-leg-row" :class="{ 'nerv-leg-row-hidden': hidden.calendar }">
        <button type="button" class="nerv-leg-head" :aria-expanded="!collapsed.calendar" @click="toggleCollapse('calendar')">
          <ChevronRightIcon class="nerv-leg-chevron" :class="{ 'nerv-leg-chevron-open': !collapsed.calendar }" aria-hidden="true" />
          <span class="nerv-leg-title">工作日历</span>
        </button>
        <button
          type="button"
          class="nerv-leg-eye"
          :aria-pressed="!!hidden.calendar"
          :aria-label="hidden.calendar ? '显示 工作日历' : '隐藏 工作日历'"
          @click="toggleVisible('calendar')"
        >
          <component :is="hidden.calendar ? EyeOffIcon : EyeIcon" class="size-3.5" aria-hidden="true" />
        </button>
        <div v-show="!collapsed.calendar" class="nerv-leg-items">
          <span class="inline-flex items-center gap-1.5">
            <span class="h-2.5 w-6 rounded-[3px] bg-foreground/[0.05]"></span>非工作 / 夜班
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span class="h-3.5 w-0.5 rounded-full bg-brand"></span>现在
          </span>
        </div>
      </div>
    </template>
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
