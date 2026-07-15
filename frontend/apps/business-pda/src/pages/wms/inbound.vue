<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import {
  useWmsInbound,
  useWmsReceivingQualityGates,
  type ReceivingQualityGateLine,
} from '@/composables/useBusinessWms'
import {
  aggregateReceivingGateStatus,
  expiryToneFromDate,
  expiryToneLabel,
  inboundOrderStatusLabel,
  inboundReceiveFlow,
  isNearOrExpired,
  orderReleasedForPutaway,
  parseGs1,
  receivingQualityGateStatusLabel,
  RECEIVING_QUALITY_GATE_STATUS,
  type ExpiryTone,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvCell,
  NvListRow,
  NvMobileButton,
  NvMobileDatePicker,
  NvMobileResult,
  NvMobileTag,
  NvNoticeBar,
  NvScanBar,
} from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '收货入库',
  },
})

const router = useRouter()
const { filters, orders, pending, error, refresh, completeInbound, completePending } =
  useWmsInbound()

// 收货质检门禁（#705）：行级投影，挂列表状态标 + 明细抽屉行 + 上架门禁。
const { linesByOrderId, refresh: refreshGates } = useWmsReceivingQualityGates()

// 选中的收货单号 + GUID（GUID 仅用于 complete 调用与 :key，绝不展示）。
const selectedOrderId = ref('')
const selectedOrderNo = ref('')
const sheetOpen = ref(false)
const completed = ref(false)

// 每次用户发起操作（点单开抽屉）生成一次稳定幂等键，跨重试复用以防丢响应重复入库；
// 选新单/继续后再点单才换新键。绝不在重试时重新生成。
const operationKey = ref('')

// inboundReceiveFlow 驱动进度：selectOrder→complete。
const flowCtx = computed(() => ({
  orderId: selectedOrderId.value || undefined,
  completed: completed.value,
}))
const flowStep = computed(() => inboundReceiveFlow.currentStep(flowCtx.value).id)

// 抽屉或结果展示时停止外层扫码焦点抢夺，避免破坏浮层 focus-trap。
const scanActive = computed(() => !sheetOpen.value && !completed.value)

const submitError = ref('')
const gs1Notice = ref('')

// 空态仅在「无待收货单据且无加载/错误」时出现，避免与错误/加载态打架。
const showEmpty = computed(() => !pending.value && !error.value && orders.value.length === 0)

// 收货扫码收集的效期，按收货行 id 暂存（本地作业提示，效期落库缺后端端点，见 #813 后续 issue）。
const capturedExpiry = ref<Record<string, string>>({})

// ---- 质检门禁 / 效期 视图辅助 ----
type TagVariant = 'default' | 'brand' | 'success' | 'warning' | 'danger'

const GATE_VARIANT: Record<string, TagVariant> = {
  [RECEIVING_QUALITY_GATE_STATUS.pending]: 'warning',
  [RECEIVING_QUALITY_GATE_STATUS.passed]: 'success',
  [RECEIVING_QUALITY_GATE_STATUS.conditionalRelease]: 'brand',
  [RECEIVING_QUALITY_GATE_STATUS.rejected]: 'danger',
  [RECEIVING_QUALITY_GATE_STATUS.notRequired]: 'default',
}
function gateVariant(status: string | null | undefined): TagVariant {
  return (status && GATE_VARIANT[status.toLowerCase()]) || 'warning'
}
const gateLabel = receivingQualityGateStatusLabel

const EXPIRY_VARIANT: Record<ExpiryTone, TagVariant> = {
  fresh: 'success',
  near: 'warning',
  critical: 'danger',
  expired: 'danger',
}

function lineExpiry(line: ReceivingQualityGateLine): string {
  return line.inboundOrderLineId ? (capturedExpiry.value[line.inboundOrderLineId] ?? '') : ''
}
function lineExpiryTone(line: ReceivingQualityGateLine): ExpiryTone | null {
  const d = lineExpiry(line)
  return d ? expiryToneFromDate(d) : null
}

function orderLines(orderId: string | undefined): ReceivingQualityGateLine[] {
  return (orderId && linesByOrderId.value.get(orderId)) || []
}
function orderGateStatus(orderId: string | undefined): string {
  return aggregateReceivingGateStatus(orderLines(orderId).map((l) => l.qualityGateStatus))
}
function orderCanPutaway(orderId: string | undefined): boolean {
  return orderReleasedForPutaway(orderLines(orderId).map((l) => l.qualityGateStatus))
}

