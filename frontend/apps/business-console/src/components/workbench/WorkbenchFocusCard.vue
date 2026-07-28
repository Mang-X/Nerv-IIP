<script setup lang="ts">
import type { Component } from 'vue'
import { computed } from 'vue'
import { AlertTriangleIcon, ArrowRightIcon, PlugZapIcon, RefreshCwIcon } from '@lucide/vue'
import { NvButton, NvCard, Skeleton, cn } from '@nerv-iip/ui'
import { RouterLink } from 'vue-router'

/**
 * 工作台「行动卡」——待办 / 消息 / 设备预警共用的一张卡。
 *
 * 结构固定为四段：图标 + 主张（这张卡要你做什么）→ 数量读数 → 最近 4 条真实条目
 * → 去处理的出口。列表只作为「值不值得点进去」的证据，不做成迷你表格；条目主行
 * 一律用 facade 返回的人读编码（PO-… / WO-… / 设备号），副行才是状态与时间。
 *
 * 五态都有设计过的形态，**卡片外形恒定**：加载中在卡内出骨架（读数位也是骨架，
 * 不先亮一个 0 再跳变）、有数据出条目、清空出空态；空态同样保留 CTA，因为"这一路
 * 已经清空"仍然需要出口。
 *
 * **失败态 / 不可用态与空态严格分开**（曾踩坑：卡片只按 `items.length === 0` 判空，
 * 设备预警接口挂掉时照样渲染成「0 条 / 设备当前运行正常」，把系统故障伪装成现场安全）。
 * 只要 `error` 非空或 `unavailable` 为真，读数位一律显 `—`，绝不用 0 冒充"已接入且为零"，
 * 文案也只说"取不到数据、无法判断"，不出现任何"暂无 / 已清空 / 运行正常"式安慰话。
 */
export interface WorkbenchFocusItem {
  key: string
  /** 主行：人读编码或事件主体。 */
  primary: string
  /** 副行：状态 · 时间 · 来源。 */
  secondary: string
}

export type WorkbenchFocusTone = 'brand' | 'warning' | 'danger' | 'success' | 'neutral'

const props = withDefaults(
  defineProps<{
    title: string
    /** 主张：这张卡要求使用者做什么，一句话，不写用途说明。 */
    description: string
    icon: Component
    tone?: WorkbenchFocusTone
    count: number
    unit?: string
    /** 右上角补充读数，例如「紧急 2」。 */
    badge?: string
    items?: WorkbenchFocusItem[]
    emptyTitle: string
    emptyHint: string
    /** 出口路由；省略时不渲染 CTA（该域当前没有落地页）。 */
    to?: string
    actionLabel?: string
    pending?: boolean
    /**
     * 该来源本次读取失败（非空即失败）。失败时读数显 `—`、卡内出失败态与重试入口，
     * 不再落到空态——「取不到数」和「真的没有」是两件事。
     */
    error?: unknown
    /**
     * 该来源未接入 / 无权限 / 暂不可用。与失败态同样显 `—`，但不给重试（重试无意义），
     * 只说明为什么这一路现在无法判断。
     */
    unavailable?: boolean
    /** 不可用原因的一句话说明；省略时给通用说明。 */
    unavailableHint?: string
  }>(),
  { tone: 'brand', items: () => [], pending: false, unavailable: false },
)

const emit = defineEmits<{ retry: [] }>()

/** tone → 图标底色（与 NvStatusBadge / NvMetricCard 的色阶保持同一套语义）。 */
const toneTint: Record<WorkbenchFocusTone, string> = {
  brand: 'bg-brand/10 text-brand-strong',
  success: 'bg-success/10 text-success-strong',
  warning: 'bg-warning/15 text-warning-strong',
  danger: 'bg-destructive/10 text-destructive-strong',
  neutral: 'bg-muted text-muted-foreground',
}

const toneText: Record<WorkbenchFocusTone, string> = {
  brand: 'text-brand-strong',
  success: 'text-success-strong',
  warning: 'text-warning-strong',
  danger: 'text-destructive-strong',
  neutral: 'text-muted-foreground',
}

/**
 * 状态优先级：加载中 → 读取失败 → 来源不可用 → 有条目 → 真的清空。
 * 失败 / 不可用一定压过"空"，否则又会回到"故障显示成一切正常"。
 */
const failed = computed(() => !props.pending && props.error != null)
const unavailableState = computed(() => !props.pending && !failed.value && props.unavailable)
const trustworthy = computed(() => !props.pending && !failed.value && !unavailableState.value)

/** 条目上限 4 条：够撑起 1080 首屏的行动区高度，又不至于把卡片做成迷你表格。 */
const visibleItems = computed(() => (trustworthy.value ? props.items.slice(0, 4) : []))
const showEmptyState = computed(() => trustworthy.value && visibleItems.value.length === 0)

/** 读数：只有可信时才是数字，其余一律 `—`。 */
const readout = computed(() => (trustworthy.value ? String(props.count) : '—'))

