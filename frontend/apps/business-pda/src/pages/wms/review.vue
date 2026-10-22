<script setup lang="ts">
import WmsOperationalCandidatePicker from '@/components/wms/WmsOperationalCandidatePicker.vue'
import WmsPagedListFrame from '@/components/wms/WmsPagedListFrame.vue'
import WmsScopeStatusFilter from '@/components/wms/WmsScopeStatusFilter.vue'
import { useLifecycleActionRecovery } from '@/composables/lifecycleActionRecovery'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useIdempotentWriteIntent } from '@/composables/useIdempotentWriteIntent'
import { usePendingWriteLeaveGuard } from '@/composables/usePendingWriteLeaveGuard'
import { useWmsOutbound } from '@/composables/useBusinessWms'
import { useWmsOperationalCandidates } from '@/composables/useWmsOperationalCandidates'
import { PDA_OUTBOUND_ORDER_STATUS_OPTIONS } from '@/data/wmsReference'
import { outboundOrderStatusLabel, statusActionGate } from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileButton,
  NvMobileResult,
  NvMobileSwitch,
  NvMobileToast,
  NvScanBar,
} from '@nerv-iip/ui-mobile'
import { BusinessOperationPendingError } from '@nerv-iip/api-client'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '复核发货',
  },
})

const router = useRouter()
const {
  filters,
  scopeKey,
  scopeOptions,
  selectedScopeLabel,
  orders,
  total,
  pending,
  refreshing,
  loadingMore,
  error,
  loadMoreError,
  refresh,
  loadMore,
  completeOutbound,
  completePending,
  organizationId,
  environmentId,
  scopeKind,
  scopeId,
  scopeReady,
  hasSuccessfulResponse,
  hasFailedResponse,
} = useWmsOutbound({ status: 'Open' })
const candidates = useWmsOperationalCandidates('shipment', {
  organizationId,
  environmentId,
  scopeKind,
  scopeId,
  scopeReady,
  filters,
})
async function refreshAll() {
  await Promise.all([refresh(), candidates.refresh()])
}
const reviewScope = computed(() =>
  scopeReady.value ? selectedScopeLabel.value : 'WMS 作业范围未就绪',
)
const reviewTotal = computed(() => total.value)
const reviewStatusOptions = PDA_OUTBOUND_ORDER_STATUS_OPTIONS
const taskListFilterState = computed(() => ({
  scopeKey: scopeKey.value,
  status: filters.status ?? '',
  keyword: filters.keyword ?? '',
  locationCode: filters.locationCode ?? '',
  lotNo: filters.lotNo ?? '',
}))
function restoreTaskListState(state: { filters: Record<string, unknown> }) {
  scopeKey.value = String(state.filters.scopeKey ?? scopeKey.value)
  filters.status = String(state.filters.status ?? '') || undefined
  filters.keyword = String(state.filters.keyword ?? '') || undefined
  filters.locationCode = String(state.filters.locationCode ?? '') || undefined
  filters.lotNo = String(state.filters.lotNo ?? '') || undefined
}

// 选中的出库单号 + GUID（GUID 仅用于 complete 调用与 :key，绝不展示）。
const selectedOrderId = ref('')
const selectedOrderNo = ref('')
const sheetOpen = ref(false)
const completed = ref(false)
/** 复核已落库、库存仍在异步过账（#3926）：结果页如实说「已提交、正在过账」。 */
const postingPending = ref(false)

// 每次用户发起操作（点单开抽屉）生成一次稳定幂等键，跨重试复用以防丢响应重复出库；
// 选新单/继续后再点单才换新键。绝不在重试时重新生成。
const intent = useIdempotentWriteIntent<{
  passed: boolean
  idempotencyKey: string
}>(makeIdempotencyKey)
const intentLocked = intent.locked
usePendingWriteLeaveGuard(intentLocked)

// 复核录入：只有通过/不通过开关；复核单号由系统按编码规则生成，现场不填不扫。
const passed = ref(true)
watch(passed, () => {
  intent.inputChanged()
  submitError.value = ''
})

