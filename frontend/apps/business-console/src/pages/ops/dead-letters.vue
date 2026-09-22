<script setup lang="ts">
import type {
  IntegrationEventDeadLetterResponse,
  IntegrationEventDeadLetterStatus,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, StatusTone } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import {
  deadLetterRowKey,
  useBusinessDeadLetters,
  type DeadLetterReplayOutcome,
  type DeadLetterUnavailableSource,
} from '@/composables/useBusinessDeadLetters'
import {
  inlineErrorMessage,
  notifyOperationFailure,
  notifySuccess,
  notifyWarning,
} from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvField,
  NvFieldLabel,
  NvInput,
  NvSectionCard,
  NvSectionCards,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { BanIcon, RotateCwIcon, ShieldAlertIcon } from '@lucide/vue'
import { computed, ref } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '集成事件死信',
    requiredPermissions: ['business.dlq.read'],
  },
})

const {
  availableServices,
  contextReady,
  filteredServiceUnavailable,
  filters,
  hasUnavailableSource,
  ignore,
  ignorePending,
  items,
  listError,
  listPending,
  metrics,
  metricsError,
  refresh,
  replayOne,
  replayOutcomes,
  replayPending,
  replaySelected,
  selectedDeadLetter,
  detailError,
  detailPending,
  selectedRowKey,
  selectedRowKeys,
  selectedTarget,
  serviceMetrics,
  unavailableSources,
} = useBusinessDeadLetters()

const detailOpen = ref(false)
const ignoreReason = ref('')

const auth = useAuthStore()
/**
 * 重放与忽略端点要求 `business.dlq.manage`（网关侧强制，见 BusinessGatewayAuthorization）。
 * 只读角色不该看到一个按下去必然 403 的按钮——前端可见性与网关授权一起改、保持一致。
 */
const canManage = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.deadLettersManage),
)

const listErrorMessage = computed(() => inlineErrorMessage(listError.value))
const detailErrorMessage = computed(() => inlineErrorMessage(detailError.value))
/**
 * 概览取数失败时必须明确降级：四张卡一律 `?? 0` 会把「没读到」画成「正常且为零」，
 * 而这正是母票 #3727 要消除的静默。`unavailableSources` 只来自**成功**信封里的逐源状态，
 * 覆盖不到「概览请求整体失败」这一档。
 */
const metricsErrorMessage = computed(() => inlineErrorMessage(metricsError.value))
const actionPending = computed(() => replayPending.value || ignorePending.value)

/**
 * 表格行：把 `service` / `id` 从「契约上可空」收敛成「这里一定有」，在**进入表格前**一次性完成。
 * 否则下游每个用到它们的地方都得各写一次判空或 `!`，而那些判空全都走不到。
 */
interface DeadLetterRow {
  service: string
  deadLetterId: string
  deadLetter: IntegrationEventDeadLetterResponse
}

const rows = computed<DeadLetterRow[]>(() =>
  items.value.flatMap((item) => {
    const service = item.service
    const deadLetter = item.deadLetter
    return service && deadLetter?.id ? [{ service, deadLetterId: deadLetter.id, deadLetter }] : []
  }),
)
const selectedRows = computed(() =>
  rows.value.filter((row) => selectedRowKeys.value.includes(rowKeyOf(row))),
)

/**
 * 空态文案要能区分两件事：本次**没读到**这个服务，和这个服务**确实没有**死信。
 * 只写「暂无死信」会把前者说成后者——那正是母票 #3727 要消除的那类静默。
 */
const emptyMessage = computed(() => {
  if (filteredServiceUnavailable.value) {
    return `本次未读到 ${filters.service} 的死信（该服务未响应），这不代表它没有死信；请稍后重试。`
  }
  if (hasUnavailableSource.value) {
    return '当前条件下已读到的服务均无死信；另有服务本次未读到，见上方提示。'
  }
  return '当前条件下没有死信。'
})