const unavailableText = computed(
  () =>
    props.unavailableHint ?? `${props.title}来源未接入或当前账号无权查看，工作台无法统计这一路。`,
)
</script>

<template>
  <NvCard class="flex min-h-[19rem] flex-col overflow-hidden p-0" :data-focus="title">
    <div class="flex items-start gap-3 px-5 pt-5">
      <span :class="cn('grid size-10 flex-none place-items-center rounded-[10px]', toneTint[tone])">
        <component :is="icon" class="size-5" aria-hidden="true" />
      </span>
      <div class="min-w-0 flex-1">
        <div class="flex items-baseline gap-2">
          <h2 class="truncate text-sm font-semibold text-foreground">{{ title }}</h2>
          <span
            v-if="badge && trustworthy"
            :class="cn('shrink-0 text-xs font-semibold', toneText[tone])"
          >
            {{ badge }}
          </span>
        </div>
        <p class="mt-1 text-sm leading-6 text-muted-foreground">{{ description }}</p>
      </div>
      <Skeleton v-if="pending" class="h-7 w-12 shrink-0 rounded-md" />
      <!-- 读数位：不可信时只出一个破折号，绝不显 0 -->
      <p
        v-else-if="!trustworthy"
        class="shrink-0 text-3xl font-semibold leading-none tracking-tight text-muted-foreground"
        aria-label="数据不可用"
      >
        —
      </p>
      <p v-else class="shrink-0 text-3xl font-semibold leading-none tabular-nums tracking-tight">
        {{ readout
        }}<span v-if="unit" class="ml-0.5 text-sm font-medium text-muted-foreground">{{
          unit
        }}</span>
      </p>
    </div>

    <div class="mt-4 flex flex-1 flex-col">
      <!-- 加载态：条目位出骨架，卡片外形与有数据时一致，不做高度跳变 -->
      <ul v-if="pending" class="flex flex-col divide-y border-t" aria-hidden="true">
        <li v-for="row in 3" :key="row" class="flex items-center justify-between gap-3 px-5 py-3">
          <Skeleton class="h-4 w-32 rounded" />
          <Skeleton class="h-3 w-20 rounded" />
        </li>
      </ul>

      <!-- 失败态：说清"取不到数、无法判断"，并给重试；不出现任何"暂无 / 正常"式措辞 -->
      <div
        v-else-if="failed"
        class="flex flex-1 flex-col items-center justify-center gap-2 border-t px-6 py-6 text-center"
        role="alert"
      >
        <span class="grid size-10 place-items-center rounded-full bg-destructive/10">
          <AlertTriangleIcon class="size-5 text-destructive-strong" aria-hidden="true" />
        </span>
        <p class="text-sm font-medium text-destructive-strong">{{ title }}读取失败</p>
        <p class="text-sm leading-6 text-muted-foreground">
          没有取到{{ title }}数据，无法判断当前是否有需要处理的事项。
        </p>
        <NvButton class="mt-1" size="sm" type="button" variant="outline" @click="emit('retry')">
          <RefreshCwIcon aria-hidden="true" />
          重试
        </NvButton>
      </div>

      <!-- 不可用态：来源未接入 / 无权限，重试也不会有数，只说明原因 -->
      <div
        v-else-if="unavailableState"
        class="flex flex-1 flex-col items-center justify-center gap-2 border-t px-6 py-6 text-center"
        role="alert"
      >
        <span class="grid size-10 place-items-center rounded-full bg-muted">
          <PlugZapIcon class="size-5 text-muted-foreground" aria-hidden="true" />
        </span>
        <p class="text-sm font-medium text-foreground">{{ title }}暂时无法统计</p>
        <p class="text-sm leading-6 text-muted-foreground">{{ unavailableText }}</p>
      </div>

      <ul v-else-if="visibleItems.length > 0" class="flex flex-col divide-y border-t">
        <li
          v-for="item in visibleItems"
          :key="item.key"
          class="flex min-w-0 items-baseline justify-between gap-3 px-5 py-2.5"
        >
          <span class="truncate text-sm font-medium text-foreground">{{ item.primary }}</span>
          <span class="shrink-0 text-xs tabular-nums text-muted-foreground">{{
            item.secondary
          }}</span>
        </li>
      </ul>

      <div
        v-else-if="showEmptyState"
        class="flex flex-1 flex-col items-center justify-center gap-1 border-t px-6 py-6 text-center"
      >
        <p class="text-sm font-medium text-foreground">{{ emptyTitle }}</p>
        <p class="text-sm leading-6 text-muted-foreground">{{ emptyHint }}</p>
      </div>
    </div>

    <RouterLink
      v-if="to"
      class="group flex items-center justify-between gap-2 border-t px-5 py-3 text-sm font-medium text-brand-strong transition-colors hover:bg-accent"
      :to="to"
    >
      {{ actionLabel ?? '查看全部' }}
      <ArrowRightIcon
        class="size-4 transition-transform group-hover:translate-x-0.5"
        aria-hidden="true"
      />
    </RouterLink>
  </NvCard>
</template>
