<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import { useBusinessMaintenance } from '@/composables/useBusinessMaintenance'
import { useNonIdempotentWriteResult } from '@/composables/useNonIdempotentWriteResult'
import {
  createMeasurementDraft,
  inspectionFlow,
  inspectionResultLabel,
  inspectionResultLabels,
  measurementRowsValid,
  toMeasurementPayload,
  type InspectCtx,
  type MeasurementDraftRow,
} from '@nerv-iip/business-core'
import { NvAppShellMobile, NvListRow, NvMobileResult, NvScanBar } from '@nerv-iip/ui-mobile'
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'

// 保养计划行（composable 的 plans 为 unknown[]，此处收窄为业务字段）。
interface PlanRow {
  planId?: string
  deviceAssetId?: string
  planCode?: string
  interval?: string
  startsOn?: string
}

// 点检记录行。
interface InspectionRow {
  inspectionId?: string
  planId?: string | null
  workOrderId?: string | null
  result?: string
  inspectedAtUtc?: string
  measurements?: InspectionMeasurementRow[] | null
}

interface InspectionMeasurementRow {
  characteristicCode?: string
  measuredValue?: number
  uomCode?: string
  lowerSpecLimit?: number | null
  upperSpecLimit?: number | null
  isWithinSpec?: boolean
}

interface MeasurementFormRow extends MeasurementDraftRow {
  id: number
}

definePage({
  meta: {
    requiresAuth: true,
    title: '点检',
  },
})

const router = useRouter()

const maintenance = useBusinessMaintenance()
const {
  plansPending,
  plansError,
  plansTotal,
  loadMorePlans,
  refreshPlans,
  recordInspection,
  recordPending,
  inspectionsPending,
  inspectionsError,
  refreshInspections,
} = maintenance

// plans/inspections 收窄为业务行类型。
const allPlans = computed<PlanRow[]>(() => maintenance.plans.value as PlanRow[])
const inspections = computed<InspectionRow[]>(
  () => maintenance.inspections.value as InspectionRow[],
)

// 扫码/手输关键字 → 对**已加载**的 plans 做客户端过滤（facade 无 keyword/device 查询参数）。
const scanKeyword = ref('')
const plans = computed<PlanRow[]>(() => {
  const kw = scanKeyword.value.trim().toLowerCase()
  if (!kw) return allPlans.value
  return allPlans.value.filter((p) => {
    const code = (p.planCode ?? '').toLowerCase()
    const device = (p.deviceAssetId ?? '').toLowerCase()
    return code.includes(kw) || device.includes(kw)
  })
})

// 已加载条数 / 服务端总数：facade 仅支持 org/env/skip/take，关键字命中第一页之外时
// 不能把"已加载页内未命中"当作"不存在"——还有更多页可加载（loadMorePlans）。
const loadedPlans = computed(() => allPlans.value.length)
const hasMorePlans = computed(() => loadedPlans.value < plansTotal.value)

// 点检表单 = inspectionFlow 的上下文（selectPlan → enterResult → record）。
const form = reactive<InspectCtx>({
  planId: '',
  result: '',
})

let nextMeasurementRowId = 1
const measurementRows = reactive<MeasurementFormRow[]>([createMeasurementRow()])

// 结果选项（中文经 inspectionResultLabel）。
const resultOptions = Object.keys(inspectionResultLabels)

const measurementsValid = computed(() => measurementRowsValid(measurementRows))

const measurementPayload = computed(() => toMeasurementPayload(measurementRows))

// 流程驱动校验：planId + result 必填，测量值行可选但一旦填写必须完整有效。
const valid = computed(
  () => inspectionFlow.progress(form).completed >= 2 && measurementsValid.value,
)

// 点检端点无服务端幂等键 → 写结果状态机由共享 composable 统一：结果不确定（超时/网络中断）
// 不给盲目重试、引导核实；离线（未发出）与确定业务失败可安全重试。
const { phase, errorTitle, errorDescription, canRetry, run, retry, verify, reset } =
  useNonIdempotentWriteResult({
    failureTitle: '点检记录失败',
    verifyListLabel: '近期点检记录',
    verifyVerb: '记录',
    onVerify: () => {
      void refreshInspections()
    },
  })

// ScanBar 在浮层（成功/失败 Result）展示时停止抢焦。
const scanActive = computed(() => phase.value === 'form')

function onScan(value: string) {
  // 扫设备码/计划号 → 客户端过滤已加载的保养计划列表。
  scanKeyword.value = value
}

function selectPlan(planId: string | undefined) {
  if (!planId) return
  form.planId = planId
  // 切换计划时清空已选结果，避免沿用上一计划的结果。
  form.result = ''
}

function chooseResult(value: string) {
  form.result = value
}

function createMeasurementRow(): MeasurementFormRow {
  return { id: nextMeasurementRowId++, ...createMeasurementDraft() }
}

