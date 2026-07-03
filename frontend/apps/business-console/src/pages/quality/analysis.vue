<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useQualityNcrs } from '@/composables/useBusinessQuality'
import {
  QUALITY_ANALYSIS_FACADE_AUDIT,
  buildQualityAnalysisSummary,
  type QualityAnalysisBucket,
} from '@/composables/useBusinessQualityAnalysis'
import {
  ButtonPro,
  DataTablePro,
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

const summary = computed(() => buildQualityAnalysisSummary(ncrs.value, ncrsTotal.value))
const listErrorMessage = computed(() => formatError(ncrsError.value))
const trendGapText = '后台尚未提供按时间、工位、设备和班次的全量聚合趋势；本页只展示 NCR 返回窗口内已返回字段的真实派生摘要。'

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