// 抽屉或结果展示时停止扫码焦点抢夺，避免破坏浮层 focus-trap。
const scanActive = computed(() => !sheetOpen.value && !completed.value)

const submitError = ref('')

// 空态仅在「无待发货单据且无加载/错误」时出现，避免与错误/加载态打架。
const showEmpty = computed(
  () =>
    !pending.value &&
    !error.value &&
    !hasFailedResponse.value &&
    hasSuccessfulResponse.value &&
    orders.value.length === 0,
)

function onScan(value: string) {
  filters.keyword = value
}

function canComplete(status?: string) {
  return statusActionGate({
    domain: 'wms-outbound',
    action: 'complete',
    facts: { status },
  }).executable
}

function selectOrder(
  outboundOrderId: string | undefined,
  outboundOrderNo: string | undefined,
  status?: string,
) {
  if (!outboundOrderId) return
  if (!canComplete(status)) return
  selectedOrderId.value = outboundOrderId
  selectedOrderNo.value = outboundOrderNo ?? ''
  passed.value = true
  // 新操作开始：换一把新幂等键。
  intent.start()
  submitError.value = ''
  sheetOpen.value = true
}

function closeSheet() {
  if (intentLocked.value) return
  sheetOpen.value = false
}

function onSheetOpenChange(open: boolean) {
  if (!open && intentLocked.value) return
  sheetOpen.value = open
}

const lifecycleRecovery = useLifecycleActionRecovery({
  reset: resetFlow,
  refresh,
})

async function confirmComplete() {
  // 防重：pending 中直接早退（按钮也已禁用，UI 守双道）。
  if (completePending.value) return
  submitError.value = ''
  try {
    const payload = intent.payload((idempotencyKey) => ({
      passed: passed.value,
      idempotencyKey,
    }))
    // 重试复用同一幂等键（不重新生成），#188 客户端去重可识别为同一操作。
    await completeOutbound(selectedOrderId.value, payload, {
      attempt: intent.attempt.value,
      onCommandAttempt: intent.markCommandAttempt,
    })
    // 成功后立刻关抽屉并切到结果态，重复点击无法再触发。
    sheetOpen.value = false
    postingPending.value = false
    completed.value = true
  } catch (e) {
    // 复核已被接受、只是库存还在过账：这是中间态不是失败，照样进结果态。
    if (e instanceof BusinessOperationPendingError) {
      sheetOpen.value = false
      postingPending.value = true
      completed.value = true
      return
    }
    if (await lifecycleRecovery.handle(e)) return
    const info = intent.recordFailure(e, '完成出库复核失败')
    submitError.value = intentLocked.value
      ? `${info.message}。提交结果未知，仅可按原内容重试。`
      : info.message
  }
}

function resetFlow() {
  sheetOpen.value = false
  completed.value = false
  postingPending.value = false
  selectedOrderId.value = ''
  selectedOrderNo.value = ''
  passed.value = true
  // 清空操作键：下次点单会铸新键，保证新操作 ≠ 旧键。
  intent.reset()
  submitError.value = ''
}

function backToList() {
  resetFlow()
}

