<script setup lang="ts">
import type { Component } from 'vue'
import { computed } from 'vue'
import {
  ActivityIcon,
  BellRingIcon,
  CheckCheckIcon,
  FactoryIcon,
  ListChecksIcon,
  MinusIcon,
  RefreshCwIcon,
  ShieldAlertIcon,
} from '@lucide/vue'
import {
  NvButton,
  NvCard,
  NvMetricCard,
  NvMetricRing,
  NvPageHeader,
  NvSectionCards,
  Skeleton,
} from '@nerv-iip/ui'
import type { NvMetricSegment, NvMetricTone } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import WorkbenchDomainTiles from '@/components/workbench/WorkbenchDomainTiles.vue'
import type { WorkbenchDomainTile } from '@/components/workbench/WorkbenchDomainTiles.vue'
import WorkbenchFocusCard from '@/components/workbench/WorkbenchFocusCard.vue'
import type { WorkbenchFocusItem } from '@/components/workbench/WorkbenchFocusCard.vue'
import { BUSINESS_DOMAINS, DOMAIN_SIDE_NAV, WORKBENCH_DOMAIN_ID, permittedBy } from '@/navigation'
import { BUSINESS_DOMAIN_PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { useBusinessWorkbenchSummary } from '@/composables/useBusinessWorkbench'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import type {
  BusinessConsoleWorkbenchAlertItem,
  BusinessConsoleWorkbenchMessageItem,
  BusinessConsoleWorkbenchTodoItem,
} from '@nerv-iip/api-client'

definePage({
  meta: {
    requiresAuth: true,
    title: '业务工作台',
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.workbench],
  },
})

/**
 * 业务工作台 = 跨域经营驾驶舱首屏，不是入口目录页。三段式：
 *
 * ① 英雄区（左 2×2 指标 + 右「今日待处理构成」环图）——只放 facade 真实返回的跨域读数，
 *    某个来源 status 不是 available 就整张不出现，宁缺毋假，不用 0 冒充"已接入且为零"。
 * ② 行动区——待办 / 消息 / 设备预警三张行动卡，带最近 4 条真实条目与出口。
 * ③ 业务域磁贴——收纳到域一级，页面级导航交给进入该域后的左侧导航。
 *
 * 三段的**加载态一律在卡内出骨架**，版式与成图一致：曾踩坑（真机回归）——加载期间英雄区
 * 左格的两个分支都不成立、零节点，环图卡被 grid 自动布局顶进 1fr 列，整行塌成一条空灰壳，
 * 页顶还裸露一行"正在刷新"和一个假的 0。任何一格都不许在某个状态下消失。
 *
 * 「来源状态」（已接入 / 未接入 / 无权限 / 暂不可用）是实施与运维视角，业务角色的第一屏
 * 不承载；需要排障视图时应另建运维页面，而不是塞回工作台。
 */

const auth = useAuthStore()
const {
  alertItems,
  availableKpis,
  contextReady,
  messageItems,
  refreshWorkbenchSummary,
  summary,
  summaryError,
  summaryPending,
  todoItems,
} = useBusinessWorkbenchSummary()

// 工作台读面只回设备编码，中文设备名在设备台账里，按编码 join 出来。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })

interface HeroMetric {
  key: string
  label: string
  value: number
  unit: string
  icon: Component
  tone: NvMetricTone
}

const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])

const todosAvailable = computed(() => isAvailable(summary.value?.todos?.status))
const messagesAvailable = computed(() => isAvailable(summary.value?.messages?.status))
const alertsAvailable = computed(() => isAvailable(summary.value?.alerts?.status))

const todoTotal = computed(() => (todosAvailable.value ? (summary.value?.todos?.total ?? 0) : 0))
const messageTotal = computed(() =>
  messagesAvailable.value ? (summary.value?.messages?.total ?? 0) : 0,
)
const messageUnread = computed(() =>
  messagesAvailable.value ? (summary.value?.messages?.unread ?? 0) : 0,
)
const alertTotal = computed(() => (alertsAvailable.value ? (summary.value?.alerts?.total ?? 0) : 0))
const alertCritical = computed(() =>
  alertsAvailable.value ? (summary.value?.alerts?.critical ?? 0) : 0,
)

