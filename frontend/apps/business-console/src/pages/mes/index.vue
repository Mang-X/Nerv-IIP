<script setup lang="ts">
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { readStateNote, readStateValue } from '@/composables/businessReadState'
import {
  describeMesReadinessReason,
  mesWorkScopeKindLabel,
  useMesOperationTasks,
  useMesOverview,
} from '@/composables/useBusinessMes'
import { labelFor, MES_READINESS_AREA_LABELS } from '@/data/businessLabels'
import { type KpiPolarity, buildKpiTrend } from '@/utils/kpiTrend'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCard,
  NvCardContent,
  NvDataTable,
  NvMetricStrip,
  NvPageHeader,
  cn,
} from '@nerv-iip/ui'
import {
  ArrowRightIcon,
  ClipboardCheckIcon,
  FactoryIcon,
  PackageCheckIcon,
  RefreshCwIcon,
  ShieldAlertIcon,
} from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { inlineErrorMessage } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '生产驾驶舱',
    requiredPermissions: ['business.mes.overview.read'],
  },
})

const {
  blockers,
  counts,
  overviewError,
  overviewPending,
  overviewState,
  pendingWork,
  refreshOverview,
} = useMesOverview()

// 这一页的结论（有没有阻塞、能不能放行）只有在真的读到数据时才成立。
// 读面失败 / 未读取时，`blockers` 是空数组、各项计数是 0——若照直渲染，屏幕就会对着车间管理者
// 断言「现场无阻塞」。所以本页所有数字与结论一律以 overviewState 为准，非 ready 一律显式说「取不到」。
const isReady = computed(() => overviewState.value === 'ready')
const stateNote = computed(() => readStateNote(overviewState.value))
const errorMessage = computed(() => formatError(overviewError.value))
// 顶部状态条：只在「没读到」时出现，且必须说清楚原因 + 给重试出路，不含任何「正常 / 无阻塞」措辞。
const readNotice = computed(() => {
  if (overviewState.value === 'idle') {
    return {
      text: '尚未选择业务范围（组织与环境），现场数据未读取，暂时无法判断产线状态。',
      class: 'border-border bg-muted/40 text-muted-foreground',
      retry: false,
    }
  }
  if (overviewState.value === 'error') {
    return {
      text: errorMessage.value
        ? `现场数据获取失败，无法判断当前是否存在阻塞：${errorMessage.value}`
        : '现场数据获取失败，无法判断当前是否存在阻塞。',
      class: 'border-destructive/40 bg-destructive/5 text-destructive',
      retry: true,
    }
  }
  return null
})
// facade 回的 count key 是 kebab-case（work-orders / operation-tasks）。
// 曾按 PascalCase 取值，于是两个总量恒为 0、整个驾驶舱看着像没数据——
// 这里按「去分隔符 + 小写」归一化匹配，两种写法都认。
const workOrderCount = computed(() => countValue('work-orders'))
const operationTaskCount = computed(() => countValue('operation-tasks'))
const blockerCount = computed(() =>
  blockers.value.reduce((total, item) => total + (item.count ?? 0), 0),
)
const pendingWorkCount = computed(() =>
  pendingWork.value.reduce((total, item) => total + (item.count ?? 0), 0),
)

// 指挥卡已经承担「先看什么、去哪里」；这一条只压缩成一行现场总量，不再重复副标题。
// 没读到数据时给 `—` 而不是 0，并在副行写明原因——0 是结论，`—` 才是「不知道」。
function metricCell(
  key: string,
  label: string,
  value: number,
  unit: string,
  tone?: NvMetricStripCell['valueTone'],
  polarity: KpiPolarity = 'neutral',
): NvMetricStripCell {
  // 走势只在**确实读到数**时才画：值是 `—` 时下面配一条线等于给"不知道"配了历史。
  // overview 读面只回当前时点计数，MES 列表读面也没有任何日期区间参数，
  // 所以这条线是由后端真值反推的形状（末点 === 卡上的数字，见 utils/kpiTrend）。
  const trend = isReady.value
    ? buildKpiTrend(`mes.${key}`, value, { kind: 'count', polarity })
    : undefined
  return {
    key,
    label,
    value: readStateValue(overviewState.value, value),
    unit: isReady.value ? unit : undefined,
    valueTone: isReady.value ? tone : undefined,
    meta: stateNote.value || undefined,
    metaTone: 'neutral',
    delta: trend?.delta,
    series: trend?.series,
    seriesLabels: trend?.seriesLabels,
    seriesUnit: isReady.value ? unit : undefined,
  }
}
// 口径标注：这一条全是**全厂**总量（overview 读面不带作业范围、也不区分执行状态——
// 后端就是 WorkOrders / OperationTasks 的整表计数，含已完工已关闭）。
// 所以不能叫「在制」，也要和上面「我的班组」那块明确区分开，免得被当成同一口径读。
const overviewCells = computed<NvMetricStripCell[]>(() => [
  metricCell('work-orders', '全厂工单', workOrderCount.value, '张', undefined, 'higher-better'),
  metricCell(
    'operation-tasks',
    '全厂工序任务',
    operationTaskCount.value,
    '个',
    undefined,
    'higher-better',
  ),
  // 阻塞项与待办涨上去都是坏消息：箭头照实朝上，配色转红，别涂成绿的。
  metricCell(
    'blockers',
    '阻塞项',
    blockerCount.value,
    '项',
    blockerCount.value > 0 ? 'danger' : undefined,
    'lower-better',
  ),
  metricCell('pending', '待办', pendingWorkCount.value, '项', undefined, 'lower-better'),
])