function goHome() {
  router.push('/').catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">复核发货</h1>
      </div>
    </template>

    <!-- 成功结果态 -->
    <NvMobileResult
      v-if="completed"
      status="success"
      :title="postingPending ? '出库复核已提交' : '出库复核已完成'"
      :description="
        postingPending
          ? `${selectedOrderNo ? `出库单 ${selectedOrderNo}，` : ''}库存正在过账，请稍后在列表查看过账结果。`
          : selectedOrderNo
            ? `出库单 ${selectedOrderNo}`
            : undefined
      "
    >
      <template #actions>
        <NvMobileButton block size="lg" variant="primary" @click="backToList">
          继续
        </NvMobileButton>
        <NvMobileButton block size="lg" variant="outline" @click="goHome"> 返回 </NvMobileButton>
      </template>
    </NvMobileResult>

    <div v-else class="flex h-full min-h-0 flex-col">
      <div class="space-y-3 border-b border-border bg-card px-4 py-3">
        <NvScanBar placeholder="扫描出库单号" :active="scanActive" @scan="onScan" />
        <WmsScopeStatusFilter
          v-model:scope-key="scopeKey"
          v-model:status="filters.status"
          :scope-options="scopeOptions"
          :status-options="reviewStatusOptions"
        />
        <WmsOperationalCandidatePicker
          v-model:location-code="filters.locationCode"
          v-model:lot-no="filters.lotNo"
          v-model:search-keyword="candidates.searchKeyword.value"
          :location-options="candidates.locationOptions.value"
          :lot-options="candidates.lotOptions.value"
          :ready="candidates.ready.value"
          :truncated="candidates.truncated.value"
          :pending="candidates.pending.value"
          :error="candidates.error.value"
          :scan-overrides="candidates.scanOverrides.value"
          :show-scanner="false"
          @scan-override-change="candidates.setScanOverride"
          @retry="candidates.refresh"
        />
      </div>

      <WmsPagedListFrame
        state-key="wms-outbound-review"
        empty-description="暂无待发货单据"
        :filter-state="taskListFilterState"
        :error="error ?? (hasFailedResponse ? '待发货单据加载失败，请重试。' : undefined)"
        :load-more-error="loadMoreError"
        :refreshing="refreshing"
        :loading-more="loadingMore"
        :pending="pending"
        :loaded="orders.length"
        :total="reviewTotal"
        @refresh="refreshAll"
        @load-more="loadMore"
        @restore="restoreTaskListState"
        @retry="refreshAll"
        @retry-load-more="loadMore"
      >
        <div class="space-y-4 px-4 py-3">
          <div
            v-if="showEmpty"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            “{{ reviewScope }}”在当前状态下暂无待发货单据
          </div>

          <div v-else class="overflow-hidden rounded-lg border border-border">
            <NvListRow
              v-for="order in orders"
              :key="order.outboundOrderId"
              :title="order.outboundOrderNo ?? ''"
              :subtitle="outboundOrderStatusLabel(order.status)"
              :interactive="canComplete(order.status)"
              @select="selectOrder(order.outboundOrderId, order.outboundOrderNo, order.status)"
            />
          </div>
        </div>
      </WmsPagedListFrame>
    </div>

    <!-- 复核完成确认抽屉 -->
    <NvBottomSheet :open="sheetOpen" title="完成出库复核" @update:open="onSheetOpenChange">
      <div class="space-y-4">
        <p v-if="selectedOrderNo" class="text-sm text-muted-foreground">
          出库单 {{ selectedOrderNo }}
        </p>
        <div class="flex items-center justify-between">
          <div>
            <p class="text-sm font-medium text-foreground">复核结果</p>
            <p class="text-xs text-muted-foreground">{{ passed ? '通过' : '不通过' }}</p>
          </div>
          <NvMobileSwitch v-model="passed" data-testid="toggle-passed" :disabled="intentLocked" />
        </div>

        <p v-if="submitError" class="text-sm text-destructive">{{ submitError }}</p>

        <div class="space-y-2 pt-2">
          <NvMobileButton
            block
            size="lg"
            variant="primary"
            data-testid="confirm-complete"
            :disabled="completePending"
            @click="confirmComplete"
          >
            {{ completePending ? '提交中…' : intentLocked ? '按原内容重试' : '确认完成' }}
          </NvMobileButton>
          <NvMobileButton
            block
            size="lg"
            variant="outline"
            :disabled="intentLocked"
            @click="closeSheet"
          >
            取消
          </NvMobileButton>
        </div>
      </div>
    </NvBottomSheet>

    <NvMobileToast
      :show="lifecycleRecovery.toast.value.show"
      :message="lifecycleRecovery.toast.value.message"
      :type="lifecycleRecovery.toast.value.type"
      @update:show="lifecycleRecovery.setToastOpen"
    />
  </NvAppShellMobile>
</template>
