<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import { useLifecycleActionRecovery } from '@/composables/lifecycleActionRecovery'
import ListScopeMeta from '@/components/ListScopeMeta.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useIdempotentWriteIntent } from '@/composables/useIdempotentWriteIntent'
import { usePendingWriteLeaveGuard } from '@/composables/usePendingWriteLeaveGuard'
import { useWmsOutbound } from '@/composables/useBusinessWms'
import {
  outboundOrderStatusLabel,
  outboundReviewFlow,
  statusActionGate,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileResult,
  NvMobileToast,
  NvScanBar,
} from '@nerv-iip/ui-mobile'
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
  orders,
  total,
  pending,
  error,
  refresh,
  completeOutbound,
  completePending,
  organizationId,
  environmentId,
  scopeReady,
  lastUpdatedAt,
  hasSuccessfulResponse,
  hasFailedResponse,
} = useWmsOutbound()
const reviewScope = computed(() =>
  scopeReady.value ? '当前登录组织 / 当前业务环境' : '组织/环境范围未就绪',
)
const reviewTotal = computed(() => total.value)

// 选中的出库单号 + GUID（GUID 仅用于 complete 调用与 :key，绝不展示）。
const selectedOrderId = ref('')
const selectedOrderNo = ref('')
const sheetOpen = ref(false)
const completed = ref(false)

// 每次用户发起操作（点单开抽屉）生成一次稳定幂等键，跨重试复用以防丢响应重复出库；
// 选新单/继续后再点单才换新键。绝不在重试时重新生成。
const intent = useIdempotentWriteIntent<{
  packReviewNo: string
  passed: boolean
  idempotencyKey: string
}>(makeIdempotencyKey)
const intentLocked = intent.locked
usePendingWriteLeaveGuard(intentLocked)

// 复核录入：复核单号 + 通过/不通过开关。
const packReviewNo = ref('')
const passed = ref(true)
// 复核单号需有非空白内容才算有效（纯空格 "   " 不可提交）。
const validPackReviewNo = computed(() => packReviewNo.value.trim().length > 0)
watch([packReviewNo, passed], () => {
  intent.inputChanged()
  submitError.value = ''
})

// outboundReviewFlow 驱动进度：selectOrder→enterReviewNo→complete。
const flowCtx = computed(() => ({
  orderId: selectedOrderId.value || undefined,
  packReviewNo: packReviewNo.value.trim() || undefined,
  completed: completed.value,
}))
const flowStep = computed(() => outboundReviewFlow.currentStep(flowCtx.value).id)
// 当前步骤暴露给抽屉做进度提示（enterReviewNo→complete）。
const reviewStepHint = computed(() =>
  flowStep.value === 'complete' ? '复核单号已填，待提交' : '请填写复核单号',
)

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
  packReviewNo.value = ''
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
  // 防重：pending 中或复核单号无有效内容直接早退（按钮也已禁用，UI 守双道）。
  if (completePending.value || !validPackReviewNo.value) return
  submitError.value = ''
  try {
    const payload = intent.payload((idempotencyKey) => ({
      packReviewNo: packReviewNo.value.trim(),
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
    completed.value = true
  } catch (e) {
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
  selectedOrderId.value = ''
  selectedOrderNo.value = ''
  packReviewNo.value = ''
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
      title="出库复核已完成"
      :description="selectedOrderNo ? `出库单 ${selectedOrderNo}` : undefined"
    >
      <template #actions>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="backToList"
        >
          继续
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goHome"
        >
          返回
        </button>
      </template>
    </NvMobileResult>

    <div v-else class="space-y-4 p-4">
      <NvScanBar placeholder="扫描出库单号" :active="scanActive" @scan="onScan" />
      <ListScopeMeta
        :scope="reviewScope"
        source="出库复核服务（组织/环境范围，暂不支持按操作员归属筛选）"
        :loaded="orders.length"
        :total="reviewTotal"
        :updated-at="lastUpdatedAt"
        :failed="hasFailedResponse"
        failure-explanation="出库复核服务未成功返回，请刷新重试。"
        :empty="!scopeReady || showEmpty"
        :empty-explanation="
          scopeReady
            ? '当前组织/环境范围没有待复核出库单；暂不支持按操作员归属筛选，空态不代表个人任务。'
            : '缺少组织或环境范围，未发起查询。'
        "
      />

      <RetryableListError
        v-if="error || hasFailedResponse"
        :error="error ?? '出库复核服务未成功返回'"
        :pending="pending"
        fallback="单据加载失败，请下拉重试或检查网络。"
        test-id="error-banner"
        @retry="() => refresh()"
      />

      <div
        v-if="showEmpty"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无待发货单据（当前组织/环境范围；暂不支持按操作员归属筛选）
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <NvListRow
          v-for="order in orders"
          :key="order.outboundOrderId"
          :title="order.outboundOrderNo ?? ''"
          :subtitle="outboundOrderStatusLabel(order.status)"
          @select="selectOrder(order.outboundOrderId, order.outboundOrderNo, order.status)"
        />
      </div>
    </div>

    <!-- 复核完成确认抽屉 -->
    <NvBottomSheet :open="sheetOpen" title="完成出库复核" @update:open="onSheetOpenChange">
      <div class="space-y-4">
        <p v-if="selectedOrderNo" class="text-sm text-muted-foreground">
          出库单 {{ selectedOrderNo }}
        </p>
        <p class="text-xs text-muted-foreground">{{ reviewStepHint }}</p>

        <label class="block space-y-2">
          <span class="text-sm font-medium text-foreground">复核单号</span>
          <input
            v-model="packReviewNo"
            data-testid="pack-review-no"
            type="text"
            :disabled="intentLocked"
            inputmode="text"
            placeholder="请输入复核单号"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base text-foreground"
          />
        </label>

        <div class="flex items-center justify-between">
          <span class="text-sm font-medium text-foreground">复核结果</span>
          <button
            type="button"
            data-testid="toggle-passed"
            :disabled="intentLocked"
            class="min-h-touch rounded-lg border px-4 text-base font-medium"
            :class="
              passed
                ? 'border-primary bg-primary/10 text-primary'
                : 'border-destructive bg-destructive/10 text-destructive'
            "
            @click="passed = !passed"
          >
            {{ passed ? '通过' : '不通过' }}
          </button>
        </div>

        <p v-if="submitError" class="text-sm text-destructive">{{ submitError }}</p>

        <div class="space-y-2 pt-2">
          <button
            type="button"
            data-testid="confirm-complete"
            :disabled="completePending || !validPackReviewNo"
            class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
            @click="confirmComplete"
          >
            {{ completePending ? '提交中…' : intentLocked ? '按原内容重试' : '确认完成' }}
          </button>
          <button
            type="button"
            :disabled="intentLocked"
            class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground disabled:opacity-60"
            @click="closeSheet"
          >
            取消
          </button>
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
