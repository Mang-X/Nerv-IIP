<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import {
  useWmsInbound,
  useWmsReceivingLines,
  type InboundLineCapture,
  type ReceivingQualityGateLine,
} from '@/composables/useBusinessWms'
import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import {
  expiryToneFromDate,
  expiryToneLabel,
  inboundOrderStatusLabel,
  isNearOrExpired,
  parseGs1,
  receivingQualityGateStatusLabel,
  RECEIVING_QUALITY_GATE_STATUS,
  type ExpiryTone,
  type ReceivingQualityGateStatus,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvMobileButton,
  NvMobileDatePicker,
  NvMobileInput,
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

type InboundOrder = BusinessConsoleWmsInboundOrderItem

const router = useRouter()
const { filters, orders, pending, error, refresh, completeInbound, completePending } =
  useWmsInbound()

// 选中的收货单（单据级质检状态/上架放行来自列表项派生字段，避免按分页门禁行跨页聚合）。
const selectedOrder = ref<InboundOrder | null>(null)
const selectedOrderId = computed(() => selectedOrder.value?.inboundOrderId ?? '')
const selectedOrderNo = computed(() => selectedOrder.value?.inboundOrderNo ?? '')
const sheetOpen = ref(false)
const completed = ref(false)

// 打开某单明细时按单查完整收货行（含免检行）；加载/失败/不完整态显式暴露，未确认前禁止提交。
const {
  lines: selectedLines,
  complete: linesComplete,
  pending: linesPending,
  error: linesError,
  refresh: refreshLines,
} = useWmsReceivingLines(selectedOrderNo)

// 收货现场「当前作业行」：多行单先选行再扫码，GS1 采集落到选中行（新收货行上尚无
// 批号时无法按 lotNo 匹配）。单行单自动选中；未选中时扫码回退 lotNo 匹配/单行兜底。
const activeLineId = ref('')

// 每次用户发起操作（点单开抽屉）生成一次稳定幂等键，跨重试复用以防丢响应重复入库；
// 选新单/继续后再点单才换新键。绝不在重试时重新生成。
const operationKey = ref('')

// 抽屉或结果展示时停止外层扫码焦点抢夺，避免破坏浮层 focus-trap。
const scanActive = computed(() => !sheetOpen.value && !completed.value)

const submitError = ref('')
const gs1Notice = ref('')

// 空态仅在「无待收货单据且无加载/错误」时出现，避免与错误/加载态打架。
const showEmpty = computed(() => !pending.value && !error.value && orders.value.length === 0)

// 收货现场按行采集的批号/效期（GS1 扫码、批号手输或日期滚轮），随 completeInbound 落库（#935 闭环）。
// 采集值覆盖后端已有值；未采集则展示/提交后端投影的既有值。
interface LineCapture {
  lotNo?: string
  productionDate?: string
  expiryDate?: string
}
const capturedByLine = ref<Record<string, LineCapture>>({})
function captureLine(lineId: string, patch: LineCapture) {
  capturedByLine.value = {
    ...capturedByLine.value,
    [lineId]: { ...capturedByLine.value[lineId], ...patch },
  }
}

// ---- 质检门禁 / 效期 视图辅助 ----
type TagVariant = 'default' | 'brand' | 'success' | 'warning' | 'danger'

const GATE_VARIANT: Record<ReceivingQualityGateStatus, TagVariant> = {
  [RECEIVING_QUALITY_GATE_STATUS.pending]: 'warning',
  [RECEIVING_QUALITY_GATE_STATUS.passed]: 'success',
  [RECEIVING_QUALITY_GATE_STATUS.conditionalRelease]: 'brand',
  [RECEIVING_QUALITY_GATE_STATUS.rejected]: 'danger',
  [RECEIVING_QUALITY_GATE_STATUS.notRequired]: 'default',
}
// 已知状态映射到语义色；未知/空码用中性 default（不静默当成待检的琥珀色误导操作者）。
function gateVariant(status: string | null | undefined): TagVariant {
  const key = status?.toLowerCase() as ReceivingQualityGateStatus | undefined
  return (key && GATE_VARIANT[key]) ?? 'default'
}
const gateLabel = receivingQualityGateStatusLabel

const EXPIRY_VARIANT: Record<ExpiryTone, TagVariant> = {
  fresh: 'success',
  near: 'warning',
  critical: 'danger',
  expired: 'danger',
}

// 采集值优先，否则回退后端投影的既有效期/批号（#935 收货行已带 expiryDate/productionDate）。
function lineCapture(line: ReceivingQualityGateLine): LineCapture {
  return (line.inboundOrderLineId && capturedByLine.value[line.inboundOrderLineId]) || {}
}
function lineExpiry(line: ReceivingQualityGateLine): string {
  return lineCapture(line).expiryDate ?? line.expiryDate ?? ''
}
function lineBatch(line: ReceivingQualityGateLine): string {
  return lineCapture(line).lotNo ?? line.lotNo ?? ''
}
function lineExpiryTone(line: ReceivingQualityGateLine): ExpiryTone | null {
  const d = lineExpiry(line)
  return d ? expiryToneFromDate(d) : null
}

// 抽屉内任一行临期/过期 → 黄色提示。
const hasNearExpiry = computed(() =>
  selectedLines.value.some((l) => isNearOrExpired(lineExpiryTone(l))),
)
// 上架门禁：单据级派生（后端聚合含免检行）。待检/不合格 → 不出现上架引导。
const selectedCanPutaway = computed(() => selectedOrder.value?.isReleasedForPutaway === true)
const selectedNeedsQuality = computed(
  () => Boolean(selectedOrder.value?.qualityGateStatus) && !selectedCanPutaway.value,
)
// 行数据未加载/失败/不完整前不得提交（否则会以空或被截断的 lines 完成收货，静默漏采集）。
const submitDisabled = computed(
  () =>
    completePending.value ||
    linesPending.value ||
    Boolean(linesError.value) ||
    !linesComplete.value,
)

// 当前作业行：显式选中优先，否则单行单自动落到唯一行。
const activeLine = computed<ReceivingQualityGateLine | undefined>(() => {
  const explicit = selectedLines.value.find((l) => l.inboundOrderLineId === activeLineId.value)
  if (explicit) return explicit
  return selectedLines.value.length === 1 ? selectedLines.value[0] : undefined
})
function selectLine(line: ReceivingQualityGateLine) {
  activeLineId.value = line.inboundOrderLineId ?? ''
}

function onScan(value: string) {
  // 外层扫单号：直接按单号过滤列表（无独立 keyword 输入，扫码即筛）。
  filters.keyword = value
}

function selectOrder(order: InboundOrder) {
  if (!order.inboundOrderId) return
  selectedOrder.value = order
  // 新操作开始：换一把新幂等键。
  operationKey.value = makeIdempotencyKey()
  submitError.value = ''
  gs1Notice.value = ''
  sheetOpen.value = true
}

function closeSheet() {
  sheetOpen.value = false
}

// 扫 GS1 批次码：解析批号/效期/生产日期采集到目标行。目标优先级：
// 当前作业行（选中/单行）> 按扫出 lotNo 匹配已有批号的行。新收货行上尚无批号时
// 靠先选行绑定，满足多行单的「扫码自动带出批号效期」。采集随 completeInbound 落库。
function onGs1Scan(value: string) {
  gs1Notice.value = ''
  const parsed = parseGs1(value)
  if (!parsed || (!parsed.lotNo && !parsed.expiryDate)) {
    gs1Notice.value = '未识别到 GS1 批次码，请核对或手动录入'
    return
  }
  const lines = selectedLines.value
  let target = activeLine.value
  if (!target && parsed.lotNo) {
    target = lines.find((l) => lineBatch(l).toLowerCase() === parsed.lotNo!.toLowerCase())
  }
  if (!target?.inboundOrderLineId) {
    gs1Notice.value =
      lines.length > 1
        ? '多行单请先点选目标行再扫码'
        : parsed.lotNo
          ? `未匹配到批号 ${parsed.lotNo} 的收货行`
          : '未匹配到收货行，请手动选择'
    return
  }
  captureLine(target.inboundOrderLineId, {
    ...(parsed.lotNo ? { lotNo: parsed.lotNo } : {}),
    ...(parsed.productionDate ? { productionDate: parsed.productionDate } : {}),
    ...(parsed.expiryDate ? { expiryDate: parsed.expiryDate } : {}),
  })
  if (!parsed.expiryDate) {
    gs1Notice.value = `批号 ${parsed.lotNo} 已匹配，但码内无效期，请手动录入`
  }
}

// 逐行批号手输兜底（多行新收货行上无批号时定位采集）。
function onBatchInput(line: ReceivingQualityGateLine, value: string | number) {
  if (!line.inboundOrderLineId) return
  captureLine(line.inboundOrderLineId, { lotNo: String(value).trim() })
}

// 手输兜底：日期滚轮录入效期（初值取采集值或后端既有效期）。
const expiryPickerOpen = ref(false)
const expiryPickerLineId = ref('')
const expiryPickerValue = computed<string>({
  get: () => {
    const id = expiryPickerLineId.value
    if (!id) return ''
    const line = selectedLines.value.find((l) => l.inboundOrderLineId === id)
    return (line ? lineExpiry(line) : '') || ''
  },
  set: (v) => {
    if (!expiryPickerLineId.value) return
    captureLine(expiryPickerLineId.value, { expiryDate: v })
  },
})
function openExpiryPicker(line: ReceivingQualityGateLine) {
  if (!line.inboundOrderLineId) return
  expiryPickerLineId.value = line.inboundOrderLineId
  expiryPickerOpen.value = true
}

// 提交时把每行的有效批号/生产日期/效期（采集值优先，否则后端既有）打包落库。
// 仅提交至少带一个批次字段的行；均无则不带 lines（等价旧行为）。
function buildCaptureLines(): InboundLineCapture[] {
  const out: InboundLineCapture[] = []
  for (const line of selectedLines.value) {
    if (!line.lineNo) continue
    const cap = lineCapture(line)
    const lotNo = cap.lotNo ?? line.lotNo ?? undefined
    const productionDate = cap.productionDate ?? line.productionDate ?? undefined
    const expiryDate = cap.expiryDate ?? line.expiryDate ?? undefined
    if (!lotNo && !productionDate && !expiryDate) continue
    out.push({ lineNo: line.lineNo, lotNo, productionDate, expiryDate })
  }
  return out
}

async function confirmComplete() {
  // 防重 + #3：pending / 行数据未加载或失败时禁止提交（不以空 lines 完成丢采集）。
  if (submitDisabled.value) return
  submitError.value = ''
  try {
    // 重试复用同一 operationKey（不重新生成），#188 客户端去重可识别为同一操作。
    await completeInbound(selectedOrderId.value, operationKey.value, buildCaptureLines())
    // 成功后立刻关抽屉并切到结果态，重复点击无法再触发。
    sheetOpen.value = false
    completed.value = true
    void refresh()
  } catch (e) {
    submitError.value = e instanceof Error ? e.message : '完成收货入库失败'
  }
}

function resetFlow() {
  completed.value = false
  selectedOrder.value = null
  // 清空操作键：下次点单会铸新键，保证新操作 ≠ 旧键。
  operationKey.value = ''
  submitError.value = ''
  gs1Notice.value = ''
  capturedByLine.value = {}
  activeLineId.value = ''
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
        <div
          v-for="order in orders"
          :key="order.inboundOrderId"
          data-row
          role="button"
          tabindex="0"
          class="min-h-row flex w-full items-center gap-3 border-b border-border bg-card px-4 py-3 text-left last:border-b-0 active:bg-accent"
          @click="selectOrder(order)"
          @keydown.enter="selectOrder(order)"
        >
          <div class="min-w-0 flex-1">
            <div class="truncate text-base font-medium text-foreground">
              {{ order.inboundOrderNo ?? '' }}
            </div>
            <div class="truncate text-sm text-muted-foreground">
              {{ inboundOrderStatusLabel(order.status) }}
            </div>
          </div>
          <NvMobileTag
            v-if="order.qualityGateStatus"
            :variant="gateVariant(order.qualityGateStatus)"
            size="sm"
          >
            {{ gateLabel(order.qualityGateStatus) }}
          </NvMobileTag>
        </div>
      </div>
    </div>

    <!-- 完成入库确认抽屉 -->
    <NvBottomSheet :open="sheetOpen" title="完成收货入库" @update:open="(v) => (sheetOpen = v)">
      <div class="space-y-4">
        <p v-if="selectedOrderNo" class="text-sm text-muted-foreground">
          收货单 {{ selectedOrderNo }}
        </p>

        <!-- 行数据加载/失败态：未确认前禁止提交 -->
        <p v-if="linesPending" class="text-sm text-muted-foreground" data-lines-loading>
          正在加载收货明细…
        </p>
        <RetryableListError
          v-else-if="linesError"
          :error="linesError"
          :pending="linesPending"
          fallback="收货明细加载失败，请重试。"
          test-id="lines-error"
          @retry="() => refreshLines()"
        />

        <!-- 行级明细：批号（可手输）+ 质检门禁 + 效期三色 -->
        <div
          v-else-if="selectedLines.length"
          class="divide-y divide-border overflow-hidden rounded-lg border border-border"
        >
          <div
            v-for="line in selectedLines"
            :key="line.inboundOrderLineId"
            data-line
            role="button"
            tabindex="0"
            :data-active="activeLine?.inboundOrderLineId === line.inboundOrderLineId || undefined"
            class="cursor-pointer px-4 py-3 data-[active]:bg-brand/8"
            @click="selectLine(line)"
            @keydown.enter="selectLine(line)"
          >
            <div class="flex items-center justify-between gap-2">
              <div class="flex min-w-0 items-center gap-1.5 text-[15px] text-foreground">
                {{ line.skuCode ?? '' }} ×{{ line.receivedQuantity ?? 0 }}
                <NvMobileTag
                  v-if="
                    selectedLines.length > 1 &&
                    activeLine?.inboundOrderLineId === line.inboundOrderLineId
                  "
                  variant="brand"
                  size="sm"
                  data-active-line
                >
                  当前行
                </NvMobileTag>
              </div>
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
              </div>
            </div>
            <div class="mt-2 flex items-center gap-2">
              <NvMobileInput
                :model-value="lineBatch(line)"
                placeholder="批号"
                class="h-9 flex-1"
                data-batch-input
                @update:model-value="(v) => onBatchInput(line, v)"
              />
              <NvMobileButton
                size="sm"
                variant="outline"
                data-expiry-input
                @click="openExpiryPicker(line)"
              >
                {{ lineExpiry(line) ? '改效期' : '录效期' }}
              </NvMobileButton>
            </div>
          </div>
        </div>

        <!-- 明细超量截断（未证明完整）：fail closed，禁止完成，避免静默漏采集。 -->
        <NvNoticeBar
          v-if="!linesPending && !linesError && selectedLines.length && !linesComplete"
          tone="danger"
          data-lines-incomplete
        >
          收货明细过多未取全，暂不能完成入库，请联系管理员
        </NvNoticeBar>

        <!-- 多行单：先点选目标行再扫码带出批号/效期。 -->
        <p
          v-if="!linesPending && !linesError && selectedLines.length > 1"
          class="text-xs text-muted-foreground"
          data-multiline-hint
        >
          多行单：先点选目标行，再扫码带出批号/效期
        </p>

        <!-- GS1 扫码带出批号/效期。本页有两个 NvScanBar（外层单号 + 本抽屉 GS1），
             靠 active 严格互斥：抽屉开时外层 active=false 让出 document 捕获，仅本条 active，
             不触发 ScanBar「单 ScanBar 页面」的双写仲裁限制。 -->
        <NvScanBar
          v-if="!linesPending && !linesError && selectedLines.length"
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

        <p class="text-base text-foreground">确认完成收货入库？</p>

        <p v-if="submitError" class="text-sm text-destructive">{{ submitError }}</p>

        <div class="space-y-2 pt-2">
          <NvMobileButton
            block
            variant="primary"
            data-testid="confirm-complete"
            :disabled="submitDisabled"
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
