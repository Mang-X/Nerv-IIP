<script setup lang="ts">
import type {
  BusinessConsoleBomExplosionDiagnostic,
  BusinessConsoleBomExplosionNode,
  BusinessConsoleBomWhereUsedItem,
} from '@nerv-iip/api-client'
import type { DataTableProColumn, StatusTone } from '@nerv-iip/ui'
import { useBomAnalysis } from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { formatDate, today } from '@/utils/format'
import {
  ButtonPro,
  DataTablePro,
  DatePickerPro,
  FieldPro,
  FieldProLabel,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { NetworkIcon, SearchIcon } from 'lucide-vue-next'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: 'BOM 分析', requiredPermissions: ['business.engineering.boms.read'] } })

type BomKind = 'engineering' | 'manufacturing'
type AnalysisView = 'tree' | 'explosion' | 'where-used'

interface TreeRow extends BusinessConsoleBomExplosionNode {
  key: string
  depth: number
  hasChildren: boolean
}

const route = useRoute()
const router = useRouter()
const {
  explosion,
  whereUsed,
  pending,
  error,
  loadEngineeringExplosion,
  loadManufacturingExplosion,
  loadEngineeringWhereUsed,
  loadManufacturingWhereUsed,
} = useBomAnalysis()

const form = reactive({
  kind: 'engineering' as BomKind,
  view: 'tree' as AnalysisView,
  rootCode: '',
  componentCode: '',
  effectiveDate: today(),
  lotSize: '1',
  bomCode: '',
  revision: '',
})
const submitted = ref(false)

const analysisViews: Array<{ value: AnalysisView, label: string }> = [
  { value: 'tree', label: '树视图' },
  { value: 'explosion', label: '爆炸' },
  { value: 'where-used', label: '反查' },
]
const kindOptions = [
  { value: 'engineering', label: 'EBOM' },
  { value: 'manufacturing', label: 'MBOM' },
]

const codeLabel = computed(() => form.kind === 'engineering' ? '父项物料' : '产出物料')
const codePlaceholder = computed(() => form.kind === 'engineering' ? '如 FG-100' : '如 SKU-FG-100')
const canSubmit = computed(() => {
  if (!form.effectiveDate) return false
  return form.view === 'where-used'
    ? form.componentCode.trim().length > 0
    : form.rootCode.trim().length > 0
})
const root = computed(() => explosion.value?.root)
const diagnostics = computed(() => explosion.value?.diagnostics ?? [])
const whereUsedRows = computed(() => whereUsed.value?.items ?? [])
const flattenedNodes = computed(() => flattenBom(root.value))
const resultCount = computed(() => form.view === 'where-used' ? whereUsedRows.value.length : flattenedNodes.value.length)
const warningCount = computed(() => diagnostics.value.filter((d) => (d.severity ?? '').toLowerCase() !== 'error').length)
const errorCount = computed(() => diagnostics.value.filter((d) => (d.severity ?? '').toLowerCase() === 'error').length)
const errorMessage = computed(() => formatError(error.value))

const treeColumns: DataTableProColumn<TreeRow>[] = [
  { key: 'itemCode', header: '物料', cellClass: 'font-medium' },
  { key: 'requiredQuantity', header: '需求量', align: 'end', width: 'w-28' },
  { key: 'unitOfMeasureCode', header: '单位', width: 'w-24' },
  { key: 'bomCode', header: 'BOM 版本' },
  { key: 'flags', header: '属性', width: 'w-44' },
]
const explosionColumns: DataTableProColumn<TreeRow>[] = [
  { key: 'itemCode', header: '物料', cellClass: 'font-medium' },
  { key: 'level', header: '层级', align: 'end', width: 'w-20' },
  { key: 'lineQuantity', header: '行数量', align: 'end', width: 'w-24' },
  { key: 'requiredQuantity', header: '滚算需求', align: 'end', width: 'w-28' },
  { key: 'yield', header: '损耗/得率', align: 'end', width: 'w-28' },
  { key: 'alternates', header: '替代/位号' },
]
const whereUsedColumns: DataTableProColumn<BusinessConsoleBomWhereUsedItem>[] = [
  { key: 'parentItemCode', header: '父项', cellClass: 'font-medium' },
  { key: 'bomCode', header: 'BOM 版本' },
  { key: 'lineQuantity', header: '用量', align: 'end', width: 'w-24' },
  { key: 'unitOfMeasureCode', header: '单位', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
  { key: 'flags', header: '行属性', width: 'w-44' },
]
const diagnosticColumns: DataTableProColumn<BusinessConsoleBomExplosionDiagnostic>[] = [
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'itemCode', header: '物料', width: 'w-32' },
  { key: 'message', header: '诊断' },
  { key: 'path', header: '路径' },
]

