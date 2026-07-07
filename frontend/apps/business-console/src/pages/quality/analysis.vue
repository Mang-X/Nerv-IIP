<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useQualityNcrs } from '@/composables/useBusinessQuality'
import {
  QUALITY_ANALYSIS_FACADE_AUDIT,
  buildQualityAnalysisSummary,
  useQualitySpcAnalysis,
  type QualityAnalysisBucket,
  type QualitySpcViolation,
} from '@/composables/useBusinessQualityAnalysis'
import {
  ButtonPro,
  DataTablePro,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import {
  BellRingIcon,
  ClipboardCheckIcon,
  FileCheck2Icon,
  FileTextIcon,
  LineChartIcon,
  RefreshCwIcon,
  ShieldAlertIcon,
} from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '质量分析', requiredPermissions: ['business.quality.ncr.read'] } })

const {
  filters,
  ncrs,
  ncrsError,
  ncrsPending,
  ncrsTotal,
  refreshNcrs,
} = useQualityNcrs()
const spc = useQualitySpcAnalysis()

const summary = computed(() => buildQualityAnalysisSummary(ncrs.value, ncrsTotal.value))
const listErrorMessage = computed(() => formatError(ncrsError.value))
const spcErrorMessage = computed(() => formatError(spc.spcError.value))
const trendGapText = '后台尚未提供按时间、工位、设备和班次的全量聚合趋势；本页只展示 NCR 返回窗口内已返回字段的真实派生摘要。'
const spcScopeHint = computed(() =>
  spc.spcReady.value
    ? `${spc.filters.skuCode} / ${spc.filters.characteristicCode} / ${spc.filters.workCenterId}`
    : '填写 SKU、特性和工作中心后查询',
)

