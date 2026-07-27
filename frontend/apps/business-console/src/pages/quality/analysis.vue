<script setup lang="ts">
import type {
  ComboboxSuggestion,
  EntityPickerOption,
  NvDataTableColumn,
  NvMetricFacet,
  NvMetricSegment,
  SearchSelectOption,
} from '@nerv-iip/ui'
import { qualitySourceTypeLabel } from '@nerv-iip/business-core'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import QualityParetoPanel from '@/components/quality/QualityParetoPanel.vue'
import QualitySpcCharts from '@/components/quality/QualitySpcCharts.vue'
import {
  useBusinessMasterDataResources,
  useBusinessSkus,
} from '@/composables/useBusinessMasterData'
import {
  useQualityInspectionPlanCharacteristics,
  useQualityInspectionPlans,
  useQualityNcrs,
} from '@/composables/useBusinessQuality'
import {
  buildQualityAnalysisSummary,
  buildQualityBucketDetail,
  formatQualityQuantity,
  spcViolationTargetId,
  useQualitySpcAnalysis,
  type QualityAnalysisBucket,
  type QualitySpcViolation,
} from '@/composables/useBusinessQualityAnalysis'
import { friendlyErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvCombobox,
  NvDataTable,
  NvEntityPicker,
  NvInput,
  NvMetricCard,
  NvMetricRing,
  NvPageHeader,
  NvSearchSelect,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
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
import { computed, shallowRef } from 'vue'
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

// SPC 范围目录：SKU 取物料主数据、工作中心取主数据资源目录，选择器只选不填。
const skuCatalog = useBusinessSkus()
const workCenterCatalog = useBusinessMasterDataResources('work-center')
const skuOptions = computed<EntityPickerOption[]>(() =>
  skuCatalog.skus.value.flatMap((sku) => {
    const code = sku.code?.trim()
    if (!code) return []
    return [{ value: code, label: sku.displayName?.trim() || code }]
  }),
)
const workCenterOptions = computed<SearchSelectOption[]>(() =>
  workCenterCatalog.resources.value.flatMap((resource) => {
    const code = resource.code?.trim()
    if (!code) return []
    return [{ value: code, label: resource.displayName?.trim() || code, hint: code }]
  }),
)

// 后端目前没有「按 SKU / 工序查质量特性目录」的接口，特性因此保留自由录入；
// 建议项取当前所选 SKU 对应检验方案（已加载方案列表中首个匹配项）的特性清单，匹配不到就不给建议。
const { inspectionPlans } = useQualityInspectionPlans()
const characteristicSuggestionPlanId = computed(() => {
  const sku = spc.filters.skuCode.trim()
  if (!sku) return ''
  const matched = inspectionPlans.value.find((plan) => plan.skuCode === sku && plan.id)
  return matched?.id ?? ''
})
const { planCharacteristics: suggestionPlanCharacteristics } =
  useQualityInspectionPlanCharacteristics(() => ({
    organizationId: spc.filters.organizationId,
    environmentId: spc.filters.environmentId,
    inspectionPlanId: characteristicSuggestionPlanId.value,
  }))
const characteristicSuggestions = computed<ComboboxSuggestion[]>(() =>
  suggestionPlanCharacteristics.value.flatMap((characteristic) => {
    const code = characteristic.characteristicCode?.trim()
    if (!code) return []
    return [{ value: code, label: characteristic.name?.trim() || code, hint: code }]
  }),
)

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
// 这张卡回显当前生效的 SPC 范围；还没给条件的维度如实标「待选择」。
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

// 两张维度表各自命名表头：SKU 表 join 物料主数据补名称列，来源类型表显中文。
const skuNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const sku of skuCatalog.skus.value) {
    const code = sku.code?.trim()
    const name = sku.displayName?.trim()
    if (code && name) map.set(code, name)
  }
  return map
})
function skuDisplayName(code: string) {
  return skuNameByCode.value.get(code) ?? '—'
}
const skuDimensionColumns: NvDataTableColumn<QualityAnalysisBucket>[] = [
  { key: 'label', header: 'SKU', cellClass: 'font-medium' },
  { key: 'skuName', header: '名称', accessor: (row) => skuDisplayName(row.label) },
  { key: 'count', header: 'NCR 数', align: 'end', width: 'w-20' },
  { key: 'defectQuantity', header: '缺陷数量', align: 'end', width: 'w-24' },
]
const sourceTypeDimensionColumns: NvDataTableColumn<QualityAnalysisBucket>[] = [
  {
    key: 'label',
    header: '来源类型',
    cellClass: 'font-medium',
    accessor: (row) => qualitySourceTypeLabel(row.label),
  },
  { key: 'count', header: 'NCR 数', align: 'end', width: 'w-24' },
  { key: 'defectQuantity', header: '缺陷数量', align: 'end', width: 'w-28' },
]

