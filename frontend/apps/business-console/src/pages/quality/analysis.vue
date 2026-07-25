<script setup lang="ts">
import type { NvDataTableColumn, NvMetricFacet, NvMetricSegment } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import QualityParetoPanel from '@/components/quality/QualityParetoPanel.vue'
import QualitySpcCharts from '@/components/quality/QualitySpcCharts.vue'
import { useQualityNcrs } from '@/composables/useBusinessQuality'
import {
  buildQualityAnalysisSummary,
  formatQualityQuantity,
  spcViolationTargetId,
  useQualitySpcAnalysis,
  type QualityAnalysisBucket,
  type QualitySpcViolation,
} from '@/composables/useBusinessQualityAnalysis'
import { friendlyErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvMetricCard,
  NvMetricRing,
  NvPageHeader,
  NvToolbar,
} from '@nerv-iip/ui'
import {
  BellRingIcon,
  ClipboardCheckIcon,
  FileCheck2Icon,
  FileTextIcon,
  LineChartIcon,
  RefreshCwIcon,
  ShieldAlertIcon,
} from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '质量分析',
    requiredPermissions: ['business.quality.ncr.read'],
  },
})

const router = useRouter()
const { filters, ncrs, ncrsError, ncrsPending, ncrsTotal, refreshNcrs } = useQualityNcrs()
const spc = useQualitySpcAnalysis()

const summary = computed(() => buildQualityAnalysisSummary(ncrs.value, ncrsTotal.value))
const listErrorMessage = computed(() => formatError(ncrsError.value))
const spcErrorMessage = computed(() => formatError(spc.spcError.value))
const trendGapText =
  '本页按当前不合格品记录窗口分析；按时间、工位、设备和班次的全量趋势请到质量报表跟进。'

/**
 * 不合格品的构成关系：各处置状态之和 = 当前窗口记录数，所以用环形卡。
 * 后端可能返回窗口内的其它状态，差额补一段中性「其它状态」，
 * 否则环形的百分比会按一个用户看不见的分母绘制。
 */
const ncrStatusSegments = computed<NvMetricSegment[]>(() => {
  const { openNcrCount, dispositionedNcrCount, closedNcrCount, sampledNcrCount } = summary.value
  const segments: NvMetricSegment[] = [
    { key: 'open', label: '尚未处置', value: openNcrCount, tone: 'danger' },
    {
      key: 'dispositioned',
      label: '已给出处置结论',
      value: dispositionedNcrCount,
      tone: 'warning',
    },
    { key: 'closed', label: '已关闭', value: closedNcrCount, tone: 'success' },
  ]
  const rest = sampledNcrCount - openNcrCount - dispositionedNcrCount - closedNcrCount
  return rest > 0
    ? [...segments, { key: 'other', label: '其它状态', value: rest, tone: 'neutral' }]
    : segments
})
/** 缺陷原因 Top 6 作为迷你柱，回答「主要坏在哪一项」。 */
const defectBars = computed(() => {
  const top = summary.value.defectPareto.slice(0, 6)
  return {
    series: top.map((bucket) => bucket.defectQuantity),
    labels: top.map((bucket) => bucket.label),
    leader: top[0],
  }
})
// 上方三个输入框带的是示例占位（SKU-001 / DIAMETER / WC-01），灰字看着像"已填"，
// 于是这张卡说「未填」就显得自相矛盾。改说「待选择」，把"还没给条件"讲成一句人话。
const spcScopeFacets = computed<NvMetricFacet[]>(() => [
  { key: 'sku', label: '物料', value: spc.filters.skuCode.trim() || '待选择' },
  {
    key: 'characteristic',
    label: '特性',
    value: spc.filters.characteristicCode.trim() || '待选择',
  },
  { key: 'workCenter', label: '工作中心', value: spc.filters.workCenterId.trim() || '待选择' },
])
const spcSubgroupCount = computed(() => spc.spcChart.value?.subgroups?.length ?? 0)
const spcXbarSeries = computed(() =>
  (spc.spcChart.value?.subgroups ?? [])
    .map((subgroup) => subgroup.xbar)
    .filter((value): value is number => typeof value === 'number' && Number.isFinite(value)),
)
const spcXbarLabels = computed(() =>
  (spc.spcChart.value?.subgroups ?? [])
    .filter((subgroup) => typeof subgroup.xbar === 'number' && Number.isFinite(subgroup.xbar))
    .map((subgroup) => `子组 ${subgroup.index ?? 0}`),
)
const spcCapabilityFacets = computed<NvMetricFacet[]>(() => [
  { key: 'cp', label: 'Cp', value: formatMetric(spc.capability.value?.cp) },
  {
    key: 'cpk',
    label: 'Cpk',
    value: formatMetric(spc.capability.value?.cpk),
    tone: isBelowCapabilityFloor(spc.capability.value?.cpk) ? 'danger' : undefined,
  },
  { key: 'samples', label: '实测值', value: spc.capability.value?.sampleCount ?? 0 },
])
const spcControlLimitHint = computed(() => {
  if (spc.spcWarmup.value) {
    return '实测值不足一个完整子组'
  }

  return spc.spcChart.value?.controlLimits?.locked ? '控制限已锁定' : '自动计算控制限'
})
const spcViolationEmptyMessage = computed(() =>
  spc.spcWarmup.value
    ? '实测值不足一个完整子组，暂不生成控制限和判异。'
    : '当前 SPC 范围没有判异。',
)