const unavailableHint = computed(() =>
  unavailableSources.value.map((source) => `${source.service}（${reasonText(source)}）`).join('、'),
)

const columns: NvDataTableColumn<DeadLetterRow>[] = [
  { key: 'service', header: '服务', filter: 'enum', width: 'w-36' },
  { key: 'eventType', header: '事件类型', accessor: (row) => row.deadLetter.eventType ?? '' },
  { key: 'consumerName', header: '消费者', accessor: (row) => row.deadLetter.consumerName ?? '' },
  { key: 'failureCode', header: '失败码', accessor: (row) => row.deadLetter.failureCode ?? '' },
  {
    key: 'deadLetteredAtUtc',
    header: '死信时间',
    sortable: true,
    accessor: (row) => row.deadLetter.deadLetteredAtUtc ?? '',
    width: 'w-44',
  },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'replayOutcome', header: '重放结果', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-40', hideable: false },
]

function rowKeyOf(row: DeadLetterRow) {
  return deadLetterRowKey(row.service, row.deadLetterId)
}

function reasonText(source: DeadLetterUnavailableSource) {
  return source.reason === 'sourceTimeout' ? '响应超时，可能只是慢' : '服务不可用'
}

const STATUS_LABELS: Record<IntegrationEventDeadLetterStatus, { label: string; tone: StatusTone }> =
  {
    pending: { label: '待处理', tone: 'warning' },
    failed: { label: '重放失败', tone: 'danger' },
    replayed: { label: '已重放', tone: 'success' },
    ignored: { label: '已忽略', tone: 'neutral' },
  }

const REPLAY_LABELS: Record<
  DeadLetterReplayOutcome['status'],
  { label: string; tone: StatusTone }
> = {
  replayed: { label: '已重放', tone: 'success' },
  failed: { label: '重放失败', tone: 'danger' },
  // 「试过并失败」与「这个服务压根没有重放能力」对操作者是两回事：后者再点多少次都不会变。
  noHandler: { label: '该服务无重放能力', tone: 'warning' },
  notFound: { label: '死信已不存在', tone: 'neutral' },
}

/**
 * 这行的重放结果，包装成 0 或 1 个元素的数组——模板里用 `v-for` 渲染，
 * 「有没有」和「是什么」因此读自同一次取值，不需要在模板里断言非空。
 */
function replayDisplay(row: DeadLetterRow) {
  const outcome = replayOutcomes.get(rowKeyOf(row))
  return outcome ? [{ value: outcome.status, ...REPLAY_LABELS[outcome.status] }] : []
}

function canReplay(row: DeadLetterRow) {
  const status = row.deadLetter.status
  return canManage.value && status !== 'replayed' && status !== 'ignored'
}

function openDetail(row: DeadLetterRow) {
  selectedRowKey.value = rowKeyOf(row)
  ignoreReason.value = ''
  detailOpen.value = true
}

function announce(outcome: DeadLetterReplayOutcome, subject: string) {
  if (outcome.status === 'replayed') {
    notifySuccess(`${subject}已重放`)
    return
  }
  notifyWarning(`${subject}未重放：${REPLAY_LABELS[outcome.status].label}`)
}

async function handleRefresh() {
  try {
    await refresh()
  } catch (error) {
    notifyOperationFailure('刷新失败', error, '无法刷新死信列表，请稍后重试。')
  }
}

async function handleReplayRow(row: DeadLetterRow) {
  try {
    announce(await replayOne(row.service, row.deadLetterId), '该死信')
  } catch (error) {
    notifyOperationFailure('重放失败', error, '无法重放该死信，请稍后重试。')
  }
}