// 维度行下钻：点击 SKU / 来源类型行，用抽屉展示当前分析窗口内该对象的缺陷构成与逐条记录。
const bucketDetailOpen = shallowRef(false)
const bucketDetailTarget = shallowRef<{ kind: 'sku' | 'sourceType'; label: string } | null>(null)
const bucketDetail = computed(() =>
  bucketDetailTarget.value
    ? buildQualityBucketDetail(
        ncrs.value,
        bucketDetailTarget.value.kind,
        bucketDetailTarget.value.label,
      )
    : null,
)
const bucketDetailTitle = computed(() => {
  const target = bucketDetailTarget.value
  if (!target) return ''
  if (target.kind === 'sku') {
    const name = skuNameByCode.value.get(target.label)
    return name ? `SKU ${target.label} · ${name}` : `SKU ${target.label}`
  }
  return `来源类型 ${qualitySourceTypeLabel(target.label)}`
})
function openBucketDetail(kind: 'sku' | 'sourceType', bucket: QualityAnalysisBucket) {
  bucketDetailTarget.value = { kind, label: bucket.label }
  bucketDetailOpen.value = true
}
const bucketDetailReasonColumns: NvDataTableColumn<QualityAnalysisBucket>[] = [
  { key: 'label', header: '缺陷原因', cellClass: 'font-medium' },
  { key: 'count', header: 'NCR 数', align: 'end', width: 'w-20' },
  { key: 'defectQuantity', header: '缺陷数量', align: 'end', width: 'w-24' },
  { key: 'sharePercent', header: '占比', align: 'end', width: 'w-20' },
]
type BucketDetailRecord = NonNullable<typeof bucketDetail.value>['records'][number]
const bucketDetailRecordColumns: NvDataTableColumn<BucketDetailRecord>[] = [
  { key: 'code', header: '编号', cellClass: 'font-medium', accessor: (row) => row.code ?? '未知' },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'defectReason',
    header: '缺陷原因',
    accessor: (row) => row.defectReason?.trim() || '未填',
  },
  {
    key: 'defectQuantity',
    header: '缺陷数量',
    align: 'end',
    width: 'w-24',
    accessor: (row) =>
      formatQualityQuantity(
        typeof row.defectQuantity === 'number' && Number.isFinite(row.defectQuantity)
          ? row.defectQuantity
          : 0,
      ),
  },
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
            <div class="grid gap-1 text-xs font-medium text-muted-foreground">
              SKU
              <NvEntityPicker
                v-model="spc.filters.skuCode"
                :options="skuOptions"
                title="选择 SKU"
                source-text="数据来自基础数据物料主数据"
                :loading="skuCatalog.skusPending.value"
                clearable
                aria-label="SKU"
              />
            </div>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              特性
              <NvCombobox
                v-model="spc.filters.characteristicCode"
                class="h-9"
                :suggestions="characteristicSuggestions"
                placeholder="录入或选择特性编码"
                empty-text="暂无该 SKU 的特性建议"
              />
            </label>
            <div class="grid gap-1 text-xs font-medium text-muted-foreground">
              工作中心
              <NvSearchSelect
                v-model="spc.filters.workCenterId"
                :options="workCenterOptions"
                :loading="workCenterCatalog.resourcesPending.value"
                aria-label="工作中心"
              />
            </div>
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

      <div class="grid content-start gap-4">
        <NvDataTable
          :columns="skuDimensionColumns"
          :rows="summary.bySku"
          row-key="label"
          :loading="ncrsPending"
          :searchable="false"
          :column-settings="false"
          row-class="cursor-pointer"
          empty-message="当前分析时间范围内没有物料维度。"
          @row-click="openBucketDetail('sku', $event)"
        >
          <template #cell-defectQuantity="{ row }">{{
            formatQualityQuantity(row.defectQuantity)
          }}</template>
        </NvDataTable>

        <NvDataTable
          :columns="sourceTypeDimensionColumns"
          :rows="summary.bySourceType"
          row-key="label"
          :loading="ncrsPending"
          :searchable="false"
          :column-settings="false"
          row-class="cursor-pointer"
          empty-message="当前分析时间范围内没有来源维度。"
          @row-click="openBucketDetail('sourceType', $event)"
        >
          <template #cell-defectQuantity="{ row }">{{
            formatQualityQuantity(row.defectQuantity)
          }}</template>
        </NvDataTable>
      </div>
    </div>

    <!-- 维度行下钻抽屉：当前分析窗口内该对象的缺陷构成交叉呈现（不引入窗口外数据）。 -->
    <NvSheet v-model:open="bucketDetailOpen">
      <NvSheetContent class="gap-0 overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>{{ bucketDetailTitle }}</NvSheetTitle>
          <NvSheetDescription>
            明细来自当前分析时间范围内的不合格品记录，是窗口口径、不是全量历史。
          </NvSheetDescription>
        </NvSheetHeader>
        <div v-if="bucketDetail" class="grid content-start gap-4 px-4 pb-4">
          <dl class="grid grid-cols-3 gap-3">
            <div class="rounded-lg border bg-card p-3">
              <dt class="text-xs text-muted-foreground">NCR 数</dt>
              <dd class="text-lg font-semibold">{{ bucketDetail.ncrCount }}</dd>
            </div>
            <div class="rounded-lg border bg-card p-3">
              <dt class="text-xs text-muted-foreground">缺陷数量</dt>
              <dd class="text-lg font-semibold">
                {{ formatQualityQuantity(bucketDetail.defectQuantity) }}
              </dd>
            </div>
            <div class="rounded-lg border bg-card p-3">
              <dt class="text-xs text-muted-foreground">尚未处置</dt>
              <dd
                class="text-lg font-semibold"
                :class="bucketDetail.openNcrCount > 0 ? 'text-destructive' : ''"
              >
                {{ bucketDetail.openNcrCount }}
              </dd>
            </div>
          </dl>

          <section class="grid gap-2">
            <h3 class="text-sm font-semibold">缺陷原因分布</h3>
            <NvDataTable
              :columns="bucketDetailReasonColumns"
              :rows="bucketDetail.defectReasons"
              row-key="label"
              :searchable="false"
              :column-settings="false"
              empty-message="当前分析时间范围内该对象没有缺陷原因记录。"
            >
              <template #cell-defectQuantity="{ row }">{{
                formatQualityQuantity(row.defectQuantity)
              }}</template>
              <template #cell-sharePercent="{ row }">{{ row.sharePercent }}%</template>
            </NvDataTable>
          </section>

          <section class="grid gap-2">
            <h3 class="text-sm font-semibold">不合格品记录</h3>
            <NvDataTable
              :columns="bucketDetailRecordColumns"
              :rows="bucketDetail.records"
              :row-key="(row) => row.id ?? row.code ?? '未知'"
              :searchable="false"
              :column-settings="false"
              empty-message="当前分析时间范围内没有该对象的不合格品记录。"
            >
              <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
            </NvDataTable>
          </section>
        </div>
      </NvSheetContent>
    </NvSheet>

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