/**
 * 三路来源各自的读面状态。**「取不到」「没接入」「真的是 0」是三件事**，页面必须分开表达：
 * 曾踩坑——`isAvailable` 把「未接入 / 无权限 / 暂不可用」一律折成 0 计入合计，页头与环图
 * 于是给出假读数，行动卡还照样写着「设备当前运行正常」。
 *
 * 另外注意：业务上下文未就绪时 `enabled: false`，pinia-colada 的 `asyncStatus` 停在
 * `idle`，`isLoading` 为 **false**——「压根没查」不会被 `summaryPending` 兜住，必须靠
 * `contextReady` 单独识别，否则会被当成「查过了、是 0」。
 */
// 只有「没就绪 **且** 手上确实没有摘要」才算未查询；已拿到结果就按实际数据走。
const summaryUnscoped = computed(() => !contextReady.value && summary.value === undefined)
const summaryFailed = computed(() => !summaryPending.value && summaryError.value != null)

const UNSCOPED_HINT = '尚未选择业务范围，工作台还没有发起查询——请先在顶部选择。'

function focusSourceState(available: boolean, name: string) {
  if (summaryFailed.value) {
    return { error: summaryError.value, unavailable: false, unavailableHint: undefined }
  }
  if (summaryUnscoped.value) {
    return { error: undefined, unavailable: true, unavailableHint: UNSCOPED_HINT }
  }
  return {
    error: undefined,
    unavailable: !available,
    unavailableHint: `${name}来源未接入或当前账号无权查看，工作台无法统计这一路，请联系管理员确认接入状态。`,
  }
}

const todoCardState = computed(() => focusSourceState(todosAvailable.value, '待办'))
const messageCardState = computed(() => focusSourceState(messagesAvailable.value, '消息'))
const alertCardState = computed(() => focusSourceState(alertsAvailable.value, '设备预警'))

/**
 * 首屏总量：只累加**当前可用**的来源；不可用的一路记 `null` 并被排除，绝不折成 0 进合计。
 * 一路可用来源都没有时整体为 `null`，页头与环图一律显 `—`。
 */
const pendingContributions = computed<(number | null)[]>(() =>
  summaryFailed.value || summaryUnscoped.value
    ? [null, null, null]
    : [
        todosAvailable.value ? todoTotal.value : null,
        messagesAvailable.value ? messageUnread.value : null,
        alertsAvailable.value ? alertTotal.value : null,
      ],
)

const pendingTotal = computed<number | null>(() => {
  const values = pendingContributions.value.filter((value): value is number => value !== null)
  return values.length > 0 ? values.reduce((sum, value) => sum + value, 0) : null
})

/** 有多少路"没算进来"——大于 0 时任何合计读数都只是部分事实，必须在文案里注明。 */
const missingSourceCount = computed(
  () => pendingContributions.value.filter((value) => value === null).length,
)

/** 页头读数：加载中不出数，取不到就直说取不到，部分缺失就注明是部分。 */
const pendingHeadline = computed(() => {
  if (summaryPending.value) return undefined
  if (summaryFailed.value) return '待处理数量取不到'
  if (pendingTotal.value === null) return '待处理数量暂不可用'
  return missingSourceCount.value > 0
    ? `${pendingTotal.value} 项待处理（另有 ${missingSourceCount.value} 路取不到）`
    : `${pendingTotal.value} 项待处理`
})

/** 环图标题同样要如实说明份额只覆盖可用来源，不让部分事实冒充全貌。 */
const workloadLabel = computed(() =>
  missingSourceCount.value > 0 ? '今日待处理构成（部分来源不可用）' : '今日待处理构成',
)

/**
 * 英雄区指标：facade KPI（当前口径为已下达工单 / 未关闭质量异常）+ 待办 + 设备预警。
 * 未读消息不占指标位——它的价值在行动卡的条目里，指标位留给能驱动决策的量。
 */