async function handleReplaySelected() {
  // 整批走完才返回，列表与计数已在 composable 里统一失效——这里不会留下「部分已重放但屏上没变」。
  const { outcomes, firstError, unansweredCount } = await replaySelected(selectedRows.value)
  selectedRowKeys.value = []

  if (firstError) {
    // 「没收到答复」不等于「重放失败」：结果未知，要引导去核实，而不是说它失败了。
    notifyOperationFailure(
      '重放未确认',
      firstError,
      `${unansweredCount} 条未收到服务端答复，结果未知；请刷新列表核实是否已重放，勿直接重试。`,
    )
    return
  }

  const replayed = outcomes.filter(({ outcome }) => outcome.status === 'replayed').length
  if (replayed === outcomes.length) {
    notifySuccess(`已重放 ${replayed} 条死信`)
  } else {
    notifyWarning(`${outcomes.length} 条中重放成功 ${replayed} 条，其余见「重放结果」列`)
  }
}

async function handleIgnore() {
  const target = selectedTarget.value
  if (!target) return

  try {
    await ignore(target.service, target.deadLetterId, ignoreReason.value)
    notifySuccess('该死信已忽略')
    ignoreReason.value = ''
    detailOpen.value = false
  } catch (error) {
    notifyOperationFailure('忽略失败', error, '无法忽略该死信，请稍后重试。')
  }
}

function formatTime(value: string | null | undefined) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('zh-CN', { dateStyle: 'short', timeStyle: 'medium' }).format(
    new Date(value),
  )
}

