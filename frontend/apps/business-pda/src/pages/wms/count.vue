<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import WmsOperationalCandidatePicker from '@/components/wms/WmsOperationalCandidatePicker.vue'
import WmsPagedListFrame from '@/components/wms/WmsPagedListFrame.vue'
import WmsScopeStatusFilter from '@/components/wms/WmsScopeStatusFilter.vue'
import { useLifecycleActionRecovery } from '@/composables/lifecycleActionRecovery'
import ListScopeMeta from '@/components/ListScopeMeta.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useIdempotentWriteIntent } from '@/composables/useIdempotentWriteIntent'
import { usePendingWriteLeaveGuard } from '@/composables/usePendingWriteLeaveGuard'
import { useWmsCount } from '@/composables/useBusinessWms'
import { useWmsOperationalCandidates } from '@/composables/useWmsOperationalCandidates'
import { PDA_COUNT_EXECUTION_STATUS_OPTIONS } from '@/data/wmsReference'
import {
  countExecutionFlow,
  countExecutionStatusLabel,
  statusActionGate,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvCell,
  NvListRow,
  NvMobileButton,
  NvMobileResult,
  NvMobileToast,
  NvNumberKeyboard,
} from '@nerv-iip/ui-mobile'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '盘点',
  },
})

const router = useRouter()
const {
  filters,
  scopeKey,
  scopeOptions,
  selectedScopeLabel,
  executions,
  total,
  pending,
  refreshing,
  loadingMore,
  error,
  refresh,
  loadMore,
  completeCount,
  completePending,
  organizationId,
  environmentId,
  scopeKind,
  scopeId,
  scopeReady,
  lastUpdatedAt,
  hasSuccessfulResponse,
  hasFailedResponse,
} = useWmsCount({ status: 'Open' })
const candidates = useWmsOperationalCandidates('count', {
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
const countScope = computed(() =>
  scopeReady.value ? selectedScopeLabel.value : 'WMS 作业范围未就绪',
)
const countTotal = computed(() => total.value)
const countStatusOptions = PDA_COUNT_EXECUTION_STATUS_OPTIONS

// 选中的盘点号 + GUID（GUID 仅用于 complete 调用与 :key，绝不展示）。
const selectedExecutionId = ref('')
const selectedCountNo = ref('')
const expectedQuantity = ref(0)
const sheetOpen = ref(false)
const completed = ref(false)

// 每次用户发起操作（点任务开抽屉）生成一次稳定幂等键，跨重试复用以防丢响应重复提交；
// 选新任务/继续后再点任务才换新键。绝不在重试时重新生成。
const intent = useIdempotentWriteIntent<{
  countedQuantity: number
  idempotencyKey: string
}>(makeIdempotencyKey)
const intentLocked = intent.locked
usePendingWriteLeaveGuard(intentLocked)

// 数量只通过移动端大键盘录入，触发单元格保持只读，避免系统软键盘抢占扫码焦点。
const countedQuantityText = ref('')
const numberKeyboardOpen = ref(false)
const countedQuantity = computed(() => Number(countedQuantityText.value))
watch(countedQuantityText, () => {
  intent.inputChanged()
  submitError.value = ''
})
// 有效：非空、可解析为有限数且非负。
const validCount = computed(() => {
  const text = String(countedQuantityText.value).trim()
  if (text === '') return false
  const n = Number(text)
  return Number.isFinite(n) && n >= 0
})
// 差异实时提示（仅在已填有效值时展示）。
const variance = computed(() => countedQuantity.value - expectedQuantity.value)

// countExecutionFlow 驱动进度：selectExecution→enterCount→complete。
const flowCtx = computed(() => ({
  countExecutionId: selectedExecutionId.value || undefined,
  countEntered: validCount.value || undefined,
  completed: completed.value,
}))
const flowStep = computed(() => countExecutionFlow.currentStep(flowCtx.value).id)
const countStepHint = computed(() =>
  flowStep.value === 'complete' ? '实盘数已填，待提交' : '请填写实盘数量',
)

// 抽屉或结果展示时停止扫码焦点抢夺，避免破坏浮层 focus-trap。
const scanActive = computed(() => !sheetOpen.value && !completed.value && !numberKeyboardOpen.value)

const submitError = ref('')

// 空态仅在「无盘点任务且无加载/错误」时出现，避免与错误/加载态打架。
const showEmpty = computed(
  () =>
    !pending.value &&
    !error.value &&
    !hasFailedResponse.value &&
    hasSuccessfulResponse.value &&
    executions.value.length === 0,
)

function displayCountStatus(status?: string) {
  if (status?.trim().toLowerCase() === 'open') return '待盘点'
  return countExecutionStatusLabel(status)
}

function canComplete(status?: string) {
  return statusActionGate({
    domain: 'wms-count',
    action: 'complete',
    facts: { status },
  }).executable
}

function selectExecution(
  countExecutionId: string | undefined,
  countNo: string | undefined,
  expected: number | undefined,
  status?: string,
) {
  if (!countExecutionId) return
  if (!canComplete(status)) return
  selectedExecutionId.value = countExecutionId
  selectedCountNo.value = countNo ?? ''
  expectedQuantity.value = expected ?? 0
  countedQuantityText.value = ''
  // 新操作开始：换一把新幂等键。
  intent.start()
  submitError.value = ''
  sheetOpen.value = true
}

function openCountKeyboard() {
  if (intentLocked.value) return
  numberKeyboardOpen.value = true
}

function closeSheet() {
  if (intentLocked.value) return
  numberKeyboardOpen.value = false
  sheetOpen.value = false
}

function onSheetOpenChange(open: boolean) {
  if (!open && intentLocked.value) return
  if (!open) numberKeyboardOpen.value = false
  sheetOpen.value = open
}

const lifecycleRecovery = useLifecycleActionRecovery({
  reset: resetFlow,
  refresh,
})

async function confirmComplete() {
  // 防重：pending 中或实盘数无效直接早退（按钮也已禁用，UI 守双道）。
  if (completePending.value || !validCount.value) return
  numberKeyboardOpen.value = false
  submitError.value = ''
  try {
    const payload = intent.payload((idempotencyKey) => ({
      countedQuantity: countedQuantity.value,
      idempotencyKey,
    }))
    // 重试复用同一幂等键（不重新生成），#188 客户端去重可识别为同一操作。
    await completeCount(selectedExecutionId.value, payload, {
      attempt: intent.attempt.value,
      onCommandAttempt: intent.markCommandAttempt,
    })
    // 成功后立刻关抽屉并切到结果态，重复点击无法再触发。
    sheetOpen.value = false
    completed.value = true
  } catch (e) {
    if (await lifecycleRecovery.handle(e)) return
    const info = intent.recordFailure(e, '提交盘点失败')
    submitError.value = intentLocked.value
      ? `${info.message}。提交结果未知，仅可按原内容重试。`
      : info.message
  }
}

function resetFlow() {
  numberKeyboardOpen.value = false
  sheetOpen.value = false
  completed.value = false
  selectedExecutionId.value = ''
  selectedCountNo.value = ''
  expectedQuantity.value = 0
  countedQuantityText.value = ''
  // 清空操作键：下次点任务会铸新键，保证新操作 ≠ 旧键。
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
        <h1 class="text-lg font-semibold text-foreground">盘点</h1>
      </div>
    </template>

    <!-- 成功结果态 -->
    <NvMobileResult
      v-if="completed"
      status="success"
      title="盘点已提交"
      :description="selectedCountNo ? `盘点 ${selectedCountNo}` : undefined"
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
        <WmsScopeStatusFilter
          v-model:scope-key="scopeKey"
          v-model:status="filters.status"
          :scope-options="scopeOptions"
          :status-options="countStatusOptions"
        />
        <WmsOperationalCandidatePicker
          v-model:location-code="filters.locationCode"
          v-model:search-keyword="candidates.searchKeyword.value"
          :location-options="candidates.locationOptions.value"
          :lot-options="candidates.lotOptions.value"
          :ready="candidates.ready.value"
          :source-label="candidates.sourceLabel.value"
          :as-of-utc="candidates.asOfUtc.value"
          :freshness-utc="candidates.freshnessUtc.value"
          :truncated="candidates.truncated.value"
          :pending="candidates.pending.value"
          :error="candidates.error.value"
          :active="scanActive"
          :show-lot="false"
          @retry="candidates.refresh"
        />
        <ListScopeMeta
          :scope="countScope"
          source="WMS 盘点作业范围目录"
          :loaded="executions.length"
          :total="countTotal"
          :updated-at="lastUpdatedAt"
          :failed="hasFailedResponse"
          failure-explanation="盘点任务服务未成功返回，请刷新重试。"
          :empty="!scopeReady || showEmpty"
          :empty-explanation="
            scopeReady
              ? `“${countScope}”在当前状态下没有盘点任务。`
              : 'WMS 未返回可用作业范围，未发起列表查询。'
          "
        />

        <RetryableListError
          v-if="error || hasFailedResponse"
          :error="error ?? '盘点任务服务未成功返回'"
          :pending="pending"
          fallback="盘点任务加载失败，请下拉重试或检查网络。"
          test-id="error-banner"
          @retry="refreshAll"
        />
      </div>

      <WmsPagedListFrame
        :refreshing="refreshing"
        :loading-more="loadingMore"
        :pending="pending"
        :loaded="executions.length"
        :total="countTotal"
        @refresh="refreshAll"
        @load-more="loadMore"
      >
        <div class="space-y-4 px-4 py-3">
          <div
            v-if="showEmpty"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            “{{ countScope }}”在当前状态下暂无盘点任务；数据来自 WMS 派工
          </div>

          <div v-else class="overflow-hidden rounded-lg border border-border">
            <NvListRow
              v-for="execution in executions"
              :key="execution.countExecutionId"
              :title="`盘点 ${execution.countNo ?? ''}`"
              :subtitle="`SKU ${execution.skuCode ?? ''} · 库位 ${execution.locationCode ?? ''} · 预期 ${execution.expectedQuantity ?? 0} · ${displayCountStatus(execution.status)}`"
              :interactive="canComplete(execution.status)"
              @select="
                selectExecution(
                  execution.countExecutionId,
                  execution.countNo,
                  execution.expectedQuantity,
                  execution.status,
                )
              "
            />
          </div>
        </div>
      </WmsPagedListFrame>
    </div>

    <!-- 完成盘点确认抽屉 -->
    <NvBottomSheet :open="sheetOpen" title="完成盘点" @update:open="onSheetOpenChange">
      <div class="space-y-4">
        <p v-if="selectedCountNo" class="text-sm text-muted-foreground">
          盘点 {{ selectedCountNo }}
        </p>
        <p class="text-xs text-muted-foreground">{{ countStepHint }}</p>

        <div class="overflow-hidden rounded-xl border border-border">
          <NvCell data-testid="expected-quantity" title="预期数量" :value="expectedQuantity" />
          <NvCell
            data-testid="counted-quantity"
            title="实盘数量"
            :value="countedQuantityText || '点击录入'"
            :arrow="!intentLocked"
            :aria-disabled="intentLocked"
            @click="openCountKeyboard"
          />
        </div>

        <p v-if="validCount" class="text-sm text-muted-foreground">
          差异 {{ variance > 0 ? `+${variance}` : variance }}
        </p>

        <p v-if="submitError" class="text-sm text-destructive">{{ submitError }}</p>

        <div class="space-y-2 pt-2">
          <NvMobileButton
            block
            size="lg"
            variant="primary"
            data-testid="confirm-complete"
            :disabled="completePending || !validCount"
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

    <NvNumberKeyboard
      v-model="countedQuantityText"
      v-model:show="numberKeyboardOpen"
      title="录入实盘数量"
      extra-key="."
    />

    <NvMobileToast
      :show="lifecycleRecovery.toast.value.show"
      :message="lifecycleRecovery.toast.value.message"
      :type="lifecycleRecovery.toast.value.type"
      @update:show="lifecycleRecovery.setToastOpen"
    />
  </NvAppShellMobile>
</template>