// 阻塞指挥卡的措辞完全由读面状态决定：
// 只有确实读到了数据、且结果为空，才允许说「没有阻塞、可以继续推进」。
const blockerCard = computed(() => {
  if (overviewState.value === 'idle') {
    return {
      title: '现场状态未知',
      description: '尚未选择业务范围，没有读取现场阻塞，无法判断能否放行。',
      action: '查看异常与产能',
      route: '/mes/capacity',
      tone: 'border-border bg-muted/40',
    }
  }
  if (overviewState.value === 'loading') {
    return {
      title: '现场状态读取中',
      description: '正在读取物料、质量、设备与产能的阻塞汇总，请稍候。',
      action: '查看异常与产能',
      route: '/mes/capacity',
      tone: 'border-border bg-muted/40',
    }
  }
  if (overviewState.value === 'error') {
    return {
      title: '现场状态未知',
      description: '阻塞数据获取失败，无法判断现场是否存在阻塞，请重试后再决定是否放行。',
      action: '查看异常与产能',
      route: '/mes/capacity',
      tone: 'border-warning/40 bg-warning/10',
    }
  }
  if (blockerCount.value > 0) {
    return {
      title: '先处理阻塞',
      description: '物料、质量、设备或产能存在阻塞，先排除再放行。',
      action: '查看异常与产能',
      route: '/mes/capacity',
      tone: 'border-destructive/30 bg-destructive/5',
    }
  }
  return {
    title: '先处理阻塞',
    description: '本次读取的汇总里没有阻塞，可进入工单与派工继续推进。',
    action: '进入工单与派工',
    route: '/mes/work-orders',
    tone: 'border-success/30 bg-success/5',
  }
})

const commandCards = computed(() => [
  {
    ...blockerCard.value,
    value: readStateValue(overviewState.value, blockerCount.value),
    icon: ShieldAlertIcon,
  },
  {
    title: '安排今日工单',
    description: '查看待下达、待派工和急单影响，围绕工单推进生产节奏。',
    value: readStateValue(overviewState.value, workOrderCount.value),
    route: '/mes/work-orders',
    action: '打开工单队列',
    icon: FactoryIcon,
    tone: 'border-primary/20 bg-primary/5',
  },
  {
    title: '盯紧工序现场',
    description: '从工序任务进入报工、质检和异常记录，减少跨页面手工查找。',
    value: readStateValue(overviewState.value, operationTaskCount.value),
    route: '/mes/operation-tasks',
    action: '查看工序执行',
    icon: ClipboardCheckIcon,
    tone: 'border-brand/30 bg-brand/5',
  },
])
/**
 * 「我的班组」维度（走查台账 #50）。
 *
 * 三张行动卡讲的是全厂总量（在制 4760 单 / 工序任务 31554），班组长早上打开驾驶舱看不到
 * 「我这一摊有多少活」。工序任务读面本身就按登录人的授权作业范围（scopeKind/scopeId）过滤，
 * 且返回的 total 是过滤后的服务端计数——不新增任何后端读面就能给出真数字。
 *
 * 「今天」这一刀切不了：MES 列表读面没有任何日期区间参数，客户端只能对已取回的那一页切，
 * 而服务端按最早开工时间升序返回、页大小封顶 500，backlog 一大今天的活根本不在这一页里，
 * 切出来的数会骗人。所以这里只讲**作业范围**这一刀（精确），日期维度另立 followup。
 */