const paretoColumns: DataTableProColumn<QualityAnalysisBucket>[] = [
  { key: 'label', header: '缺陷原因', cellClass: 'font-medium' },
  { key: 'count', header: 'NCR 数', align: 'end', width: 'w-24' },
  { key: 'defectQuantity', header: '缺陷数量', align: 'end', width: 'w-28' },
  { key: 'sharePercent', header: '缺陷占比', align: 'end', width: 'w-24' },
]
const dimensionColumns: DataTableProColumn<QualityAnalysisBucket>[] = [
  { key: 'label', header: '对象', cellClass: 'font-medium' },
  { key: 'count', header: 'NCR 数', align: 'end', width: 'w-24' },
  { key: 'defectQuantity', header: '缺陷数量', align: 'end', width: 'w-28' },
]
const spcViolationColumns: DataTableProColumn<QualitySpcViolation>[] = [
  { key: 'rule', header: '判异规则', cellClass: 'font-medium' },
  { key: 'startSubgroupIndex', header: '开始子组', align: 'end', width: 'w-24' },
  { key: 'endSubgroupIndex', header: '结束子组', align: 'end', width: 'w-24' },
  { key: 'message', header: '说明' },
]
type AuditRow = (typeof QUALITY_ANALYSIS_FACADE_AUDIT)[number]
const auditRows = computed<AuditRow[]>(() => [...QUALITY_ANALYSIS_FACADE_AUDIT])
const auditColumns: DataTableProColumn<AuditRow>[] = [
  { key: 'capability', header: '能力', cellClass: 'font-medium' },
  { key: 'qualityService', header: '质量后台' },
  { key: 'businessConsoleFacade', header: '控制台入口', width: 'w-28' },
  { key: 'frontendHandling', header: '当前处理' },
]

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function formatQuantity(value: number) {
  return Number.isInteger(value) ? String(value) : value.toFixed(2)
}
function formatMetric(value: number | null | undefined) {
  return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(2) : '-'
}
function statusTone(value: string) {
  return value === '已接入' ? 'success' : 'warning'
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="质量分析" :breadcrumbs="[{ label: '质量管理' }]" :count="`${summary.totalNcrCount} 条 NCR`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/quality/ncrs"><ShieldAlertIcon aria-hidden="true" />不合格品</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/approval"><FileCheck2Icon aria-hidden="true" />审批中心</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="ncrsPending" @click="refreshNcrs">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="当前窗口 NCR" :value="summary.sampledNcrCount" :hint="summary.sampleNotice" />
      <SectionCard description="待处理 NCR" :value="summary.openNcrCount" hint="状态为 open" />
      <SectionCard description="已提交处置" :value="summary.dispositionedNcrCount" hint="状态为 dispositioned" />
      <SectionCard description="缺陷数量" :value="formatQuantity(summary.totalDefectQuantity)" hint="按当前窗口汇总" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <p class="max-w-3xl text-sm text-muted-foreground">{{ trendGapText }}</p>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <div class="grid gap-4">
      <Toolbar :show-search="false">
        <template #filters>
          <div class="grid w-full gap-3 lg:grid-cols-[repeat(5,minmax(0,1fr))_auto]">
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              SKU
              <InputPro v-model="spc.filters.skuCode" placeholder="SKU-001" />
            </label>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              特性
              <InputPro v-model="spc.filters.characteristicCode" placeholder="DIAMETER" />
            </label>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              工作中心
              <InputPro v-model="spc.filters.workCenterId" placeholder="WC-01" />
            </label>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              子组
              <InputPro v-model="spc.filters.subgroupSize" type="number" min="2" max="10" />
            </label>
            <label class="grid gap-1 text-xs font-medium text-muted-foreground">
              点数
              <InputPro v-model="spc.filters.take" type="number" min="5" max="200" />
            </label>
            <ButtonPro class="self-end" size="sm" type="button" variant="outline" :disabled="!spc.spcReady.value || spc.spcPending.value" @click="spc.refreshSpc">
              <LineChartIcon aria-hidden="true" />
              查询
            </ButtonPro>
          </div>
        </template>
      </Toolbar>

      <SectionCards :columns="4">
        <SectionCard description="SPC 范围" :value="spcScopeHint" hint="SKU / 特性 / 工作中心" />
        <SectionCard description="Xbar UCL" :value="formatMetric(spc.spcChart.value?.controlLimits?.xbarUpperControlLimit)" :hint="spc.spcChart.value?.controlLimits?.locked ? '控制限已锁定' : '自动计算控制限'" />
        <SectionCard description="Cp / Cpk" :value="`${formatMetric(spc.capability.value?.cp)} / ${formatMetric(spc.capability.value?.cpk)}`" :hint="`${spc.capability.value?.sampleCount ?? 0} 个实测值`" />
        <SectionCard description="判异" :value="spc.spcViolations.value.length" hint="质量 SPC 预警，不计入设备报警" />
      </SectionCards>

      <p v-if="spcErrorMessage" class="text-sm text-destructive" role="alert">{{ spcErrorMessage }}</p>

      <DataTablePro
        :columns="spcViolationColumns"
        :rows="spc.spcViolations.value"
        row-key="rule"
        :loading="spc.spcPending.value"
        :searchable="false"
        :column-settings="false"
        empty-message="当前 SPC 范围没有判异。"
      />
    </div>

    <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.7fr)]">
      <DataTablePro
        :columns="paretoColumns"
        :rows="summary.defectPareto"
        row-key="label"
        :loading="ncrsPending"
        :searchable="false"
        :column-settings="false"
        empty-message="当前返回窗口没有 NCR，暂无可汇总的缺陷原因。"
      >
        <template #cell-defectQuantity="{ row }">{{ formatQuantity(row.defectQuantity) }}</template>
        <template #cell-sharePercent="{ row }">{{ row.sharePercent }}%</template>
      </DataTablePro>

      <div class="grid gap-4">
        <DataTablePro
          :columns="dimensionColumns"
          :rows="summary.bySku"
          row-key="label"
          :loading="ncrsPending"
          :searchable="false"
          :column-settings="false"
          empty-message="当前返回窗口没有物料维度。"
        >
          <template #cell-defectQuantity="{ row }">{{ formatQuantity(row.defectQuantity) }}</template>
        </DataTablePro>

        <DataTablePro
          :columns="dimensionColumns"
          :rows="summary.bySourceType"
          row-key="label"
          :loading="ncrsPending"
          :searchable="false"
          :column-settings="false"
          empty-message="当前返回窗口没有来源维度。"
        >
          <template #cell-defectQuantity="{ row }">{{ formatQuantity(row.defectQuantity) }}</template>
        </DataTablePro>
      </div>
    </div>

    <div class="grid gap-3">
      <div class="flex flex-wrap gap-2">
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/mes/quality"><ClipboardCheckIcon aria-hidden="true" />MES 质量上下文</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/alarms"><BellRingIcon aria-hidden="true" />设备报警</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/engineering/documents"><FileTextIcon aria-hidden="true" />工程文档</RouterLink>
        </ButtonPro>
      </div>

      <DataTablePro
        :columns="auditColumns"
        :rows="auditRows"
        row-key="capability"
        :searchable="false"
        :column-settings="false"
        empty-message="暂无能力对照。"
      >
        <template #cell-businessConsoleFacade="{ row }">
          <StatusBadgePro :label="row.businessConsoleFacade" :tone="statusTone(row.businessConsoleFacade)" />
        </template>
      </DataTablePro>
    </div>
  </BusinessLayout>
</template>