watch(() => form.kind, () => {
  form.rootCode = ''
  form.componentCode = ''
  form.bomCode = ''
  form.revision = ''
})

onMounted(() => {
  applyRouteQuery()
  if (canSubmit.value) {
    void submit()
  }
})

function firstQuery(value: unknown): string | undefined {
  if (Array.isArray(value)) return value[0] ?? undefined
  return typeof value === 'string' ? value : undefined
}

function applyRouteQuery() {
  const kind = firstQuery(route.query.kind)
  const view = firstQuery(route.query.view)
  form.kind = kind === 'manufacturing' ? 'manufacturing' : 'engineering'
  if (view === 'tree' || view === 'explosion' || view === 'where-used') form.view = view
  form.rootCode = firstQuery(route.query.itemCode) ?? firstQuery(route.query.skuCode) ?? ''
  form.componentCode = firstQuery(route.query.componentCode) ?? ''
  form.effectiveDate = firstQuery(route.query.effectiveDate) ?? today()
  form.lotSize = firstQuery(route.query.lotSize) ?? '1'
  form.bomCode = firstQuery(route.query.bomCode) ?? ''
  form.revision = firstQuery(route.query.revision) ?? ''
}

async function submit() {
  submitted.value = true
  if (!canSubmit.value) return
  const lotSize = parsePositiveNumber(form.lotSize)
  const bomCode = form.bomCode.trim() || undefined
  const revision = form.revision.trim() || undefined
  const effectiveDate = form.effectiveDate

  await router.replace({
    query: {
      ...route.query,
      kind: form.kind,
      view: form.view,
      effectiveDate,
      ...(form.view === 'where-used'
        ? { componentCode: form.componentCode.trim(), itemCode: undefined, skuCode: undefined }
        : {
            [form.kind === 'engineering' ? 'itemCode' : 'skuCode']: form.rootCode.trim(),
            componentCode: undefined,
          }),
      lotSize: form.view === 'where-used' ? undefined : String(lotSize ?? 1),
      bomCode,
      revision,
    },
  })

  if (form.view === 'where-used') {
    const input = { componentCode: form.componentCode.trim(), effectiveDate }
    if (form.kind === 'engineering') await loadEngineeringWhereUsed(input)
    else await loadManufacturingWhereUsed(input)
    return
  }

  const input = {
    code: form.rootCode.trim(),
    effectiveDate,
    lotSize,
    bomCode,
    revision,
  }
  if (form.kind === 'engineering') await loadEngineeringExplosion(input)
  else await loadManufacturingExplosion(input)
}

function flattenBom(node: BusinessConsoleBomExplosionNode | undefined, depth = 0, prefix = 'root'): TreeRow[] {
  if (!node) return []
  const children = node.children ?? []
  return [
    {
      ...node,
      key: `${prefix}:${node.itemCode ?? 'node'}:${node.bomCode ?? ''}:${node.revision ?? ''}`,
      depth,
      hasChildren: children.length > 0,
    },
    ...children.flatMap((child, index) => flattenBom(child, depth + 1, `${prefix}.${index}`)),
  ]
}

function parsePositiveNumber(value: string): number | undefined {
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : undefined
}

function formatQty(value?: number | null) {
  if (value === null || value === undefined) return '无'
  return Number(value).toLocaleString('zh-CN', { maximumFractionDigits: 6 })
}

function formatRate(value?: number | null) {
  if (value === null || value === undefined) return '无'
  return Number(value).toLocaleString('zh-CN', { style: 'percent', maximumFractionDigits: 2 })
}

function flagLabels(row: Pick<TreeRow, 'isPhantom' | 'backflush' | 'alternateGroup' | 'alternatePriority'>) {
  const labels: string[] = []
  if (row.isPhantom) labels.push('虚拟件')
  if (row.backflush) labels.push('倒冲')
  if (row.alternateGroup) labels.push(`替代组 ${row.alternateGroup}${row.alternatePriority ? `/${row.alternatePriority}` : ''}`)
  return labels
}