/**
 * 未终态的四个执行状态各给一格，**逐一对应后端状态码，不做归并**：
 * 曾把「进行中」写成"已开工、等着报工或完工"，实际上把 `paused` 一起讲了进去，而 `paused`
 * 根本没进任何一格；`scheduleInvalidated`（排程已失效）同样漏在外面——走查里堵住主链的
 * 533 项正是这一类。少一格就等于让班组长以为"我这摊只有这些活"。
 */
const MY_SCOPE_TASK_BUCKETS = [
  { key: 'queued', label: '待开工', meta: '已排程、尚未开工' },
  { key: 'inProgress', label: '进行中', meta: '已开工、正在做' },
  { key: 'paused', label: '已暂停', meta: '开工后被挂起，等着恢复' },
  { key: 'scheduleInvalidated', label: '排程已失效', meta: '排程作废，需重新排产才能开工' },
] as const

// 一格一次查询：只要服务端算好的 total，不需要行——页大小取 1，别为一个数字拉回 100 行。
const myScopeQueries = MY_SCOPE_TASK_BUCKETS.map((bucket) => {
  const query = useMesOperationTasks()
  query.filters.status = bucket.key
  query.filters.take = 1
  return { bucket, query }
})
const myScopeAnchor = myScopeQueries[0].query

const myScope = computed(() => myScopeAnchor.operationListScope.value)
const myScopeLabel = computed(() => {
  const scope = myScope.value
  if (!scope) return ''
  const kind = mesWorkScopeKindLabel(scope.kind)
  return scope.displayName ? `${scope.displayName}（${kind}）` : kind
})
/** 任一格还没读到就整块显「—」：范围数字宁可说不知道，也不能拿 0 当结论。 */
const myScopeReady = computed(() =>
  myScopeQueries.every(({ query }) => query.operationTasksState.value === 'ready'),
)
const myScopeNote = computed(() => {
  if (!myScope.value)
    return myScopeAnchor.operationListScopeMessage.value || '尚未确定你的作业范围。'
  if (!myScopeReady.value) return readStateNote(myScopeAnchor.operationTasksState.value)
  return `作业范围：${myScopeLabel.value}`
})
const myScopeCells = computed<NvMetricStripCell[]>(() =>
  myScopeQueries.map(({ bucket, query }) => ({
    key: `my-${bucket.key}`,
    label: `我的范围 · ${bucket.label}`,
    value: readStateValue(query.operationTasksState.value, query.operationTasksTotal.value),
    unit: myScopeReady.value ? '个' : undefined,
    meta: bucket.meta,
    metaTone: 'neutral',
  })),
)

function blockerCountByArea(keywords: string[]) {
  return blockers.value.filter((i) =>
    keywords.some((k) => (i.areaCode ?? '').toLowerCase().includes(k)),
  ).length
}
const roleLanes = computed(() => [
  {
    role: '调度员',
    focus: '工单释放、插单影响、派工顺序',
    route: '/mes/work-orders',
    count: readStateValue(overviewState.value, workOrderCount.value),
  },
  {
    role: '班组长',
    focus: '可开工任务、报工进度、班次遗留',
    route: '/mes/operation-tasks',
    count: readStateValue(overviewState.value, operationTaskCount.value),
  },
  {
    role: '物料员',
    focus: '齐套、领料、补料和退料线索',
    route: '/mes/materials',
    count: readStateValue(overviewState.value, blockerCountByArea(['material'])),
  },
  {
    role: '质检/设备',
    focus: '质量阻塞、停机、产能影响',
    route: '/mes/capacity',
    count: readStateValue(
      overviewState.value,
      blockerCountByArea(['quality', 'equipment', 'capacity']),
    ),
  },
])

// 角色码 → 一线称呼；facade 未来加新角色码时按原样显示，不吞掉。
const PENDING_ROLE_LABELS: Record<string, string> = {
  dispatcher: '调度员',
  planner: '计划员',
  supervisor: '班组长',
  operator: '操作员',
  material: '物料员',
  quality: '质检员',
  maintenance: '设备员',
}
// facade 的 routeHint 是相对路径提示；只接受站内 /mes 路径，其余回落到工序执行。
function resolvePendingRoute(routeHint?: string | null) {
  const hint = (routeHint ?? '').trim()
  return hint.startsWith('/mes') ? hint : '/mes/operation-tasks'
}
const pendingWorkItems = computed(() =>
  pendingWork.value.map((item, index) => ({
    key: `${item.roleCode ?? 'role'}-${item.workType ?? 'work'}-${index}`,
    role: PENDING_ROLE_LABELS[(item.roleCode ?? '').toLowerCase()] ?? item.roleCode ?? '未指定角色',
    workType: item.workType ?? '待办',
    count: item.count ?? 0,
    route: resolvePendingRoute(item.routeHint),
  })),
)