const heroMetrics = computed<HeroMetric[]>(() => {
  const metrics: HeroMetric[] = availableKpis.value.map((kpi) => {
    const value = kpi.value ?? 0
    const preset = KPI_PRESENTATION[normalize(kpi.key)]
    return {
      key: `kpi-${normalize(kpi.source)}-${normalize(kpi.key)}`,
      // 未登记的 KPI 只在后端 label 本身是中文时才透传；facade 现有若干英文 label
      // （如 "Open NCRs"），直接回吐会把英文印到首页工作台上。
      label: preset?.label ?? chineseOrFallback(kpi.label, '业务指标'),
      value,
      unit: preset?.unit ?? '',
      icon: preset?.icon ?? ListChecksIcon,
      tone: preset ? preset.tone(value) : 'neutral',
    }
  })

  if (todosAvailable.value) {
    metrics.push({
      key: 'todos',
      label: '待办事项',
      value: todoTotal.value,
      unit: '项',
      icon: ListChecksIcon,
      tone: todoTotal.value > 0 ? 'warning' : 'success',
    })
  }

  if (alertsAvailable.value) {
    metrics.push({
      key: 'alerts',
      label: '未解除设备预警',
      value: alertTotal.value,
      unit: '条',
      icon: ActivityIcon,
      tone: alertCritical.value > 0 ? 'danger' : alertTotal.value > 0 ? 'warning' : 'success',
    })
  }

  return metrics
})

/**
 * 今日待处理构成：三路各自的**权威总量**（不是抽样条目），所以份额是真的。
 * 只收当前可用的来源——某一路不可用时它连图例行都不出现，而不是记成 0；
 * 三路都为零时整块换成"已清空"读数，不画一个空环。
 */
const workloadSegments = computed<NvMetricSegment[]>(() =>
  [
    { key: 'todos', label: '待办', value: todoTotal.value, tone: 'warning' as const },
    { key: 'messages', label: '未读消息', value: messageUnread.value, tone: 'brand' as const },
    { key: 'alerts', label: '设备预警', value: alertTotal.value, tone: 'danger' as const },
  ].filter((segment) => availableWorkloadKeys.value.has(segment.key)),
)

const availableWorkloadKeys = computed(
  () =>
    new Set(
      [
        todosAvailable.value ? 'todos' : '',
        messagesAvailable.value ? 'messages' : '',
        alertsAvailable.value ? 'alerts' : '',
      ].filter(Boolean),
    ),
)

const todoFocusItems = computed<WorkbenchFocusItem[]>(() =>
  todoItems.value.map((item) => ({
    key: `${normalize(item.source)}-${normalize(item.itemId)}`,
    primary: readableCode(item.referenceId) || todoLabel(item),
    secondary: todoMeta(item),
  })),
)

const messageFocusItems = computed<WorkbenchFocusItem[]>(() =>
  messageItems.value.map((item) => ({
    key: normalize(item.messageId),
    primary: readableCode(item.resourceId) || messageLabel(item),
    secondary: messageMeta(item),
  })),
)

const alertFocusItems = computed<WorkbenchFocusItem[]>(() =>
  alertItems.value.map((item) => ({
    key: normalize(item.alarmEventId),
    primary: alertLabel(item),
    secondary: alertMeta(item),
  })),
)

/**
 * 业务域磁贴：落点取该域中当前角色**第一个有权限**的页面；一个可进入页面都没有的域
 * 直接不出现（前端可见性与网关授权保持一致，不留点进去 403 的入口）。
 */
const domainTiles = computed<WorkbenchDomainTile[]>(() =>
  BUSINESS_DOMAINS.filter((domain) => domain.id !== WORKBENCH_DOMAIN_ID)
    .map((domain) => {
      const items = (DOMAIN_SIDE_NAV[domain.id] ?? []).flatMap((group) =>
        permittedBy(group.items, permissionCodes.value),
      )
      const first = items[0]
      const to = first ? (typeof first.to === 'string' ? first.to : (first.to.path ?? '')) : ''

      return {
        id: domain.id,
        title: domain.title,
        icon: domain.icon,
        moduleCount: items.length,
        to,
      }
    })
    .filter((tile) => tile.moduleCount > 0 && tile.to.length > 0),
)