function diagnosticTone(severity?: string | null): StatusTone {
  return (severity ?? '').toLowerCase() === 'error' ? 'danger' : 'warning'
}

function diagnosticLabel(severity?: string | null) {
  return (severity ?? '').toLowerCase() === 'error' ? '错误' : '警告'
}

function formatError(value: unknown) {
  if (!value) return ''
  if (value instanceof Error) return value.message
  if (typeof value === 'string') return value
  return '加载 BOM 分析失败，请稍后重试。'
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="BOM 分析"
      description="多级 BOM 树、滚算爆炸与反查"
    />

    <SectionCards :columns="3">
      <SectionCard description="结果行" :value="resultCount" hint="来自后端 BOM facade" />
      <SectionCard description="警告" :value="warningCount" hint="缺失下级版本等诊断" />
      <SectionCard description="错误" :value="errorCount" hint="循环引用等阻断诊断" />
    </SectionCards>

    <form class="grid gap-4 rounded-md border bg-background p-4" @submit.prevent="submit">
      <div class="flex flex-wrap items-center gap-2" role="group" aria-label="分析视图">
        <ButtonPro
          v-for="option in analysisViews"
          :key="option.value"
          type="button"
          size="sm"
          :variant="form.view === option.value ? 'default' : 'outline'"
          @click="form.view = option.value"
        >
          {{ option.label }}
        </ButtonPro>
      </div>

      <div class="grid gap-3 md:grid-cols-[11rem_1fr_1fr_9rem]">
        <FieldPro>
          <FieldProLabel for="bom-kind">BOM 类型</FieldProLabel>
          <SelectPro id="bom-kind" v-model="form.kind">
            <SelectProTrigger class="h-9"><SelectProValue /></SelectProTrigger>
            <SelectProContent>
              <SelectProItem v-for="option in kindOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
            </SelectProContent>
          </SelectPro>
        </FieldPro>

        <FieldPro v-if="form.view !== 'where-used'" :data-invalid="submitted && !form.rootCode.trim()">
          <FieldProLabel for="bom-root">{{ codeLabel }}</FieldProLabel>
          <InputPro id="bom-root" v-model="form.rootCode" :placeholder="codePlaceholder" />
        </FieldPro>
        <FieldPro v-else :data-invalid="submitted && !form.componentCode.trim()">
          <FieldProLabel for="bom-component">组件物料</FieldProLabel>
          <InputPro id="bom-component" v-model="form.componentCode" placeholder="如 RM-200" />
        </FieldPro>

        <FieldPro>
          <FieldProLabel for="bom-effective">有效日期</FieldProLabel>
          <DatePickerPro id="bom-effective" v-model="form.effectiveDate" />
        </FieldPro>

        <FieldPro v-if="form.view !== 'where-used'">
          <FieldProLabel for="bom-lot">批量</FieldProLabel>
          <InputPro id="bom-lot" v-model="form.lotSize" type="number" min="0.000001" step="any" />
        </FieldPro>
      </div>

      <div v-if="form.view !== 'where-used'" class="grid gap-3 md:grid-cols-[1fr_1fr_auto]">
        <FieldPro>
          <FieldProLabel for="bom-code">指定 BOM</FieldProLabel>
          <InputPro id="bom-code" v-model="form.bomCode" placeholder="留空自动选择" />
        </FieldPro>
        <FieldPro>
          <FieldProLabel for="bom-revision">指定修订</FieldProLabel>
          <InputPro id="bom-revision" v-model="form.revision" placeholder="留空自动选择" />
        </FieldPro>
        <div class="flex items-end">
          <ButtonPro type="submit" :disabled="pending">
            <Spinner v-if="pending" aria-hidden="true" />
            <NetworkIcon v-else aria-hidden="true" />
            分析
          </ButtonPro>
        </div>
      </div>
      <div v-else class="flex justify-end">
        <ButtonPro type="submit" :disabled="pending">
          <Spinner v-if="pending" aria-hidden="true" />
          <SearchIcon v-else aria-hidden="true" />
          反查
        </ButtonPro>
      </div>

      <p v-if="submitted && !canSubmit" class="text-sm text-destructive" role="alert">
        请填写物料编码和有效日期。
      </p>
      <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
    </form>

    <Toolbar v-if="form.view !== 'where-used'" search-placeholder="按物料、BOM 或路径筛选" />

    <DataTablePro
      v-if="form.view === 'tree'"
      :columns="treeColumns"
      :rows="flattenedNodes"
      :row-key="(row) => row.key"
      :loading="pending"
      empty-message="当前条件没有 BOM 树。"
    >
      <template #cell-itemCode="{ row }">
        <div class="flex items-center gap-2" :style="{ paddingLeft: `${row.depth * 1.25}rem` }">
          <span class="text-muted-foreground">{{ row.hasChildren ? '▾' : '•' }}</span>
          <div class="flex flex-col gap-0.5">
            <span>{{ row.itemCode || '无' }}</span>
            <span v-if="row.parentItemCode" class="text-xs text-muted-foreground">上级 {{ row.parentItemCode }}</span>
          </div>
        </div>
      </template>
      <template #cell-requiredQuantity="{ row }">{{ formatQty(row.requiredQuantity) }}</template>
      <template #cell-bomCode="{ row }">
        {{ row.bomCode ? `${row.bomCode} / ${row.revision ?? '无'}` : '无' }}
      </template>
      <template #cell-flags="{ row }">
        <div class="flex flex-wrap gap-1">
          <StatusBadgePro v-for="flag in flagLabels(row)" :key="flag" :label="flag" tone="neutral" />
          <span v-if="!flagLabels(row).length" class="text-muted-foreground">无</span>
        </div>
      </template>
    </DataTablePro>

    <DataTablePro
      v-else-if="form.view === 'explosion'"
      :columns="explosionColumns"
      :rows="flattenedNodes"
      :row-key="(row) => row.key"
      :loading="pending"
      empty-message="当前条件没有 BOM 爆炸结果。"
    >
      <template #cell-itemCode="{ row }">
        <span :style="{ paddingLeft: `${row.depth * 1.25}rem` }">{{ row.itemCode || '无' }}</span>
      </template>
      <template #cell-lineQuantity="{ row }">{{ formatQty(row.lineQuantity) }}</template>
      <template #cell-requiredQuantity="{ row }">{{ formatQty(row.requiredQuantity) }}</template>
      <template #cell-yield="{ row }">
        {{ formatRate(row.scrapRate) }} / {{ formatRate(row.yieldRate) }}
      </template>
      <template #cell-alternates="{ row }">
        <div class="grid gap-0.5 text-sm">
          <span>{{ row.substituteSkuCodes || row.alternateGroup || '无' }}</span>
          <span v-if="row.referenceDesignators" class="text-xs text-muted-foreground">{{ row.referenceDesignators }}</span>
        </div>
      </template>
    </DataTablePro>

    <DataTablePro
      v-else
      :columns="whereUsedColumns"
      :rows="whereUsedRows"
      :row-key="(row) => `${row.bomKind}:${row.bomCode}:${row.revision}:${row.parentItemCode}`"
      :loading="pending"
      empty-message="当前条件没有反查结果。"
    >
      <template #cell-bomCode="{ row }">{{ row.bomCode }} / {{ row.revision }}</template>
      <template #cell-lineQuantity="{ row }">{{ formatQty(row.lineQuantity) }}</template>
      <template #cell-effectiveDate="{ row }">{{ formatDate(row.effectiveDate) }}</template>
      <template #cell-flags="{ row }">
        <div class="flex flex-wrap gap-1">
          <StatusBadgePro v-for="flag in flagLabels(row)" :key="flag" :label="flag" tone="neutral" />
          <span v-if="!flagLabels(row).length" class="text-muted-foreground">无</span>
        </div>
      </template>
    </DataTablePro>

    <section v-if="diagnostics.length" class="grid gap-3">
      <h2 class="text-base font-semibold">诊断</h2>
      <DataTablePro
        :columns="diagnosticColumns"
        :rows="diagnostics"
        :row-key="(row) => `${row.severity}:${row.itemCode}:${row.path}:${row.code}`"
        :searchable="false"
        :column-settings="false"
        empty-message="没有诊断。"
      >
        <template #cell-severity="{ row }">
          <StatusBadgePro :label="diagnosticLabel(row.severity)" :tone="diagnosticTone(row.severity)" />
        </template>
      </DataTablePro>
    </section>
  </BusinessLayout>
</template>