// 空态文案分四档：没读取 / 读取中 / 读取失败 / 确实读到了且为空。
// 只有最后一档才可以说「没有阻塞」。
const blockerEmptyMessage = computed(() => {
  if (overviewState.value === 'idle') return '尚未选择业务范围，未读取现场阻塞。'
  if (overviewState.value === 'loading') return '正在读取现场阻塞。'
  if (overviewState.value === 'error')
    return '现场阻塞获取失败，无法判断现场是否存在阻塞。请点右上角「刷新」重试。'
  return '本次读取的范围内没有阻塞记录。物料、质量、设备或产能出现卡点时会汇总到这里。'
})
const pendingEmptyMessage = computed(() => {
  if (overviewState.value === 'idle') return '尚未选择业务范围，未读取待办。'
  if (overviewState.value === 'loading') return '正在读取按角色汇总的待办。'
  if (overviewState.value === 'error')
    return '待办获取失败，无法判断各角色是否有待办。请点右上角「刷新」重试。'
  return '本次读取没有按角色汇总的待办。各角色可从上方工作台直接进入自己的队列。'
})

type BlockerRow = (typeof blockers)['value'][number]
const blockerColumns: NvDataTableColumn<BlockerRow>[] = [
  // 区域码词表与生产准备检查页共用；阻塞代码走 MES 就绪原因目录，取它的中文说法。
  {
    key: 'areaCode',
    header: '区域',
    width: 'w-28',
    accessor: (r) => labelFor(MES_READINESS_AREA_LABELS, r.areaCode) || '未知',
  },
  {
    key: 'code',
    header: '阻塞原因',
    cellClass: 'font-medium',
    accessor: (r) => (r.code ? describeMesReadinessReason(r.code).label : '未知'),
  },
  { key: 'message', header: '说明', accessor: (r) => r.message ?? '无说明' },
  { key: 'count', header: '数量', align: 'end', width: 'w-20' },
]