const dimensionColumns: NvDataTableColumn<QualityAnalysisBucket>[] = [
  { key: 'label', header: '对象', cellClass: 'font-medium' },
  { key: 'count', header: 'NCR 数', align: 'end', width: 'w-24' },
  { key: 'defectQuantity', header: '缺陷数量', align: 'end', width: 'w-28' },
]
const spcViolationColumns: NvDataTableColumn<QualitySpcViolation>[] = [
  { key: 'rule', header: '判异规则', cellClass: 'font-medium' },
  { key: 'startSubgroupIndex', header: '开始子组', align: 'end', width: 'w-24' },
  { key: 'endSubgroupIndex', header: '结束子组', align: 'end', width: 'w-24' },
  { key: 'message', header: '说明' },
]

function formatError(error: unknown) {
  return error ? friendlyErrorMessage(error, '质量分析加载失败，请稍后重试。') : ''
}
function formatMetric(value: number | null | undefined) {
  return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(2) : '-'
}
/** 制造业通行的过程能力底线：Cpk < 1.33 需要关注。 */
function isBelowCapabilityFloor(value: number | null | undefined) {
  return typeof value === 'number' && Number.isFinite(value) && value < 1.33
}
function spcViolationKey(row: QualitySpcViolation) {
  return spcViolationTargetId(row, spc.spcViolations.value.indexOf(row))
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="质量分析"
      :breadcrumbs="[{ label: '质量管理' }]"
      :count="listErrorMessage ? 'NCR 数据加载失败' : `${summary.totalNcrCount} 条 NCR`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/quality/ncrs"><ShieldAlertIcon aria-hidden="true" />不合格品</RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/approval"><FileCheck2Icon aria-hidden="true" />审批中心</RouterLink>
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="ncrsPending"
          @click="refreshNcrs"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p
      v-if="listErrorMessage"
      class="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive"
      role="alert"
    >
      NCR 数据加载失败：{{ listErrorMessage }}
    </p>

    <div v-else class="grid gap-4 lg:grid-cols-3">
      <NvMetricRing
        label="不合格品处置构成"
        :value="summary.sampledNcrCount"
        center-caption="条"
        :segments="ncrStatusSegments"
      />
      <NvMetricCard
        variant="bars"
        label="缺陷数量"
        :value="formatQualityQuantity(summary.totalDefectQuantity)"
        unit="件"
        :series="defectBars.series"
        :series-labels="defectBars.labels"
        series-unit="件"
        foot-start="缺陷原因前六位"
        :foot-end="defectBars.leader ? `最多：${defectBars.leader.label}` : ''"
      />
      <NvMetricCard
        variant="alert"
        label="尚未处置"
        :value="summary.openNcrCount"
        unit="条"
        :tone="summary.openNcrCount > 0 ? 'danger' : 'neutral'"
        :status="
          summary.openNcrCount > 0
            ? { label: '待处置', tone: 'danger' }
            : { label: '已清零', tone: 'success' }
        "
        :foot-start="summary.sampleNotice"
        :action="summary.openNcrCount > 0 ? { label: '去处置' } : undefined"
        @action="router.push({ path: '/quality/ncrs' })"
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <p class="max-w-3xl text-sm text-muted-foreground">{{ trendGapText }}</p>
      </template>
    </NvToolbar>

    <div class="grid gap-4">
      <NvToolbar :show-search="false">
        <template #filters>
          <div class="grid w-full gap-3 lg:grid-cols-[repeat(5,minmax(0,1fr))_auto]">
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              SKU
              <NvInput v-model="spc.filters.skuCode" placeholder="SKU-001" />
            </label>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              特性
              <NvInput v-model="spc.filters.characteristicCode" placeholder="DIAMETER" />
            </label>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              工作中心
              <NvInput v-model="spc.filters.workCenterId" placeholder="WC-01" />
            </label>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              子组
              <NvInput v-model="spc.filters.subgroupSize" type="number" min="2" max="10" />
            </label>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              点数
              <NvInput v-model="spc.filters.take" type="number" min="5" max="200" />
            </label>
            <NvButton
              class="self-end"
              size="sm"
              type="button"
              variant="outline"
              :disabled="!spc.spcReady.value || spc.spcPending.value"
              @click="spc.refreshSpc"
            >
              <LineChartIcon aria-hidden="true" />
              查询
            </NvButton>
          </div>
        </template>
      </NvToolbar>

      <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <NvMetricCard
          variant="facets"
          label="分析范围"
          :value="spc.spcReady.value ? spcSubgroupCount : '—'"
          :unit="spc.spcReady.value ? '个子组' : undefined"
          :facets="spcScopeFacets"
        />
        <NvMetricCard
          variant="sparkline"
          label="控制上限 UCL"
          :value="formatMetric(spc.spcChart.value?.controlLimits?.xbarUpperControlLimit)"
          :series="spcXbarSeries"
          :series-labels="spcXbarLabels"
          :foot-start="spcControlLimitHint"
          :foot-end="`中心线 ${formatMetric(spc.spcChart.value?.controlLimits?.centerLine)}`"
        />
        <NvMetricCard
          variant="facets"
          label="过程能力"
          :value="formatMetric(spc.capability.value?.cpk)"
          unit="Cpk"
          :facets="spcCapabilityFacets"
        />
        <NvMetricCard
          variant="alert"
          label="判异点"
          :value="spc.spcViolations.value.length"
          unit="处"
          :tone="spc.spcViolations.value.length > 0 ? 'danger' : 'neutral'"
          :status="
            spc.spcViolations.value.length > 0
              ? { label: '过程失控', tone: 'danger' }
              : { label: '受控', tone: 'success' }
          "
          foot-start="判异属于质量过程预警，不计入设备报警。"
        />
      </div>

      <QualitySpcCharts
        :chart="spc.spcChart.value"
        :pending="spc.spcPending.value"
        :warmup="spc.spcWarmup.value"
        :error-message="spcErrorMessage"
      />

      <NvDataTable
        :columns="spcViolationColumns"
        :rows="spc.spcViolations.value"
        :row-key="spcViolationKey"
        :loading="spc.spcPending.value"
        :searchable="false"
        :column-settings="false"
        :empty-message="spcViolationEmptyMessage"
      >
        <template #cell-rule="{ row }">
          <span
            :id="spcViolationTargetId(row, spc.spcViolations.value.indexOf(row))"
            class="font-medium scroll-mt-24"
            >{{ row.rule }}</span
          >
        </template>
      </NvDataTable>
    </div>

    <div
      v-if="!listErrorMessage"
      class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.7fr)]"
    >
      <QualityParetoPanel :rows="summary.defectPareto" :pending="ncrsPending" />

      <div class="grid gap-4">
        <NvDataTable
          :columns="dimensionColumns"
          :rows="summary.bySku"
          row-key="label"
          :loading="ncrsPending"
          :searchable="false"
          :column-settings="false"
          empty-message="当前分析时间范围内没有物料维度。"
        >
          <template #cell-defectQuantity="{ row }">{{
            formatQualityQuantity(row.defectQuantity)
          }}</template>
        </NvDataTable>

        <NvDataTable
          :columns="dimensionColumns"
          :rows="summary.bySourceType"
          row-key="label"
          :loading="ncrsPending"
          :searchable="false"
          :column-settings="false"
          empty-message="当前分析时间范围内没有来源维度。"
        >
          <template #cell-defectQuantity="{ row }">{{
            formatQualityQuantity(row.defectQuantity)
          }}</template>
        </NvDataTable>
      </div>
    </div>

    <div class="grid gap-3">
      <div class="flex flex-wrap gap-2">
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/mes/quality"
            ><ClipboardCheckIcon aria-hidden="true" />生产质量记录</RouterLink
          >
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/alarms"
            ><BellRingIcon aria-hidden="true" />设备报警</RouterLink
          >
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/engineering/documents"
            ><FileTextIcon aria-hidden="true" />工程文档</RouterLink
          >
        </NvButton>
      </div>
    </div>
  </BusinessLayout>
</template>