function addMeasurementRow() {
  measurementRows.push(createMeasurementRow())
}

function removeMeasurementRow(rowId: number) {
  if (measurementRows.length === 1) {
    Object.assign(measurementRows[0], createMeasurementRow())
    return
  }
  const index = measurementRows.findIndex((row) => row.id === rowId)
  if (index >= 0) {
    measurementRows.splice(index, 1)
  }
}

async function submit() {
  if (!valid.value || recordPending.value) return
  await run(() =>
    recordInspection({
      planId: form.planId as string,
      result: form.result as string,
      ...(measurementPayload.value.length > 0 ? { measurements: measurementPayload.value } : {}),
    }),
  )
}

function resetForm() {
  // 成功后清空，避免重复记录相同点检（端点无服务端幂等）。
  form.planId = ''
  form.result = ''
  measurementRows.splice(0, measurementRows.length, createMeasurementRow())
  reset()
}

function goBack() {
  router.push('/').catch(() => {})
}

function planSubtitle(item: { deviceAssetId?: string; interval?: string }) {
  const parts: string[] = []
  if (item.deviceAssetId) parts.push(`设备 ${item.deviceAssetId}`)
  if (item.interval) parts.push(`周期 ${item.interval}`)
  return parts.join(' · ')
}

function inspectionTitle(item: { planId?: string | null; workOrderId?: string | null }) {
  if (item.planId) return `计划 ${item.planId}`
  if (item.workOrderId) return `工单 ${item.workOrderId}`
  return '点检记录'
}

