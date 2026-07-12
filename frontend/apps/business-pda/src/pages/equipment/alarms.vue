<script setup lang="ts">
import type { BusinessConsoleTelemetryAlarmEventItem } from '@nerv-iip/api-client'
import { alarmLifecycleStatusLabel, alarmSeverityLabel } from '@nerv-iip/business-core'
import { describeRequestError } from '@/api/request-timeout'
import RetryableListError from '@/components/RetryableListError.vue'
import {
  ALARM_SHELVE_DURATIONS_MINUTES,
  useBusinessEquipmentAlarms,
} from '@/composables/useBusinessEquipmentAlarms'
import {
  NvActionSheet,
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileButton,
  NvMobileDialog,
  NvMobileTag,
  NvMobileToast,
  NvScanBar,
  type ActionItem,
} from '@nerv-iip/ui-mobile'
import { ChevronRight } from 'lucide-vue-next'
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '查看报警',
  },
})

type Alarm = BusinessConsoleTelemetryAlarmEventItem

const router = useRouter()

const { filters, alarms, pending, error, refresh, acknowledge, shelve, actionPending } =
  useBusinessEquipmentAlarms()

// 当前是否按设备过滤（用于展示/清除过滤）。
const filteredDevice = computed(() => filters.deviceAssetId)

// ScanBar 扫设备码 → 服务端按 deviceAssetId 过滤。
function onScan(value: string) {
  filters.deviceAssetId = value
}

function clearFilter() {
  filters.deviceAssetId = undefined
}

function statusOf(item: Alarm) {
  return (item.status ?? '').trim().toLowerCase()
}

// 未确认（raised）才提供行内 确认/搁置；已处理行灰显 + 状态标。
function isRaised(item: Alarm) {
  return statusOf(item) === 'raised'
}

function timeText(iso?: string | null) {
  if (!iso) return ''
  const date = new Date(iso)
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })
}

// 行标题：设备 · 报警码（均为业务码，可显示）。
function alarmTitle(item: Alarm) {
  const device = item.deviceAssetId ?? '未知设备'
  const code = item.alarmCode ?? '—'
  return `${device} · 报警码 ${code}`
}

// 行副标题：级别中文 + 发生时间（alarmEventId/externalAlarmId 仅作 key / 透传，不外显）。
function alarmSubtitle(item: Alarm) {
  const parts = [alarmSeverityLabel(item.severity)]
  const raised = timeText(item.raisedAtUtc)
  if (raised) parts.push(raised)
  return parts.join(' · ')
}

// 已处理行的处理人 + 时刻元信息（“张三 · 14:32”）。搁置额外显示解除时刻。
function processedMeta(item: Alarm) {
  const status = statusOf(item)
  if (status === 'acknowledged') {
    return [item.acknowledgedBy, timeText(item.acknowledgedAtUtc)].filter(Boolean).join(' · ')
  }
  if (status === 'shelved') {
    const until = timeText(item.shelvedUntilUtc)
    return [item.shelvedBy, until ? `至 ${until}` : ''].filter(Boolean).join(' · ')
  }
  return ''
}

const TAG_VARIANTS: Record<string, 'success' | 'warning' | 'default'> = {
  acknowledged: 'success',
  shelved: 'warning',
  cleared: 'default',
}
function tagVariant(item: Alarm) {
  return TAG_VARIANTS[statusOf(item)] ?? 'default'
}

const DURATION_LABELS: Record<number, string> = { 30: '30 分钟', 120: '2 小时', 480: '8 小时' }

// --- 稳定的逐操作幂等标识 -----------------------------------------------------
// 用户发起一次确认/搁置时铸造一次 atUtc，重试该操作复用同一 atUtc：
//  - 确认：领域 first-write-wins，重复确认为 no-op；
//  - 搁置：窗口 = [atUtc, atUtc+时长]，复用同 atUtc → 窗口固定、重试不延长。
type PendingAction =
  | { kind: 'ack'; item: Alarm; atUtc: string }
  | { kind: 'shelve'; item: Alarm; atUtc: string; minutes: number }
const pendingAction = ref<PendingAction | null>(null)

// 失败结果：确定性失败（无副作用）可复用同键重试；已发出但结果未知（超时/断网）不盲目重试，
// 交给 verify（刷新列表核对是否已处理）。
const actionError = ref<{ message: string; canRetry: boolean } | null>(null)