/** facade KPI key → 业务展示口径。未登记的 key 按 facade 标签原样展示，不硬编码兜底。 */
const KPI_PRESENTATION: Record<
  string,
  { label: string; unit: string; icon: Component; tone: (value: number) => NvMetricTone }
> = {
  openNcrs: {
    label: '未关闭质量异常',
    unit: '项',
    icon: ShieldAlertIcon,
    tone: (value) => (value > 0 ? 'danger' : 'success'),
  },
  releasedWorkOrders: {
    label: '已下达工单',
    unit: '张',
    icon: FactoryIcon,
    tone: () => 'brand',
  },
}

function isAvailable(status: string | null | undefined) {
  return normalize(status).toLowerCase() === 'available'
}

function normalize(value: string | null | undefined) {
  return value?.trim() ?? ''
}

/** 只在文本含中文时采用，否则退回占位——不把后端英文 label 直接印上屏。 */
function chineseOrFallback(value: string | null | undefined, fallback: string) {
  const text = normalize(value)
  return text && /[一-鿿]/.test(text) ? text : fallback
}

function sourceLabel(source: string | null | undefined) {
  const labels: Record<string, string> = {
    BusinessApproval: '审批',
    BusinessInventory: '库存管理',
    BusinessMES: '制造执行',
    BusinessQuality: '质量管理',
    IndustrialTelemetry: '设备预警',
    Notification: '消息通知',
  }

  const key = normalize(source)
  return labels[key] ?? '业务来源'
}

/**
 * 条目状态（待办 / 消息自己的生命周期）。这里刻意不复用「来源接入状态」那套词表——
 * 曾踩坑：两者共用一张表时 `unread` / `pending` 全部落进兜底，真机上每条消息都写着
 * "待确认"。认不出的状态返回空串，由调用处丢弃，绝不编一个词糊上去。
 */
function itemStatusLabel(status: string | null | undefined) {
  const labels: Record<string, string> = {
    acknowledged: '已确认',
    approved: '已通过',
    completed: '已完成',
    open: '待处理',
    pending: '待处理',
    read: '已读',
    rejected: '已驳回',
    unread: '未读',
  }

  return labels[normalize(status).toLowerCase()] ?? ''
}

/**
 * 只有**人读**编码才配上条目主行。facade 的 referenceId / resourceId 可能是内部
 * GUID（真机实测：审批链消息的 resourceId 就是 GUID），那种值一律不进 UI。
 */
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function readableCode(value: string | null | undefined) {
  const code = normalize(value)
  return code && !GUID_PATTERN.test(code) ? code : ''
}

function resourceLabel(resourceType: string | null | undefined) {
  const labels: Record<string, string> = {
    'approval-chain': '审批流转',
    'inventory-count': '盘点任务',
    'purchase-order': '采购单据',
    'quality-ncr': '质量异常',
    'work-order': '生产工单',
  }

  return labels[normalize(resourceType).toLowerCase()] ?? ''
}

function todoLabel(item: BusinessConsoleWorkbenchTodoItem) {
  const source = sourceLabel(item.source)
  const typeLabels: Record<string, string> = {
    'inventory-count': '盘点任务',
    'purchase-order': '采购单据',
    quality: '质量处置',
  }
  const type = typeLabels[normalize(item.itemType)] ?? '待办事项'
  return `${source} · ${type}`
}

function todoMeta(item: BusinessConsoleWorkbenchTodoItem) {
  const parts = [itemStatusLabel(item.status)]
  if (item.dueAtUtc) {
    parts.push(`到期 ${formatDateTime(item.dueAtUtc)}`)
  }
  return parts.filter(Boolean).join(' · ')
}

function messageLabel(item: BusinessConsoleWorkbenchMessageItem) {
  return resourceLabel(item.resourceType) || `${severityLabel(item.severity)}消息`
}