function normalizeCountKey(key?: string | null) {
  return (key ?? '').replace(/[-_\s]/g, '').toLowerCase()
}
function countValue(key: string) {
  const wanted = normalizeCountKey(key)
  return counts.value.find((item) => normalizeCountKey(item.key) === wanted)?.count ?? 0
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="生产驾驶舱" :breadcrumbs="[{ label: '制造执行' }]">
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="overviewPending"
          @click="refreshOverview"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div
      v-if="readNotice"
      :class="
        cn(
          'flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3',
          readNotice.class,
        )
      "
      role="alert"
    >
      <p class="text-sm">{{ readNotice.text }}</p>
      <NvButton
        v-if="readNotice.retry"
        size="sm"
        type="button"
        variant="outline"
        :disabled="overviewPending"
        @click="refreshOverview"
      >
        <RefreshCwIcon aria-hidden="true" />
        重试
      </NvButton>
    </div>

    <div class="grid gap-4 xl:grid-cols-3">
      <RouterLink
        v-for="card in commandCards"
        :key="card.title"
        :to="{ path: card.route }"
        :class="
          cn(
            'group grid gap-4 rounded-lg border p-4 transition-colors hover:border-primary/40',
            card.tone,
          )
        "
      >
        <div class="flex items-start justify-between gap-3">
          <div class="grid gap-1">
            <p class="text-sm font-semibold text-foreground">{{ card.title }}</p>
            <p class="text-sm leading-6 text-muted-foreground">{{ card.description }}</p>
          </div>
          <component :is="card.icon" class="size-5 shrink-0 text-primary" aria-hidden="true" />
        </div>
        <div class="flex items-end justify-between gap-3">
          <span class="text-3xl font-semibold tabular-nums text-foreground">{{ card.value }}</span>
          <span class="inline-flex items-center gap-1 text-sm font-medium text-primary">
            {{ card.action }}
            <ArrowRightIcon
              class="size-4 transition-transform group-hover:translate-x-0.5"
              aria-hidden="true"
            />
          </span>
        </div>
      </RouterLink>
    </div>

    <!-- 台账 #50：上面三张行动卡是全厂总量；这一块只讲「我这一摊」，
         数字由工序任务读面按登录人的授权作业范围过滤后由服务端计出，不是本页自算。 -->
    <NvCard>
      <NvCardContent class="grid gap-3">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div class="grid gap-0.5">
            <p class="text-sm font-semibold text-foreground">我的班组 · 现在该干什么</p>
            <p class="text-sm text-muted-foreground">{{ myScopeNote }}</p>
          </div>
          <RouterLink
            class="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline"
            :to="{ path: '/mes/operation-tasks' }"
          >
            打开我的工序队列
            <ArrowRightIcon class="size-4" aria-hidden="true" />
          </RouterLink>
        </div>
        <NvMetricStrip :cells="myScopeCells" />
      </NvCardContent>
    </NvCard>

    <NvMetricStrip :cells="overviewCells" />

    <!-- 两栏各自按内容定高：默认 stretch 会把矮的一栏拉到高的一栏那么高，
         留出整片空边框（阻塞表为空时尤其明显）。 -->
    <div class="grid items-start gap-4 xl:grid-cols-[1.15fr_0.85fr]">
      <div class="grid min-w-0 content-start gap-2">
        <div class="flex items-center justify-between">
          <span class="text-sm font-semibold text-foreground">现场阻塞</span>
          <RouterLink
            class="text-sm font-medium text-brand hover:underline"
            :to="{ path: '/mes/capacity' }"
            >异常与产能</RouterLink
          >
        </div>
        <NvDataTable
          :columns="blockerColumns"
          :rows="isReady ? blockers : []"
          :row-key="(r) => `${r.areaCode}-${r.code}`"
          :loading="overviewPending"
          :searchable="false"
          :column-settings="false"
          max-body-height="20rem"
          :empty-message="blockerEmptyMessage"
        >
          <template #cell-count="{ row }"
            ><span class="tabular-nums">{{ row.count ?? 0 }}</span></template
          >
        </NvDataTable>
      </div>

      <div class="grid gap-4">
        <NvCard>
          <NvCardContent class="p-0">
            <div class="border-b px-4 py-3">
              <h2 class="text-sm font-semibold text-foreground">角色工作台</h2>
              <p class="mt-1 text-xs text-muted-foreground">把同一批生产事实按一线角色重组入口。</p>
            </div>
            <div class="divide-y">
              <RouterLink
                v-for="lane in roleLanes"
                :key="lane.role"
                class="flex items-center justify-between gap-3 p-4 transition-colors hover:bg-muted/50"
                :to="{ path: lane.route }"
              >
                <div class="min-w-0">
                  <p class="text-sm font-semibold text-foreground">{{ lane.role }}</p>
                  <p class="mt-1 truncate text-sm text-muted-foreground">{{ lane.focus }}</p>
                </div>
                <div class="flex items-center gap-3">
                  <span class="text-lg font-semibold tabular-nums">{{ lane.count }}</span>
                  <ArrowRightIcon class="size-4 text-muted-foreground" aria-hidden="true" />
                </div>
              </RouterLink>
            </div>
          </NvCardContent>
        </NvCard>

        <NvCard>
          <NvCardContent class="p-0">
            <div class="flex items-center gap-2 border-b px-4 py-3">
              <PackageCheckIcon class="size-4 text-primary" aria-hidden="true" />
              <h2 class="text-sm font-semibold text-foreground">待办工作</h2>
            </div>
            <div v-if="isReady && pendingWorkItems.length" class="divide-y">
              <RouterLink
                v-for="item in pendingWorkItems"
                :key="item.key"
                class="flex items-center justify-between gap-3 p-4 transition-colors hover:bg-muted/50"
                :to="{ path: item.route }"
              >
                <div class="min-w-0">
                  <p class="text-sm font-semibold text-foreground">{{ item.workType }}</p>
                  <p class="mt-1 truncate text-sm text-muted-foreground">{{ item.role }}</p>
                </div>
                <div class="flex items-center gap-3">
                  <span class="text-lg font-semibold tabular-nums">{{ item.count }}</span>
                  <ArrowRightIcon class="size-4 text-muted-foreground" aria-hidden="true" />
                </div>
              </RouterLink>
            </div>
            <p v-else class="px-4 py-6 text-sm text-muted-foreground">{{ pendingEmptyMessage }}</p>
          </NvCardContent>
        </NvCard>
      </div>
    </div>
  </BusinessLayout>
</template>