async function runPending() {
  const p = pendingAction.value
  if (!p?.item.alarmEventId) return
  actionError.value = null
  try {
    if (p.kind === 'ack') {
      await acknowledge(p.item.alarmEventId, p.atUtc)
      showToast('已确认报警', 'success')
    } else {
      await shelve(p.item.alarmEventId, p.minutes, p.atUtc)
      showToast(`已搁置 ${DURATION_LABELS[p.minutes] ?? ''}`.trim(), 'success')
    }
    pendingAction.value = null
  } catch (e) {
    const info = describeRequestError(e, '操作失败，请重试')
    if (info.indeterminate) {
      // 已发出、结果未知：不盲目重试，刷新列表引导核对。
      void refresh()
      actionError.value = {
        message: `${info.message}。已为你刷新列表，请核对该报警是否已处理，勿重复提交。`,
        canRetry: false,
      }
    } else {
      // 确定性失败：服务端已应答、无挂起副作用 → 复用同一 atUtc 安全重试。
      actionError.value = { message: info.message, canRetry: true }
    }
  }
}

// --- 确认弹层 -----------------------------------------------------------------
const pendingAck = ref<Alarm | null>(null)
const ackOpen = computed({
  get: () => pendingAck.value !== null,
  set: (open) => {
    if (!open) pendingAck.value = null
  },
})
function askAcknowledge(item: Alarm) {
  pendingAck.value = item
}
function confirmAcknowledge() {
  const item = pendingAck.value
  pendingAck.value = null
  if (!item?.alarmEventId) return
  pendingAction.value = { kind: 'ack', item, atUtc: new Date().toISOString() }
  void runPending()
}

// --- 搁置 ActionSheet（30m / 2h / 8h）-----------------------------------------
const shelveActions: ActionItem[] = ALARM_SHELVE_DURATIONS_MINUTES.map((minutes) => ({
  label: `搁置 ${DURATION_LABELS[minutes] ?? `${minutes} 分钟`}`,
  value: String(minutes),
}))
const pendingShelve = ref<Alarm | null>(null)
const shelveOpen = computed({
  get: () => pendingShelve.value !== null,
  set: (open) => {
    if (!open) pendingShelve.value = null
  },
})
function askShelve(item: Alarm) {
  pendingShelve.value = item
}
function onShelveDuration(value: string) {
  const item = pendingShelve.value
  pendingShelve.value = null
  const minutes = Number(value)
  if (!item?.alarmEventId || !Number.isFinite(minutes)) return
  pendingAction.value = { kind: 'shelve', item, atUtc: new Date().toISOString(), minutes }
  void runPending()
}

// --- 失败对话框（重试复用同键 / 结果未知引导核对）-----------------------------
function onErrorConfirm() {
  if (actionError.value?.canRetry) {
    actionError.value = null
    void runPending() // 复用 pendingAction.atUtc
  } else {
    actionError.value = null
    pendingAction.value = null
  }
}
function cancelAction() {
  actionError.value = null
  pendingAction.value = null
}

// --- 行详情（去报修入口）------------------------------------------------------
const detail = ref<Alarm | null>(null)
const detailOpen = computed({
  get: () => detail.value !== null,
  set: (open) => {
    if (!open) detail.value = null
  },
})
function openDetail(item: Alarm) {
  detail.value = item
}

// 去报修：把设备 + 来源报警事件 ID 作为上下文带入报修页（repair.vue 消费 query 预填）。
function goRepair(item: Alarm) {
  detail.value = null
  void router.push({
    path: '/equipment/repair',
    query: {
      deviceAssetId: item.deviceAssetId,
      sourceAlarmId: item.alarmEventId,
    },
  })
}