function messageMeta(item: BusinessConsoleWorkbenchMessageItem) {
  const parts = [severityLabel(item.severity), itemStatusLabel(item.status)]
  if (item.createdAtUtc) {
    parts.push(formatDateTime(item.createdAtUtc))
  }
  return parts.filter(Boolean).join(' · ')
}

function alertLabel(item: BusinessConsoleWorkbenchAlertItem) {
  const deviceCode = normalize(item.deviceAssetId)
  const deviceName = resolveDevice(deviceCode)
  // 设备名在设备台账里，按编码 join 出中文名；名录查不到就只显编码，不编造名字。
  const device = deviceCode ? (deviceName ? `${deviceName} ${deviceCode}` : deviceCode) : '设备'
  // 报警码的中文描述要由遥测读面下发（前端没有报警码名录），这里如实显示原码。
  const code = normalize(item.alarmCode) || '报警'
  return `${device} · ${code}`
}

function alertMeta(item: BusinessConsoleWorkbenchAlertItem) {
  const parts = [severityLabel(item.severity)]
  if (item.raisedAtUtc) {
    parts.push(formatDateTime(item.raisedAtUtc))
  }
  return parts.filter(Boolean).join(' · ')
}

function severityLabel(severity: string | null | undefined) {
  const labels: Record<string, string> = {
    critical: '紧急',
    error: '严重',
    info: '提示',
    warning: '预警',
  }

  return labels[normalize(severity).toLowerCase()] ?? '业务'
}

function formatDateTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return new Intl.DateTimeFormat('zh-CN', {
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    month: '2-digit',
  }).format(date)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="业务工作台"
      :breadcrumbs="[{ label: '数字化工作台' }]"
      :count="pendingHeadline"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="summaryPending"
          @click="refreshWorkbenchSummary"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvCard
      v-if="summaryError && !summaryPending"
      class="flex items-center justify-between gap-4 border-destructive/30 bg-destructive/[0.04] px-5 py-4"
      role="alert"
    >
      <div>
        <p class="text-sm font-medium text-destructive-strong">工作台摘要读取失败</p>
        <p class="mt-1 text-sm text-muted-foreground">
          跨域汇总接口没有返回结果，现在无法判断今天是否有待处理事项，下面各卡的读数一律显
          「—」。请重试，或稍后再看。
        </p>
      </div>
      <NvButton size="sm" type="button" variant="outline" @click="refreshWorkbenchSummary">
        <RefreshCwIcon aria-hidden="true" />
        重试
      </NvButton>
    </NvCard>

    <!--
      ① 英雄区：左侧跨域指标 2×2，右侧今日待处理构成。
      左格**恒有节点**（骨架 / 指标 / 空态三选一）——曾踩坑：加载态两个分支都不成立时
      左格零节点，环图卡被 grid 自动布局顶进 1fr 宽列，第 2 列空着，整行塌成一条空灰壳。
    -->
    <section
      class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(340px,24rem)]"
      aria-label="跨域指标"
    >
      <!-- auto-rows-fr：指标块与右侧构成卡等高，英雄区不出现半截留白 -->
      <NvSectionCards class="h-full auto-rows-fr" :columns="2">
        <template v-if="summaryPending">
          <NvCard
            v-for="slot in 4"
            :key="`hero-skeleton-${slot}`"
            class="flex items-center gap-3.5 p-5"
            aria-hidden="true"
          >
            <Skeleton class="size-11 flex-none rounded-[10px]" />
            <div class="flex min-w-0 flex-1 flex-col gap-2">
              <Skeleton class="h-3.5 w-24 rounded" />
              <Skeleton class="h-6 w-16 rounded" />
            </div>
          </NvCard>
        </template>

        <template v-else-if="heroMetrics.length > 0">
          <NvMetricCard
            v-for="metric in heroMetrics"
            :key="metric.key"
            class="flex flex-col justify-center"
            variant="icon"
            :label="metric.label"
            :value="metric.value"
            :unit="metric.unit"
            :icon="metric.icon"
            :tone="metric.tone"
          />
        </template>

        <!-- 取不到数时不能说"暂无指标"——那等于断言"查过了、确实没有" -->
        <NvCard
          v-else-if="summaryFailed || summaryUnscoped"
          class="col-span-full grid place-items-center p-6 text-center"
        >
          <div>
            <p class="text-sm font-medium text-foreground">
              {{ summaryFailed ? '跨域指标读取失败' : '尚未发起查询' }}
            </p>
            <p class="mt-1 text-sm text-muted-foreground">
              {{
                summaryFailed
                  ? '没有取到跨域汇总数据，当前无法判断各项指标的实际数值。'
                  : UNSCOPED_HINT
              }}
            </p>
          </div>
        </NvCard>

        <NvCard v-else class="col-span-full grid place-items-center p-6 text-center">
          <div>
            <p class="text-sm font-medium text-foreground">当前角色没有可汇总的跨域指标</p>
            <p class="mt-1 text-sm text-muted-foreground">
              指标来源均未接入或不在你的权限范围内，接入后会自动出现在这里。
            </p>
          </div>
        </NvCard>
      </NvSectionCards>

      <!--
        构成卡直接用库件 NvMetricRing（组件库里"部分之和"的标准承载，equipment 看板同款），
        不再自绘卡壳 + 图表：库件自带环 / 图例 / 份额和居中读数。加载与全清空两态没有对应
        库件形态，才落到自绘容器，且同样只用平面 bg-card。
      -->
      <NvCard v-if="summaryPending" class="flex flex-col justify-center p-5" aria-hidden="true">
        <Skeleton class="h-3.5 w-24 rounded" />
        <div class="mt-3 flex items-center gap-[18px]">
          <Skeleton class="size-[84px] flex-none rounded-full" />
          <div class="flex min-w-0 flex-1 flex-col gap-2.5">
            <Skeleton v-for="row in 3" :key="`ring-skeleton-${row}`" class="h-3.5 w-full rounded" />
          </div>
        </div>
      </NvCard>

      <!--
        取不到数（失败 / 未查询 / 三路全不可用）时既不画环也不显 0——环图中心的读数一旦写成
        0，就等于向使用者断言"今天没事"，而事实是"根本不知道"。
      -->
      <NvCard v-else-if="pendingTotal === null" class="flex flex-col p-5" role="alert">
        <p class="truncate text-sm text-muted-foreground">今日待处理构成</p>
        <div class="grid flex-1 place-items-center py-2 text-center">
          <div class="grid justify-items-center gap-3">
            <span class="grid size-12 place-items-center rounded-full bg-muted">
              <MinusIcon class="size-6 text-muted-foreground" aria-hidden="true" />
            </span>
            <div>
              <p class="text-sm font-medium text-foreground">
                {{ summaryFailed ? '取不到数据，无法判断' : '尚未发起查询' }}
              </p>
              <p class="mt-1 text-sm leading-6 text-muted-foreground">
                {{
                  summaryFailed
                    ? '跨域汇总没有返回结果，现在不能确认今天是否有待处理事项。'
                    : summaryUnscoped
                      ? UNSCOPED_HINT
                      : '待办、消息与设备预警三路来源当前都无法统计，请联系管理员确认接入状态。'
                }}
              </p>
            </div>
            <NvButton
              v-if="summaryFailed"
              size="sm"
              type="button"
              variant="outline"
              @click="refreshWorkbenchSummary"
            >
              <RefreshCwIcon aria-hidden="true" />
              重试
            </NvButton>
          </div>
        </div>
      </NvCard>

      <NvMetricRing
        v-else-if="(pendingTotal ?? 0) > 0"
        class="flex flex-col justify-center"
        :label="workloadLabel"
        :value="pendingTotal ?? 0"
        center-caption="项待处理"
        :segments="workloadSegments"
      />

      <!-- 还有来源没算进来时，"全部清空"这句话就不成立，只能说已接入的这几路是 0。 -->
      <NvCard v-else-if="missingSourceCount > 0" class="flex flex-col p-5">
        <p class="truncate text-sm text-muted-foreground">{{ workloadLabel }}</p>
        <div class="grid flex-1 place-items-center py-2 text-center">
          <div class="grid justify-items-center gap-3">
            <span class="grid size-12 place-items-center rounded-full bg-muted">
              <MinusIcon class="size-6 text-muted-foreground" aria-hidden="true" />
            </span>
            <div>
              <p class="text-sm font-medium text-foreground">已接入的来源都是 0</p>
              <p class="mt-1 text-sm leading-6 text-muted-foreground">
                另有 {{ missingSourceCount }} 路来源取不到数据，还不能断定今天全部清空。
              </p>
            </div>
          </div>
        </div>
      </NvCard>

      <!-- 空态沿用 NvMetricRing 的标签排布（同样的 label 行），各态之间标题位不跳。 -->
      <NvCard v-else class="flex flex-col p-5">
        <p class="truncate text-sm text-muted-foreground">今日待处理构成</p>
        <div class="grid flex-1 place-items-center py-2 text-center">
          <div class="grid justify-items-center gap-3">
            <span class="grid size-12 place-items-center rounded-full bg-success/10">
              <CheckCheckIcon class="size-6 text-success-strong" aria-hidden="true" />
            </span>
            <div>
              <p class="text-sm font-medium text-foreground">今天没有待处理事项</p>
              <p class="mt-1 text-sm leading-6 text-muted-foreground">
                待办、消息与设备预警都已清空，新的事项到达后会自动汇总到这里。
              </p>
            </div>
          </div>
        </div>
      </NvCard>
    </section>

    <!--
      ② 行动区：三条处理路径，各带最近条目与出口。
      flex-1 让这一段吸收 1080 首屏的剩余高度——条目列表在上、空态居中、出口贴底，
      长高有内容托底，首屏吃满而不是靠拉伸磁贴凑版面。
    -->
    <section class="grid flex-1 gap-4 lg:grid-cols-3">
      <WorkbenchFocusCard
        title="待办"
        description="先清掉到期的审批与任务，别让单据停在你这一环。"
        :icon="ListChecksIcon"
        tone="warning"
        :count="todoTotal"
        unit="项"
        :items="todoFocusItems"
        :pending="summaryPending"
        :error="todoCardState.error"
        :unavailable="todoCardState.unavailable"
        :unavailable-hint="todoCardState.unavailableHint"
        empty-title="待办已清空"
        empty-hint="新的审批与任务到达后会自动出现在这里。"
        to="/approval"
        action-label="去审批中心"
        @retry="refreshWorkbenchSummary"
      />

      <WorkbenchFocusCard
        title="消息"
        description="与你相关的通知集中在这里，不用逐个业务页面翻。"
        :icon="BellRingIcon"
        tone="brand"
        :count="messageUnread"
        unit="条"
        :badge="messageTotal > 0 ? `共 ${messageTotal} 条` : undefined"
        :items="messageFocusItems"
        :pending="summaryPending"
        :error="messageCardState.error"
        :unavailable="messageCardState.unavailable"
        :unavailable-hint="messageCardState.unavailableHint"
        empty-title="没有未读消息"
        empty-hint="有新的业务通知时会第一时间出现在这里。"
        @retry="refreshWorkbenchSummary"
      />

      <WorkbenchFocusCard
        title="设备预警"
        description="先看紧急报警，确认现场是否已经影响生产节拍。"
        :icon="ShieldAlertIcon"
        :tone="alertCritical > 0 ? 'danger' : 'neutral'"
        :count="alertTotal"
        unit="条"
        :badge="alertCritical > 0 ? `紧急 ${alertCritical}` : undefined"
        :items="alertFocusItems"
        :pending="summaryPending"
        :error="alertCardState.error"
        :unavailable="alertCardState.unavailable"
        :unavailable-hint="alertCardState.unavailableHint"
        empty-title="没有未解除的设备预警"
        empty-hint="一旦有设备报出异常就会立刻出现在这里。"
        to="/equipment/alarms"
        action-label="查看设备报警"
        @retry="refreshWorkbenchSummary"
      />
    </section>

    <!-- ③ 业务域磁贴：收纳到域一级，页面级导航交给域内左侧导航 -->
    <WorkbenchDomainTiles :tiles="domainTiles" />
  </BusinessLayout>
</template>
