<script setup lang="ts">
import type { Component } from 'vue'
import { computed } from 'vue'
import {
  ActivityIcon,
  BellRingIcon,
  CheckCheckIcon,
  FactoryIcon,
  ListChecksIcon,
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
  messageItems,
  refreshWorkbenchSummary,
  summary,
  summaryError,
  summaryPending,
  todoItems,
} = useBusinessWorkbenchSummary()

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

/** 首屏总量：三路待处理之和，供页头一眼给出"今天有多少事"。 */
const pendingTotal = computed(() => todoTotal.value + messageUnread.value + alertTotal.value)

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
  const device = normalize(item.deviceAssetId) || '设备'
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
      :count="summaryPending ? undefined : `${pendingTotal} 项待处理`"
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
        <p class="text-sm font-medium text-destructive-strong">工作台摘要暂不可用</p>
        <p class="mt-1 text-sm text-muted-foreground">
          跨域汇总接口没有返回结果，下面展示的是最近一次成功获取的内容。
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

        <NvCard v-else class="col-span-full grid place-items-center p-6 text-center">
          <div>
            <p class="text-sm font-medium text-foreground">暂无可显示指标</p>
            <p class="mt-1 text-sm text-muted-foreground">
              当前角色没有可汇总的跨域指标，或来源暂不可用。
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

      <NvMetricRing
        v-else-if="pendingTotal > 0"
        class="flex flex-col justify-center"
        label="今日待处理构成"
        :value="pendingTotal"
        center-caption="项待处理"
        :segments="workloadSegments"
      />

      <!-- 空态沿用 NvMetricRing 的标签排布（同样的 label 行），三态之间标题位不跳。 -->
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
        empty-title="待办已清空"
        empty-hint="新的审批与任务到达后会自动出现在这里。"
        to="/approval"
        action-label="去审批中心"
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
        empty-title="暂无未读消息"
        empty-hint="有新的业务通知时会第一时间出现在这里。"
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
        empty-title="设备当前运行正常"
        empty-hint="一旦有设备报出异常就会立刻出现在这里。"
        to="/equipment/alarms"
        action-label="查看设备报警"
      />
    </section>

    <!-- ③ 业务域磁贴：收纳到域一级，页面级导航交给域内左侧导航 -->
    <WorkbenchDomainTiles :tiles="domainTiles" />
  </BusinessLayout>
</template>