function inspectionSubtitle(item: {
  result?: string
  inspectedAtUtc?: string
  measurements?: InspectionMeasurementRow[] | null
}) {
  const parts = [`结果 ${inspectionResultLabel(item.result)}`]
  const measurements = item.measurements ?? []
  if (measurements.length > 0) {
    const first = measurements[0]
    parts.push(
      `${first.characteristicCode ?? '测量值'} ${first.measuredValue ?? '-'} ${first.uomCode ?? ''}`.trim(),
    )
  }
  if (item.inspectedAtUtc) {
    parts.push(new Date(item.inspectedAtUtc).toLocaleString('zh-CN'))
  }
  return parts.join(' · ')
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">点检</h1>
      </div>
    </template>

    <!-- 成功 / 失败：离场态（清空表单，防重复记录） -->
    <NvMobileResult
      v-if="phase === 'success'"
      status="success"
      title="点检已记录"
      description="点检结果已记录。"
    >
      <template #actions>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="resetForm"
        >
          继续点检
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goBack"
        >
          返回
        </button>
      </template>
    </NvMobileResult>

    <NvMobileResult
      v-else-if="phase === 'error'"
      status="error"
      :title="errorTitle"
      :description="errorDescription"
    >
      <template #actions>
        <!-- 可安全重试（离线未发出 / 服务端已响应）→ 重试；结果不确定 → 只给核实入口。 -->
        <button
          v-if="canRetry"
          type="button"
          data-testid="retry"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="retry"
        >
          重试
        </button>
        <button
          v-else
          type="button"
          data-testid="verify-list"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="verify"
        >
          查看点检记录
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goBack"
        >
          返回
        </button>
      </template>
    </NvMobileResult>

    <div v-else class="space-y-6 p-4">
      <!-- 新建点检 -->
      <section class="space-y-3">
        <h2 class="text-sm font-medium text-muted-foreground">新建点检</h2>

        <NvScanBar placeholder="扫描设备码或计划号" :active="scanActive" @scan="onScan" />

        <!-- 步骤 1：选择保养计划 -->
        <div class="space-y-2">
          <p class="text-sm text-foreground">选择保养计划</p>

          <RetryableListError
            v-if="plansError"
            :error="plansError"
            :pending="plansPending"
            fallback="保养计划加载失败，请稍后重试。"
            test-id="plans-error"
            @retry="() => refreshPlans()"
          />

          <div v-else-if="plansPending" class="px-4 py-6 text-center text-sm text-muted-foreground">
            加载中…
          </div>

          <div
            v-else-if="allPlans.length === 0"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            暂无保养计划
          </div>

          <!-- 已加载页内未命中，但服务端仍有更多页：诚实提示 + 加载更多，不做死路。 -->
          <div
            v-else-if="plans.length === 0 && hasMorePlans"
            data-testid="plans-partial-no-match"
            class="space-y-3 rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            <p>在已加载的 {{ loadedPlans }} 条计划中未匹配（共 {{ plansTotal }} 条）。</p>
            <button
              data-testid="load-more-plans"
              type="button"
              :disabled="plansPending"
              class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground disabled:opacity-60"
              @click="loadMorePlans"
            >
              加载更多
            </button>
          </div>

          <!-- 全部计划已加载且仍无命中：确定性"未找到"。 -->
          <div
            v-else-if="plans.length === 0"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            未找到匹配的保养计划
          </div>

          <div v-else class="overflow-hidden rounded-lg border border-border">
            <NvListRow
              v-for="item in plans"
              :key="item.planId"
              data-testid="plan-option"
              :title="item.planCode ?? '未命名计划'"
              :subtitle="planSubtitle(item)"
              :class="form.planId === item.planId ? 'bg-brand/10' : undefined"
              @select="selectPlan(item.planId)"
            />
          </div>
        </div>

        <!-- 步骤 2：选择结果（仅在已选计划后出现） -->
        <div v-if="form.planId" class="space-y-2">
          <p class="text-sm text-foreground">点检结果</p>
          <div class="grid grid-cols-2 gap-3">
            <button
              v-for="r in resultOptions"
              :key="r"
              :data-testid="`result-${r}`"
              type="button"
              class="min-h-touch w-full rounded-lg border text-base font-medium"
              :class="
                form.result === r
                  ? 'border-brand bg-brand/10 text-foreground'
                  : 'border-border bg-card text-foreground'
              "
              @click="chooseResult(r)"
            >
              {{ inspectionResultLabel(r) }}
            </button>
          </div>
        </div>

        <div v-if="form.planId" class="space-y-2">
          <div class="flex items-center justify-between gap-3">
            <p class="text-sm text-foreground">测量值</p>
            <button
              type="button"
              data-testid="add-measurement"
              class="min-h-touch rounded-lg border border-border bg-card px-4 text-sm font-medium text-foreground"
              @click="addMeasurementRow"
            >
              添加
            </button>
          </div>

          <div
            v-for="row in measurementRows"
            :key="row.id"
            class="space-y-2 rounded-lg border border-border bg-card p-3"
          >
            <div class="grid grid-cols-1 gap-2 sm:grid-cols-3">
              <label class="space-y-1 text-xs text-muted-foreground">
                <span>特性</span>
                <input
                  v-model="row.characteristicCode"
                  data-testid="measurement-characteristic"
                  class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base text-foreground"
                  autocomplete="off"
                />
              </label>
              <label class="space-y-1 text-xs text-muted-foreground">
                <span>数值</span>
                <input
                  v-model="row.measuredValue"
                  data-testid="measurement-value"
                  class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base text-foreground"
                  type="number"
                  step="any"
                />
              </label>
              <label class="space-y-1 text-xs text-muted-foreground">
                <span>单位</span>
                <input
                  v-model="row.uomCode"
                  data-testid="measurement-uom"
                  class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base text-foreground"
                  autocomplete="off"
                />
              </label>
            </div>
            <div class="grid grid-cols-2 gap-2">
              <label class="space-y-1 text-xs text-muted-foreground">
                <span>下限</span>
                <input
                  v-model="row.lowerSpecLimit"
                  data-testid="measurement-lower"
                  class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base text-foreground"
                  type="number"
                  step="any"
                />
              </label>
              <label class="space-y-1 text-xs text-muted-foreground">
                <span>上限</span>
                <input
                  v-model="row.upperSpecLimit"
                  data-testid="measurement-upper"
                  class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base text-foreground"
                  type="number"
                  step="any"
                />
              </label>
            </div>
            <button
              type="button"
              data-testid="remove-measurement"
              class="min-h-touch w-full rounded-lg border border-border bg-background text-sm font-medium text-foreground"
              @click="removeMeasurementRow(row.id)"
            >
              移除
            </button>
          </div>

          <p
            v-if="!measurementsValid"
            data-testid="measurement-error"
            class="rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive"
          >
            请完整填写测量值，且下限不能大于上限。
          </p>
        </div>

        <!-- 步骤 3：提交 -->
        <button
          data-testid="submit"
          type="button"
          :disabled="!valid || recordPending"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
          @click="submit"
        >
          {{ recordPending ? '记录中…' : '提交点检' }}
        </button>
      </section>

      <!-- 近期点检记录 -->
      <section class="space-y-2">
        <h2 class="text-sm font-medium text-muted-foreground">近期点检记录</h2>

        <RetryableListError
          v-if="inspectionsError"
          :error="inspectionsError"
          :pending="inspectionsPending"
          fallback="点检记录加载失败，请稍后重试。"
          test-id="inspections-error"
          @retry="() => refreshInspections()"
        />

        <div
          v-else-if="inspectionsPending"
          class="px-4 py-6 text-center text-sm text-muted-foreground"
        >
          加载中…
        </div>

        <div
          v-else-if="inspections.length === 0"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          暂无点检记录
        </div>

        <div v-else class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="item in inspections"
            :key="item.inspectionId"
            :title="inspectionTitle(item)"
            :subtitle="inspectionSubtitle(item)"
            :interactive="false"
          />
        </div>
      </section>
    </div>
  </NvAppShellMobile>
</template>