const selectedLines = computed(() => orderLines(selectedOrderId.value))
// 抽屉内任一行临期/过期 → 黄色提示。
const hasNearExpiry = computed(() =>
  selectedLines.value.some((l) => isNearOrExpired(lineExpiryTone(l))),
)
const selectedCanPutaway = computed(() => orderCanPutaway(selectedOrderId.value))
const selectedNeedsQuality = computed(
  () => selectedLines.value.length > 0 && !selectedCanPutaway.value,
)

function onScan(value: string) {
  filters.keyword = value
}

function selectOrder(inboundOrderId: string | undefined, inboundOrderNo: string | undefined) {
  if (!inboundOrderId) return
  selectedOrderId.value = inboundOrderId
  selectedOrderNo.value = inboundOrderNo ?? ''
  // 新操作开始：换一把新幂等键。
  operationKey.value = makeIdempotencyKey()
  submitError.value = ''
  gs1Notice.value = ''
  sheetOpen.value = true
}

function closeSheet() {
  sheetOpen.value = false
}

// 扫 GS1 批次码：解析批号+效期，按批号匹配收货行写入效期（单行时兜底落到该行）。
function onGs1Scan(value: string) {
  gs1Notice.value = ''
  const parsed = parseGs1(value)
  if (!parsed || (!parsed.lotNo && !parsed.expiryDate)) {
    gs1Notice.value = '未识别到 GS1 批次码，请核对或手动录入效期'
    return
  }
  const lines = selectedLines.value
  let target = parsed.lotNo
    ? lines.find((l) => (l.lotNo ?? '').toLowerCase() === parsed.lotNo!.toLowerCase())
    : undefined
  if (!target && lines.length === 1) target = lines[0]
  if (!target?.inboundOrderLineId) {
    gs1Notice.value = parsed.lotNo
      ? `未匹配到批号 ${parsed.lotNo} 的收货行`
      : '未匹配到收货行，请手动选择'
    return
  }
  if (parsed.expiryDate) {
    capturedExpiry.value = {
      ...capturedExpiry.value,
      [target.inboundOrderLineId]: parsed.expiryDate,
    }
  } else {
    gs1Notice.value = `批号 ${parsed.lotNo} 已匹配，但码内无效期，请手动录入`
  }
}

// 手输兜底：日期滚轮录入效期。
const expiryPickerOpen = ref(false)
const expiryPickerLineId = ref('')
const expiryPickerValue = computed<string>({
  get: () =>
    expiryPickerLineId.value ? (capturedExpiry.value[expiryPickerLineId.value] ?? '') : '',
  set: (v) => {
    if (!expiryPickerLineId.value) return
    capturedExpiry.value = { ...capturedExpiry.value, [expiryPickerLineId.value]: v }
  },
})
function openExpiryPicker(line: ReceivingQualityGateLine) {
  if (!line.inboundOrderLineId) return
  expiryPickerLineId.value = line.inboundOrderLineId
  expiryPickerOpen.value = true
}

async function confirmComplete() {
  // 防重：幂等键已由组合式注入，但 UI 仍守一道——pending 中直接早退。
  if (completePending.value) return
  submitError.value = ''
  try {
    // 重试复用同一 operationKey（不重新生成），#188 客户端去重可识别为同一操作。
    await completeInbound(selectedOrderId.value, operationKey.value)
    // 成功后立刻关抽屉并切到结果态，重复点击无法再触发。
    sheetOpen.value = false
    completed.value = true
    void refreshGates()
  } catch (e) {
    submitError.value = e instanceof Error ? e.message : '完成收货入库失败'
  }
}

function resetFlow() {
  completed.value = false
  selectedOrderId.value = ''
  selectedOrderNo.value = ''
  // 清空操作键：下次点单会铸新键，保证新操作 ≠ 旧键。
  operationKey.value = ''
  submitError.value = ''
  gs1Notice.value = ''
}

function backToList() {
  resetFlow()
}

function goHome() {
  router.push('/').catch(() => {})
}

