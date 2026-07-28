<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMesFoundationReadiness } from '@/composables/useBusinessMes'
import {
  useMesMaterialVersionCatalog,
  useProductionScopeCatalog,
} from '@/composables/useMesPickerCatalog'
import { labelFor, MES_READINESS_AREA_LABELS } from '@/data/businessLabels'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvMetricCard,
  NvPageHeader,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '生产准备检查',
    requiredPermissions: ['business.mes.foundation.read'],
  },
})

const { filters, readiness, readinessError, readinessPending, refreshReadiness } =
  useMesFoundationReadiness()

// ── 检查范围（工厂 ▸ 产线 ▸ 工作中心 层级 / 物料 ▸ 生产版本 从属）────────────
// filters 上是 `string | undefined`（留空=全部），选择器要 `string`，这里做一层空串代理。
const {
  siteOptions,
  sitesPending,
  lineOptions,
  linesPending,
  workCenterOptions,
  workCentersPending,
} = useProductionScopeCatalog()
const { skuOptions, skusPending, productionVersionOptions, productionVersionsPending } =
  useMesMaterialVersionCatalog()

function scopeModel(
  field: 'siteCode' | 'lineCode' | 'workCenterCode' | 'skuId' | 'productionVersionId',
) {
  return computed({
    get: () => filters[field] ?? '',
    set: (value: string) => {
      filters[field] = value.trim() ? value : undefined
    },
  })
}
const siteValue = scopeModel('siteCode')
const lineValue = scopeModel('lineCode')
const workCenterValue = scopeModel('workCenterCode')
const skuValue = scopeModel('skuId')
const productionVersionValue = scopeModel('productionVersionId')

const lineChoices = computed(() => lineOptions(siteValue.value))
const workCenterChoices = computed(() => workCenterOptions(siteValue.value, lineValue.value))
const productionVersionChoices = computed(() => productionVersionOptions(skuValue.value))

// 层级规则：上游一变，下游已选值立即作废（否则会留下「A 厂 + B 厂产线」这种不存在的组合）。
watch(siteValue, () => {
  lineValue.value = ''
  workCenterValue.value = ''
})
watch(lineValue, () => {
  workCenterValue.value = ''
})
watch(skuValue, () => {
  productionVersionValue.value = ''
})

interface ReadinessArea {
  areaCode?: string
  status?: string
  issues?: Array<{ code?: string; referenceId?: string; message?: string }>
}
const areas = computed(() => (readiness.value?.areas ?? []) as ReadinessArea[])
const blockingIssues = computed(() => readiness.value?.blockingIssues ?? [])
const warningIssues = computed(() => readiness.value?.warningIssues ?? [])
const errorMessage = computed(() =>
  readinessError.value instanceof Error ? readinessError.value.message : '',
)

// 区域码 → 中文（开工前各就绪来源）；未知码回退原值，不暴露裸码占位。
// 词表与生产驾驶舱共用一份（`@/data/businessLabels`），两页说法不会漂移。
function areaLabel(code?: string) {
  if (!code) return '未知区域'
  return labelFor(MES_READINESS_AREA_LABELS, code)
}

function statusMeta(status?: string): {
  label: string
  tone: 'success' | 'warning' | 'danger' | 'neutral'
} {
  const s = (status ?? '').toLowerCase()
  if (s === 'ready') return { label: '就绪', tone: 'success' }
  if (s === 'warning') return { label: '警告', tone: 'warning' }
  if (s === 'blocked') return { label: '阻塞', tone: 'danger' }
  return { label: status ?? '未知', tone: 'neutral' }
}
const overall = computed(() => statusMeta(readiness.value?.status))
const issueTotal = computed(() => blockingIssues.value.length + warningIssues.value.length)
const issueSegments = computed(() => [
  { key: 'blocking', label: '阻塞', value: blockingIssues.value.length, tone: 'danger' as const },
  { key: 'warning', label: '警告', value: warningIssues.value.length, tone: 'warning' as const },
])
// 就绪卡的落点是「现在能不能开工、下一步做什么」，不是复述状态码。
const readinessGuidance = computed(() => {
  if (!readiness.value) return '填写上方范围后点「重新检查」，按工厂、产线或工作中心核对开工条件。'
  if (blockingIssues.value.length) return '先逐条清掉阻塞项，再回到工单与派工下达生产。'
  if (warningIssues.value.length) return '警告不阻断开工，建议开工前确认一遍再放行。'
  return '各检查区域均已就绪，可以进入工单与派工下达生产。'
})
const readinessStatusPill = computed(() => {
  if (!readiness.value) return { label: '尚未检查', tone: 'neutral' as const }
  if (blockingIssues.value.length) return { label: '不能开工', tone: 'danger' as const }
  if (warningIssues.value.length) return { label: '可开工，有提醒', tone: 'warning' as const }
  return { label: '可以开工', tone: 'success' as const }
})

function issueText(issue: { code?: string; message?: string }) {
  return issue.message ?? issue.code ?? '未命名问题'
}