// --- 成功轻反馈（toast）------------------------------------------------------
const toast = reactive<{ show: boolean; message: string; type: 'success' | 'error' }>({
  show: false,
  message: '',
  type: 'success',
})
function showToast(message: string, type: 'success' | 'error') {
  toast.message = message
  toast.type = type
  toast.show = true
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">查看报警</h1>
      </div>
    </template>

    <div class="space-y-4 p-4">
      <!-- 按设备过滤 -->
      <section class="space-y-2">
        <NvScanBar placeholder="扫描设备码筛选报警" @scan="onScan" />
        <div
          v-if="filteredDevice"
          class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-2 text-sm"
        >
          <span class="truncate text-foreground">仅显示设备 {{ filteredDevice }}</span>
          <button
            data-testid="clear-filter"
            type="button"
            class="ml-3 shrink-0 rounded-md border border-border px-3 py-1 text-sm text-foreground"
            @click="clearFilter"
          >
            清除筛选
          </button>
        </div>
      </section>

      <!-- 报警列表 -->
      <section class="space-y-2">
        <h2 class="text-sm font-medium text-muted-foreground">设备报警</h2>

        <RetryableListError
          v-if="error"
          :error="error"
          :pending="pending"
          fallback="报警加载失败，请稍后重试。"
          test-id="alarms-error"
          @retry="() => refresh()"
        />

        <div v-else-if="pending" class="px-4 py-6 text-center text-sm text-muted-foreground">
          加载中…
        </div>

        <div
          v-else-if="alarms.length === 0"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          暂无设备报警
        </div>

        <!-- 行非交互（避免行内交互控件嵌套在 role=button 行内导致键盘冒泡/辅助技术歧义）；
             确认/搁置/详情均为行内同级控件。 -->
        <div v-else class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="item in alarms"
            :key="item.alarmEventId"
            :title="alarmTitle(item)"
            :subtitle="alarmSubtitle(item)"
            :interactive="false"
            :class="isRaised(item) ? undefined : 'opacity-60'"
          >
            <template v-if="processedMeta(item)" #meta>
              <div class="truncate text-xs text-muted-foreground">{{ processedMeta(item) }}</div>
            </template>
            <template #trailing>
              <div class="flex shrink-0 items-center gap-2">
                <template v-if="isRaised(item)">
                  <NvMobileButton
                    :data-testid="`ack-${item.alarmEventId}`"
                    variant="primary"
                    size="sm"
                    :disabled="actionPending"
                    @click="askAcknowledge(item)"
                  >
                    确认
                  </NvMobileButton>
                  <NvMobileButton
                    :data-testid="`shelve-${item.alarmEventId}`"
                    variant="outline"
                    size="sm"
                    :disabled="actionPending"
                    @click="askShelve(item)"
                  >
                    搁置
                  </NvMobileButton>
                </template>
                <NvMobileTag
                  v-else
                  :data-testid="`status-${item.alarmEventId}`"
                  :variant="tagVariant(item)"
                >
                  {{ alarmLifecycleStatusLabel(item.status) }}
                </NvMobileTag>

                <!-- 详情入口：独立同级按钮（承载去报修），非嵌套在可交互行内 -->
                <button
                  :data-testid="`detail-${item.alarmEventId}`"
                  type="button"
                  class="flex size-9 shrink-0 items-center justify-center rounded-md text-muted-foreground"
                  :aria-label="`${alarmTitle(item)} 详情`"
                  @click="openDetail(item)"
                >
                  <ChevronRight class="size-5" aria-hidden="true" />
                </button>
              </div>
            </template>
          </NvListRow>
        </div>
      </section>
    </div>

    <!-- 确认弹层（轻量二次确认）-->
    <NvMobileDialog
      :open="ackOpen"
      title="确认该报警？"
      :description="pendingAck ? alarmTitle(pendingAck) : ''"
      confirm-text="确认"
      @update:open="ackOpen = $event"
      @confirm="confirmAcknowledge"
    />

    <!-- 搁置时长选择 -->
    <NvActionSheet
      v-model:open="shelveOpen"
      title="搁置多久？"
      :description="pendingShelve ? alarmTitle(pendingShelve) : undefined"
      :actions="shelveActions"
      @select="onShelveDuration"
    />

    <!-- 失败对话框：确定性失败可重试（复用同键）；结果未知只提示核对 -->
    <NvMobileDialog
      :open="actionError !== null"
      :title="actionError?.canRetry ? '操作失败' : '提交结果未知'"
      :description="actionError?.message ?? ''"
      :confirm-text="actionError?.canRetry ? '重试' : '我知道了'"
      :show-cancel="actionError?.canRetry ?? false"
      cancel-text="取消"
      @update:open="
        (open) => {
          if (!open) actionError = null
        }
      "
      @confirm="onErrorConfirm"
      @cancel="cancelAction"
    />

    <!-- 行详情：保留去报修入口 -->
    <NvBottomSheet
      :open="detailOpen"
      :title="detail ? alarmTitle(detail) : ''"
      @update:open="detailOpen = $event"
    >
      <div v-if="detail" class="space-y-3 pb-2">
        <dl class="space-y-2 text-sm">
          <div class="flex justify-between gap-3">
            <dt class="text-muted-foreground">级别</dt>
            <dd class="text-foreground">{{ alarmSeverityLabel(detail.severity) }}</dd>
          </div>
          <div class="flex justify-between gap-3">
            <dt class="text-muted-foreground">状态</dt>
            <dd class="text-foreground">{{ alarmLifecycleStatusLabel(detail.status) }}</dd>
          </div>
          <div v-if="timeText(detail.raisedAtUtc)" class="flex justify-between gap-3">
            <dt class="text-muted-foreground">发生时间</dt>
            <dd class="text-foreground">
              {{ new Date(detail.raisedAtUtc!).toLocaleString('zh-CN') }}
            </dd>
          </div>
          <div v-if="processedMeta(detail)" class="flex justify-between gap-3">
            <dt class="text-muted-foreground">处理</dt>
            <dd class="text-foreground">{{ processedMeta(detail) }}</dd>
          </div>
        </dl>

        <NvMobileButton
          :data-testid="`repair-${detail.alarmEventId}`"
          variant="primary"
          size="lg"
          block
          @click="goRepair(detail)"
        >
          去报修
        </NvMobileButton>
      </div>
    </NvBottomSheet>

    <NvMobileToast
      :show="toast.show"
      :message="toast.message"
      :type="toast.type"
      @update:show="toast.show = $event"
    />
  </NvAppShellMobile>
</template>