function goPutaway() {
  router.push('/wms/putaway').catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">收货入库</h1>
      </div>
    </template>

    <!-- 成功结果态 -->
    <NvMobileResult
      v-if="completed"
      status="success"
      title="入库完成，待质检"
      :description="selectedOrderNo ? `收货单 ${selectedOrderNo}` : undefined"
    >
      <template #actions>
        <NvMobileButton block variant="primary" @click="backToList">继续</NvMobileButton>
        <NvMobileButton block variant="outline" @click="goHome">返回</NvMobileButton>
      </template>
    </NvMobileResult>

    <div v-else class="space-y-4 p-4">
      <NvScanBar placeholder="扫描收货单号" :active="scanActive" @scan="onScan" />

      <RetryableListError
        v-if="error"
        :error="error"
        :pending="pending"
        fallback="单据加载失败，请下拉重试或检查网络。"
        test-id="error-banner"
        @retry="() => refresh()"
      />

      <div
        v-if="showEmpty"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无待收货单据
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <NvListRow
          v-for="order in orders"
          :key="order.inboundOrderId"
          :title="order.inboundOrderNo ?? ''"
          :subtitle="inboundOrderStatusLabel(order.status)"
          @select="selectOrder(order.inboundOrderId, order.inboundOrderNo)"
        >
          <template v-if="orderGateStatus(order.inboundOrderId)" #trailing>
            <NvMobileTag :variant="gateVariant(orderGateStatus(order.inboundOrderId))" size="sm">
              {{ gateLabel(orderGateStatus(order.inboundOrderId)) }}
            </NvMobileTag>
          </template>
        </NvListRow>
      </div>
    </div>

    <!-- 完成入库确认抽屉 -->
    <NvBottomSheet :open="sheetOpen" title="完成收货入库" @update:open="(v) => (sheetOpen = v)">
      <div class="space-y-4">
        <p v-if="selectedOrderNo" class="text-sm text-muted-foreground">
          收货单 {{ selectedOrderNo }}
        </p>

        <!-- 行级明细：批号 + 质检门禁 + 效期三色 -->
        <div v-if="selectedLines.length" class="overflow-hidden rounded-lg border border-border">
          <NvCell
            v-for="line in selectedLines"
            :key="line.inboundOrderLineId"
            :title="`${line.skuCode ?? ''} ×${line.receivedQuantity ?? 0}`"
            :note="`批号 ${line.lotNo || '—'}`"
            data-line
          >
            <template #value>
              <div class="flex flex-wrap items-center justify-end gap-1.5">
                <NvMobileTag :variant="gateVariant(line.qualityGateStatus)" size="sm">
                  {{ gateLabel(line.qualityGateStatus) }}
                </NvMobileTag>
                <NvMobileTag
                  v-if="lineExpiryTone(line)"
                  :variant="EXPIRY_VARIANT[lineExpiryTone(line)!]"
                  size="sm"
                  data-expiry-tag
                >
                  {{ lineExpiry(line) }}·{{ expiryToneLabel(lineExpiryTone(line)) }}
                </NvMobileTag>
                <button
                  type="button"
                  data-expiry-input
                  class="text-xs font-medium text-brand-strong"
                  @click="openExpiryPicker(line)"
                >
                  {{ lineExpiry(line) ? '改效期' : '录效期' }}
                </button>
              </div>
            </template>
          </NvCell>
        </div>

        <!-- GS1 扫码带出批号/效期 -->
        <NvScanBar
          v-if="selectedLines.length"
          placeholder="扫描 GS1 批次码带出效期"
          :active="sheetOpen && !completed"
          @scan="onGs1Scan"
        />
        <p v-if="gs1Notice" class="text-sm text-warning-strong" data-gs1-notice>{{ gs1Notice }}</p>

        <!-- 临期黄色提示 -->
        <NvNoticeBar v-if="hasNearExpiry" tone="warning" data-near-expiry-notice>
          存在临期批次，请核对效期后再入库
        </NvNoticeBar>

        <!-- 上架门禁：待检/不合格单据不出现上架引导 -->
        <NvNoticeBar v-if="selectedNeedsQuality" tone="info" data-quality-gate-notice>
          该单待质检，合格后方可上架
        </NvNoticeBar>

        <p
          v-if="flowStep === 'complete' && !selectedLines.length"
          class="text-xs text-muted-foreground"
        >
          已选单，待完成入库
        </p>
        <p class="text-base text-foreground">确认完成收货入库？</p>

        <p v-if="submitError" class="text-sm text-destructive">{{ submitError }}</p>

        <div class="space-y-2 pt-2">
          <NvMobileButton
            block
            variant="primary"
            data-testid="confirm-complete"
            :disabled="completePending"
            @click="confirmComplete"
          >
            {{ completePending ? '提交中…' : '确认完成' }}
          </NvMobileButton>
          <NvMobileButton
            v-if="selectedCanPutaway"
            block
            variant="outline"
            data-testid="go-putaway"
            @click="goPutaway"
          >
            去上架
          </NvMobileButton>
          <NvMobileButton block variant="outline" @click="closeSheet">取消</NvMobileButton>
        </div>
      </div>
    </NvBottomSheet>

    <NvMobileDatePicker
      v-model:open="expiryPickerOpen"
      v-model="expiryPickerValue"
      title="选择效期"
    />
  </NvAppShellMobile>
</template>