function formatPayload(value: string | null | undefined) {
  if (!value) return '—'
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    // 不是合法 JSON 就原样展示：这本身是排查死信时要看到的事实。
    return value
  }
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-6">
      <NvPageHeader title="集成事件死信" :breadcrumbs="[{ label: '集成运维' }]">
        <template #actions>
          <NvButton
            size="sm"
            type="button"
            variant="outline"
            :disabled="listPending"
            @click="handleRefresh"
          >
            刷新
          </NvButton>
        </template>
      </NvPageHeader>

      <p
        v-if="hasUnavailableSource"
        class="flex items-start gap-2 rounded-md border border-warning/40 bg-warning/10 px-4 py-3 text-sm"
        role="status"
      >
        <ShieldAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>
          以下服务本次未读到，下方列表与计数均不含它们：{{ unavailableHint }}。
          计数因此偏小，请勿据此判断这些服务没有死信。
        </span>
      </p>

      <!-- 概览取数失败时不画 0：那会被读成「正常且为零」。 -->
      <p
        v-if="metricsErrorMessage"
        class="flex items-start gap-2 rounded-md border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm"
        role="alert"
      >
        <ShieldAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>
          未能读取死信概览，下方计数、按服务分组与<strong>服务清单</strong>均暂不可用（服务下拉因此为空，
          这不代表平台只接入了一个来源）：{{ metricsErrorMessage }}
        </span>
      </p>
      <!-- 四档互斥且相加等于总数；不再单列「积压」，它只是「待处理 + 重放失败」的和。 -->
      <NvSectionCards v-else :columns="4">
        <NvSectionCard description="待处理" :value="metrics?.pendingCount ?? 0" />
        <NvSectionCard description="重放失败" :value="metrics?.failedCount ?? 0" />
        <NvSectionCard description="已重放" :value="metrics?.replayedCount ?? 0" />
        <NvSectionCard description="已忽略" :value="metrics?.ignoredCount ?? 0" />
      </NvSectionCards>

      <section v-if="serviceMetrics.length > 0" class="grid gap-3 rounded-lg border bg-card p-4">
        <h2 class="text-sm font-semibold">按服务分组</h2>
        <ul class="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
          <li
            v-for="entry in serviceMetrics"
            :key="entry.service"
            class="flex items-center justify-between gap-3 rounded-md border px-3 py-2 text-sm"
          >
            <span class="truncate font-medium">{{ entry.service }}</span>
            <span class="shrink-0 tabular-nums text-muted-foreground">
              待处理 {{ entry.metrics?.pendingCount ?? 0 }} · 失败
              {{ entry.metrics?.failedCount ?? 0 }}
            </span>
          </li>
        </ul>
      </section>

      <section class="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2 xl:grid-cols-3">
        <NvField>
          <NvFieldLabel>服务</NvFieldLabel>
          <!--
            服务清单与概览同源。概览读不到时清单为空，此时必须停用并说明原因：
            一个只剩「全部服务」的下拉会被读成「平台只接入了一个来源」。
          -->
          <NvSelect v-model="filters.service" :disabled="Boolean(metricsErrorMessage)">
            <!-- placeholder 永远走不到：service 始终有取值（默认哨兵 all），故保持静态。 -->
            <NvSelectTrigger><NvSelectValue placeholder="全部服务" /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem value="all">全部服务</NvSelectItem>
              <NvSelectItem v-for="service in availableServices" :key="service" :value="service">
                {{ service }}
              </NvSelectItem>
            </NvSelectContent>
          </NvSelect>
        </NvField>
        <NvField>
          <NvFieldLabel>事件类型</NvFieldLabel>
          <NvInput v-model="filters.eventType" placeholder="按事件类型筛选" />
        </NvField>
        <NvField>
          <NvFieldLabel>状态</NvFieldLabel>
          <NvSelect v-model="filters.status">
            <NvSelectTrigger><NvSelectValue placeholder="全部状态" /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem value="all">全部状态</NvSelectItem>
              <NvSelectItem value="pending">待处理</NvSelectItem>
              <NvSelectItem value="failed">重放失败</NvSelectItem>
              <NvSelectItem value="replayed">已重放</NvSelectItem>
              <NvSelectItem value="ignored">已忽略</NvSelectItem>
            </NvSelectContent>
          </NvSelect>
        </NvField>
      </section>

      <NvDataTable
        v-model:selected="selectedRowKeys"
        :columns="columns"
        :rows="rows"
        :row-key="rowKeyOf"
        :loading="listPending"
        :error="listError"
        :error-message="listErrorMessage"
        :awaiting-scope="!contextReady"
        awaiting-scope-message="尚未选择业务范围，还没有发起查询。"
        :empty-message="emptyMessage"
        selectable
        search-placeholder="搜索事件类型、消费者、失败码…"
        @retry="handleRefresh"
      >
        <template #bulk-actions>
          <NvButton
            size="sm"
            type="button"
            :disabled="!canManage || actionPending || selectedRows.length === 0"
            @click="handleReplaySelected"
          >
            <RotateCwIcon class="size-4" aria-hidden="true" />
            重放选中（{{ selectedRows.length }}）
          </NvButton>
        </template>

        <template #cell-deadLetteredAtUtc="{ row }">
          {{ formatTime(row.deadLetter.deadLetteredAtUtc) }}
        </template>

        <template #cell-status="{ row }">
          <NvStatusBadge
            v-if="row.deadLetter.status"
            :value="row.deadLetter.status"
            :label="STATUS_LABELS[row.deadLetter.status].label"
            :tone="STATUS_LABELS[row.deadLetter.status].tone"
          />
        </template>

        <template #cell-replayOutcome="{ row }">
          <NvStatusBadge
            v-for="display in replayDisplay(row)"
            :key="display.value"
            :value="display.value"
            :label="display.label"
            :tone="display.tone"
          />
          <span v-if="replayDisplay(row).length === 0" class="text-muted-foreground">—</span>
        </template>

        <template #cell-actions="{ row }">
          <div class="flex justify-end gap-2">
            <NvButton size="sm" type="button" variant="outline" @click="openDetail(row)">
              详情
            </NvButton>
            <NvButton
              size="sm"
              type="button"
              variant="outline"
              :disabled="actionPending || !canReplay(row)"
              :aria-label="`重放死信：${row.deadLetter.eventType ?? row.deadLetterId}`"
              @click="handleReplayRow(row)"
            >
              <RotateCwIcon class="size-4" aria-hidden="true" />
            </NvButton>
          </div>
        </template>
      </NvDataTable>
    </section>

    <NvSheet v-model:open="detailOpen">
      <NvSheetContent side="right" size="lg" class="overflow-y-auto">
        <NvSheetHeader>
          <NvSheetTitle>死信详情</NvSheetTitle>
          <NvSheetDescription>
            {{ selectedTarget?.service }} · {{ selectedDeadLetter?.eventType ?? '未知事件类型' }}
          </NvSheetDescription>
        </NvSheetHeader>

        <p v-if="detailErrorMessage" class="px-4 text-sm text-destructive" role="alert">
          {{ detailErrorMessage }}
        </p>
        <p v-else-if="detailPending" class="px-4 text-sm text-muted-foreground">正在加载详情…</p>
        <div v-else-if="selectedDeadLetter" class="grid gap-4 px-4 pb-6 text-sm">
          <dl class="grid grid-cols-[6.5rem_minmax(0,1fr)] gap-x-3 gap-y-2">
            <dt class="text-muted-foreground">状态</dt>
            <dd>
              <NvStatusBadge
                v-if="selectedDeadLetter.status"
                :value="selectedDeadLetter.status"
                :label="STATUS_LABELS[selectedDeadLetter.status].label"
                :tone="STATUS_LABELS[selectedDeadLetter.status].tone"
              />
            </dd>
            <dt class="text-muted-foreground">事件类型</dt>
            <dd class="break-all">{{ selectedDeadLetter.eventType ?? '—' }}</dd>
            <dt class="text-muted-foreground">消费者</dt>
            <dd class="break-all">{{ selectedDeadLetter.consumerName ?? '—' }}</dd>
            <dt class="text-muted-foreground">事件来源</dt>
            <dd class="break-all">{{ selectedDeadLetter.sourceService ?? '—' }}</dd>
            <dt class="text-muted-foreground">事件 ID</dt>
            <dd class="break-all">{{ selectedDeadLetter.eventId ?? '—' }}</dd>
            <dt class="text-muted-foreground">幂等键</dt>
            <dd class="break-all">{{ selectedDeadLetter.idempotencyKey ?? '—' }}</dd>
            <dt class="text-muted-foreground">失败码</dt>
            <dd class="break-all">{{ selectedDeadLetter.failureCode ?? '—' }}</dd>
            <dt class="text-muted-foreground">失败原因</dt>
            <dd class="break-words">{{ selectedDeadLetter.failureMessage ?? '—' }}</dd>
            <dt class="text-muted-foreground">死信时间</dt>
            <dd>{{ formatTime(selectedDeadLetter.deadLetteredAtUtc) }}</dd>
            <dt class="text-muted-foreground">重放时间</dt>
            <dd>{{ formatTime(selectedDeadLetter.replayedAtUtc) }}</dd>
          </dl>

          <div class="grid gap-1">
            <span class="text-muted-foreground">事件内容</span>
            <pre class="max-h-72 overflow-auto rounded-md bg-muted p-3 text-xs leading-relaxed">{{
              formatPayload(selectedDeadLetter.eventJson)
            }}</pre>
          </div>

          <NvField>
            <NvFieldLabel for="dead-letter-ignore-reason">忽略原因</NvFieldLabel>
            <textarea
              id="dead-letter-ignore-reason"
              v-model="ignoreReason"
              rows="2"
              class="min-h-16 w-full rounded-md border bg-transparent px-3 py-2 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              placeholder="说明为什么不再重放这条死信"
            ></textarea>
          </NvField>

          <div class="flex justify-end">
            <NvButton
              type="button"
              variant="outline"
              :disabled="!canManage || actionPending || ignoreReason.trim().length === 0"
              @click="handleIgnore"
            >
              <BanIcon class="size-4" aria-hidden="true" />
              忽略
            </NvButton>
          </div>
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