const columns: NvDataTableColumn<ReadinessArea>[] = [
  { key: 'areaCode', header: '检查区域', cellClass: 'font-medium', width: 'w-40' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'issues', header: '问题' },
]
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="生产准备检查"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${areas.length} 个检查区域`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="readinessPending"
          @click="refreshReadiness"
        >
          <RefreshCwIcon aria-hidden="true" />
          重新检查
        </NvButton>
      </template>
    </NvPageHeader>

    <p class="text-sm text-muted-foreground">
      开工、释放、派工前的辅助就绪检查；不替代主数据 / 工程 / 库存 /
      质量各自的维护入口。可选填范围缩小检查。
    </p>

    <div class="grid gap-3 rounded-lg border bg-card p-4">
      <NvFieldGroup class="grid gap-3 md:grid-cols-3 lg:grid-cols-5">
        <NvField>
          <NvFieldLabel for="foundation-site">工厂</NvFieldLabel>
          <NvEntityPicker
            id="foundation-site"
            v-model="siteValue"
            :options="siteOptions"
            title="选择工厂"
            placeholder="全部"
            source-text="数据来自基础数据工厂主数据"
            empty-text="暂无工厂，请先在基础数据维护"
            :loading="sitesPending"
            aria-label="工厂"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="foundation-line">产线</NvFieldLabel>
          <NvEntityPicker
            id="foundation-line"
            v-model="lineValue"
            :options="lineChoices"
            title="选择产线"
            placeholder="全部"
            :source-text="siteValue ? '仅列所选工厂下的产线' : '数据来自基础数据产线主数据'"
            :empty-text="
              siteValue ? '所选工厂下暂无产线，请先在基础数据维护' : '暂无产线，请先在基础数据维护'
            "
            :loading="linesPending"
            aria-label="产线"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="foundation-work-center">工作中心</NvFieldLabel>
          <NvEntityPicker
            id="foundation-work-center"
            v-model="workCenterValue"
            :options="workCenterChoices"
            title="选择工作中心"
            placeholder="全部"
            :source-text="
              lineValue || siteValue ? '仅列所选范围下的工作中心' : '数据来自基础数据工作中心主数据'
            "
            empty-text="所选范围下暂无工作中心，请先在基础数据维护"
            :loading="workCentersPending"
            aria-label="工作中心"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="foundation-sku">物料</NvFieldLabel>
          <NvEntityPicker
            id="foundation-sku"
            v-model="skuValue"
            :options="skuOptions"
            title="选择物料"
            placeholder="全部"
            source-text="数据来自基础数据物料主数据"
            empty-text="暂无物料，请先在基础数据维护"
            :loading="skusPending"
            aria-label="物料"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="foundation-version">生产版本</NvFieldLabel>
          <NvEntityPicker
            id="foundation-version"
            v-model="productionVersionValue"
            :options="productionVersionChoices"
            title="选择生产版本"
            :placeholder="skuValue ? '全部' : '先选物料'"
            :disabled="!skuValue"
            source-text="仅列所选物料的生产版本"
            empty-text="该物料暂无生产版本，请先在工程数据维护"
            :loading="productionVersionsPending"
            aria-label="生产版本"
            clearable
          />
        </NvField>
      </NvFieldGroup>
    </div>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <NvMetricCard
        variant="alert"
        label="开工就绪"
        :value="overall.label"
        :tone="overall.tone"
        :status="readinessStatusPill"
        :foot-start="readinessGuidance"
        :action="{ label: '重新检查' }"
        @action="refreshReadiness"
      />
      <NvMetricCard
        v-if="issueTotal > 0"
        variant="breakdown"
        label="待处理问题"
        :value="issueTotal"
        unit="项"
        :segments="issueSegments"
      />
    </div>

    <!-- IA：就绪检查的核心是「什么挡着我开工」——阻塞项前置成醒目清单。 -->
    <div
      v-if="blockingIssues.length"
      class="grid gap-1 rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm"
      role="alert"
    >
      <span class="font-medium text-destructive"
        >{{ blockingIssues.length }} 项阻塞，需先处理：</span
      >
      <ul class="ml-4 list-disc text-destructive/90">
        <li v-for="(issue, i) in blockingIssues" :key="i">{{ issueText(issue) }}</li>
      </ul>
    </div>

    <NvDataTable
      :columns="columns"
      :rows="areas"
      row-key="areaCode"
      :loading="readinessPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无检查结果。点「重新检查」按当前范围运行就绪检查。"
    >
      <template #cell-areaCode="{ row }">{{ areaLabel(row.areaCode) }}</template>
      <template #cell-status="{ row }">
        <NvStatusBadge :label="statusMeta(row.status).label" :tone="statusMeta(row.status).tone" />
      </template>
      <template #cell-issues="{ row }">
        <div v-if="row.issues?.length" class="grid gap-1">
          <span v-for="(issue, i) in row.issues" :key="i">{{ issueText(issue) }}</span>
        </div>
        <span v-else class="text-muted-foreground">无问题</span>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
