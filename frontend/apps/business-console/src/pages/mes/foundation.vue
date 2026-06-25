<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useMesFoundationReadiness } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  FieldPro,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadgePro,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '生产准备检查' } })

const { filters, readiness, readinessError, readinessPending, refreshReadiness } = useMesFoundationReadiness()

interface ReadinessArea {
  areaCode?: string
  status?: string
  issues?: Array<{ code?: string, referenceId?: string, message?: string }>
}
const areas = computed(() => (readiness.value?.areas ?? []) as ReadinessArea[])
const blockingIssues = computed(() => readiness.value?.blockingIssues ?? [])
const warningIssues = computed(() => readiness.value?.warningIssues ?? [])
const errorMessage = computed(() => readinessError.value instanceof Error ? readinessError.value.message : '')

// 区域码 → 中文（开工前各就绪来源）；未知码回退原值，不暴露裸码占位。
const AREA_LABELS: Record<string, string> = {
  'masterdata': '主数据',
  'master-data': '主数据',
  'engineering': '工程',
  'inventory': '库存',
  'material': '物料',
  'quality': '质量',
  'capacity': '产能',
  'routing': '工艺路线',
  'bom': '物料清单',
}
function areaLabel(code?: string) {
  if (!code) return '未知区域'
  return AREA_LABELS[code.toLowerCase()] ?? code
}

function statusMeta(status?: string): { label: string, tone: 'success' | 'warning' | 'danger' | 'neutral' } {
  const s = (status ?? '').toLowerCase()
  if (s === 'ready') return { label: '就绪', tone: 'success' }
  if (s === 'warning') return { label: '警告', tone: 'warning' }
  if (s === 'blocked') return { label: '阻塞', tone: 'danger' }
  return { label: status ?? '未知', tone: 'neutral' }
}
const overall = computed(() => statusMeta(readiness.value?.status))

function issueText(issue: { code?: string, message?: string }) {
  return issue.message ?? issue.code ?? '未命名问题'
}

const columns: DataTableProColumn<ReadinessArea>[] = [
  { key: 'areaCode', header: '检查区域', cellClass: 'font-medium', width: 'w-40' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'issues', header: '问题' },
]
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="生产准备检查"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${areas.length} 个检查区域`"
    >
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="readinessPending" @click="refreshReadiness">
          <RefreshCwIcon aria-hidden="true" />
          重新检查
        </ButtonPro>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">
      开工、释放、派工前的辅助就绪检查；不替代主数据 / 工程 / 库存 / 质量各自的维护入口。可选填范围缩小检查。
    </p>

    <div class="grid gap-3 rounded-lg border bg-card p-4">
      <FieldProGroup class="grid gap-3 md:grid-cols-3 lg:grid-cols-5">
        <FieldPro>
          <FieldProLabel for="foundation-site">工厂</FieldProLabel>
          <InputPro id="foundation-site" v-model="filters.siteCode" placeholder="全部" />
        </FieldPro>
        <FieldPro>
          <FieldProLabel for="foundation-line">产线</FieldProLabel>
          <InputPro id="foundation-line" v-model="filters.lineCode" placeholder="全部" />
        </FieldPro>
        <FieldPro>
          <FieldProLabel for="foundation-work-center">工作中心</FieldProLabel>
          <InputPro id="foundation-work-center" v-model="filters.workCenterCode" placeholder="全部" />
        </FieldPro>
        <FieldPro>
          <FieldProLabel for="foundation-sku">物料</FieldProLabel>
          <InputPro id="foundation-sku" v-model="filters.skuId" placeholder="全部" />
        </FieldPro>
        <FieldPro>
          <FieldProLabel for="foundation-version">生产版本</FieldProLabel>
          <InputPro id="foundation-version" v-model="filters.productionVersionId" placeholder="全部" />
        </FieldPro>
      </FieldProGroup>
    </div>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <SectionCards :columns="3">
      <SectionCard description="总就绪状态" :value="overall.label" hint="综合各区域的开工就绪结果" />
      <SectionCard description="阻塞问题" :value="blockingIssues.length" hint="必须先处理才能开工" />
      <SectionCard description="警告问题" :value="warningIssues.length" hint="建议处理，不强制阻断" />
    </SectionCards>

    <!-- IA：就绪检查的核心是「什么挡着我开工」——阻塞项前置成醒目清单。 -->
    <div
      v-if="blockingIssues.length"
      class="grid gap-1 rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm"
      role="alert"
    >
      <span class="font-medium text-destructive">{{ blockingIssues.length }} 项阻塞，需先处理：</span>
      <ul class="ml-4 list-disc text-destructive/90">
        <li v-for="(issue, i) in blockingIssues" :key="i">{{ issueText(issue) }}</li>
      </ul>
    </div>

    <DataTablePro
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
        <StatusBadgePro :label="statusMeta(row.status).label" :tone="statusMeta(row.status).tone" />
      </template>
      <template #cell-issues="{ row }">
        <div v-if="row.issues?.length" class="grid gap-1">
          <span v-for="(issue, i) in row.issues" :key="i">{{ issueText(issue) }}</span>
        </div>
        <span v-else class="text-muted-foreground">无问题</span>
      </template>
    </DataTablePro>
  </BusinessLayout>
</template>
