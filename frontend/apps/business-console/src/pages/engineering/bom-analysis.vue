<script setup lang="ts">
import type {
  BusinessConsoleBomExplosionDiagnostic,
  BusinessConsoleBomExplosionNode,
  BusinessConsoleBomDiffLineItem,
  BusinessConsoleBomWhereUsedItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, StatusTone } from '@nerv-iip/ui'
import { useBomAnalysis } from '@/composables/useProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { formatDate, today } from '@/utils/format'
import {
  NvButton,
  NvDataTable,
  NvDatePicker,
  NvField,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { NetworkIcon, SearchIcon } from 'lucide-vue-next'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: 'BOM 分析',
    requiredPermissions: ['business.engineering.boms.read'],
  },
})

type BomKind = 'engineering' | 'manufacturing'
type AnalysisView = 'tree' | 'explosion' | 'where-used' | 'diff'

interface TreeRow extends BusinessConsoleBomExplosionNode {
  key: string
  depth: number
  hasChildren: boolean
}

const route = useRoute()
const router = useRouter()
const {
  explosion,
  diff,
  whereUsed,
  pending,
  error,
  loadBomDiff,
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
  fromBomCode: '',
  fromRevision: '',
  toBomCode: '',
  toRevision: '',
})
const submitted = ref(false)

const analysisViews: Array<{ value: AnalysisView; label: string }> = [
  { value: 'tree', label: '树视图' },
  { value: 'explosion', label: '爆炸' },
  { value: 'where-used', label: '反查' },
  { value: 'diff', label: '对比' },
]
const kindOptions = [
  { value: 'engineering', label: 'EBOM' },
  { value: 'manufacturing', label: 'MBOM' },
]

const codeLabel = computed(() => (form.kind === 'engineering' ? '父项物料' : '产出物料'))
const codePlaceholder = computed(() =>
  form.kind === 'engineering' ? '如 FG-100' : '如 SKU-FG-100',
)
const canSubmit = computed(() => {
  if (form.view === 'diff') {
    return (
      !!form.fromBomCode.trim() &&
      !!form.fromRevision.trim() &&
      !!form.toBomCode.trim() &&
      !!form.toRevision.trim()
    )
  }
  if (!form.effectiveDate) return false
  return form.view === 'where-used'
    ? form.componentCode.trim().length > 0
    : form.rootCode.trim().length > 0
})
const root = computed(() => explosion.value?.root)
const diagnostics = computed(() => explosion.value?.diagnostics ?? [])
const diffRows = computed(() => diff.value?.lines ?? [])
const whereUsedRows = computed(() => whereUsed.value?.items ?? [])
const flattenedNodes = computed(() => flattenBom(root.value))
const resultCount = computed(() => {
  if (form.view === 'where-used') return whereUsedRows.value.length
  if (form.view === 'diff') return diffRows.value.length
  return flattenedNodes.value.length
})
const warningCount = computed(
  () => diagnostics.value.filter((d) => (d.severity ?? '').toLowerCase() !== 'error').length,
)
const errorCount = computed(
  () => diagnostics.value.filter((d) => (d.severity ?? '').toLowerCase() === 'error').length,
)
const errorMessage = computed(() => formatError(error.value))

const treeColumns: NvDataTableColumn<TreeRow>[] = [
  { key: 'itemCode', header: '物料', cellClass: 'font-medium' },
  { key: 'requiredQuantity', header: '需求量', align: 'end', width: 'w-28' },
  { key: 'unitOfMeasureCode', header: '单位', width: 'w-24' },
  { key: 'bomCode', header: 'BOM 版本' },
  { key: 'flags', header: '属性', width: 'w-44' },
]
const explosionColumns: NvDataTableColumn<TreeRow>[] = [
  { key: 'itemCode', header: '物料', cellClass: 'font-medium' },
  { key: 'level', header: '层级', align: 'end', width: 'w-20' },
  { key: 'lineQuantity', header: '行数量', align: 'end', width: 'w-24' },
  { key: 'requiredQuantity', header: '滚算需求', align: 'end', width: 'w-28' },
  { key: 'yield', header: '损耗/得率', align: 'end', width: 'w-28' },
  { key: 'alternates', header: '替代/位号' },
]
const whereUsedColumns: NvDataTableColumn<BusinessConsoleBomWhereUsedItem>[] = [
  { key: 'parentItemCode', header: '父项', cellClass: 'font-medium' },
  { key: 'bomCode', header: 'BOM 版本' },
  { key: 'lineQuantity', header: '用量', align: 'end', width: 'w-24' },
  { key: 'unitOfMeasureCode', header: '单位', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
  { key: 'flags', header: '行属性', width: 'w-44' },
]
const diffColumns: NvDataTableColumn<BusinessConsoleBomDiffLineItem>[] = [
  { key: 'changeType', header: '变化', width: 'w-24' },
  { key: 'itemCode', header: '物料', cellClass: 'font-medium' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-32' },
  { key: 'uom', header: '单位', width: 'w-28' },
  { key: 'rates', header: '损耗/得率', align: 'end', width: 'w-40' },
  { key: 'fields', header: '字段变化' },
]
const diagnosticColumns: NvDataTableColumn<BusinessConsoleBomExplosionDiagnostic>[] = [
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'itemCode', header: '物料', width: 'w-32' },
  { key: 'message', header: '诊断' },
  { key: 'path', header: '路径' },
]

watch(
  () => form.kind,
  () => {
    form.rootCode = ''
    form.componentCode = ''
    form.bomCode = ''
    form.revision = ''
    form.fromBomCode = ''
    form.fromRevision = ''
    form.toBomCode = ''
    form.toRevision = ''
  },
)

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
  if (view === 'tree' || view === 'explosion' || view === 'where-used' || view === 'diff')
    form.view = view
  form.rootCode = firstQuery(route.query.itemCode) ?? firstQuery(route.query.skuCode) ?? ''
  form.componentCode = firstQuery(route.query.componentCode) ?? ''
  form.effectiveDate = firstQuery(route.query.effectiveDate) ?? today()
  form.lotSize = firstQuery(route.query.lotSize) ?? '1'
  form.bomCode = firstQuery(route.query.bomCode) ?? ''
  form.revision = firstQuery(route.query.revision) ?? ''
  form.fromBomCode = firstQuery(route.query.fromBomCode) ?? ''
  form.fromRevision = firstQuery(route.query.fromRevision) ?? ''
  form.toBomCode = firstQuery(route.query.toBomCode) ?? ''
  form.toRevision = firstQuery(route.query.toRevision) ?? ''
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
      effectiveDate: form.view === 'diff' ? undefined : effectiveDate,
      ...(form.view === 'where-used'
        ? { componentCode: form.componentCode.trim(), itemCode: undefined, skuCode: undefined }
        : form.view === 'diff'
          ? { componentCode: undefined, itemCode: undefined, skuCode: undefined }
          : {
              [form.kind === 'engineering' ? 'itemCode' : 'skuCode']: form.rootCode.trim(),
              componentCode: undefined,
            }),
      lotSize:
        form.view === 'where-used' || form.view === 'diff' ? undefined : String(lotSize ?? 1),
      bomCode: form.view === 'diff' ? undefined : bomCode,
      revision: form.view === 'diff' ? undefined : revision,
      fromBomCode: form.view === 'diff' ? form.fromBomCode.trim() : undefined,
      fromRevision: form.view === 'diff' ? form.fromRevision.trim() : undefined,
      toBomCode: form.view === 'diff' ? form.toBomCode.trim() : undefined,
      toRevision: form.view === 'diff' ? form.toRevision.trim() : undefined,
    },
  })

  if (form.view === 'diff') {
    await loadBomDiff({
      bomKind: form.kind,
      fromBomCode: form.fromBomCode.trim(),
      fromRevision: form.fromRevision.trim(),
      toBomCode: form.toBomCode.trim(),
      toRevision: form.toRevision.trim(),
    })
    return
  }

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

function flattenBom(
  node: BusinessConsoleBomExplosionNode | undefined,
  depth = 0,
  prefix = 'root',
): TreeRow[] {
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

function flagLabels(
  row: Pick<TreeRow, 'isPhantom' | 'backflush' | 'alternateGroup' | 'alternatePriority'>,
) {
  const labels: string[] = []
  if (row.isPhantom) labels.push('虚拟件')
  if (row.backflush) labels.push('倒冲')
  if (row.alternateGroup)
    labels.push(
      `替代组 ${row.alternateGroup}${row.alternatePriority ? `/${row.alternatePriority}` : ''}`,
    )
  return labels
}

function diffChangeLabel(changeType?: string | null) {
  const normalized = (changeType ?? '').toLowerCase()
  if (normalized === 'added') return '新增'
  if (normalized === 'removed') return '删除'
  if (normalized === 'replaced') return '替换'
  if (normalized === 'changed') return '变更'
  return changeType || '变化'
}

function diffChangeTone(changeType?: string | null): StatusTone {
  const normalized = (changeType ?? '').toLowerCase()
  if (normalized === 'added') return 'success'
  if (normalized === 'removed') return 'danger'
  if (normalized === 'replaced') return 'warning'
  return 'info'
}

function diffItemLabel(row: BusinessConsoleBomDiffLineItem) {
  if (row.changeType?.toLowerCase() === 'replaced') {
    return `${row.oldItemCode || '无'} -> ${row.newItemCode || '无'}`
  }
  return row.newItemCode || row.oldItemCode || '无'
}

function formatText(value?: number | string | null) {
  if (value === null || value === undefined || value === '') return '无'
  return String(value)
}

function formatQuantityDiff(row: BusinessConsoleBomDiffLineItem) {
  return `${formatQty(row.oldQuantity)} -> ${formatQty(row.newQuantity)}`
}

function formatRateDiff(oldValue?: number | null, newValue?: number | null) {
  return `${formatRate(oldValue)} -> ${formatRate(newValue)}`
}

function formatTextDiff(oldValue?: string | null, newValue?: string | null) {
  return `${formatText(oldValue)} -> ${formatText(newValue)}`
}

function fieldChangeLabels(row: BusinessConsoleBomDiffLineItem) {
  const labels =
    row.fieldChanges?.map(
      (field) =>
        `${fieldLabel(field.fieldName)}: ${field.oldValue ?? '无'} -> ${field.newValue ?? '无'}`,
    ) ?? []
  return labels.length ? labels : ['无']
}

function fieldLabel(fieldName?: string | null) {
  const labels: Record<string, string> = {
    quantity: '数量',
    unitOfMeasureCode: '单位',
    scrapRate: '损耗',
    yieldRate: '得率',
    isPhantom: '虚拟件',
    alternateGroup: '替代组',
    alternatePriority: '替代优先级',
    substituteSkuCodes: '替代料',
    referenceDesignators: '位号',
    backflush: '倒冲',
  }
  return labels[fieldName ?? ''] ?? (fieldName || '字段')
}

function isContextNode(row: Pick<TreeRow, 'itemCode'>) {
  return (
    !!form.componentCode && row.itemCode?.toLowerCase() === form.componentCode.trim().toLowerCase()
  )
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
    <NvPageHeader title="BOM 分析" description="多级 BOM 树、滚算爆炸、反查与版本对比" />

    <NvSectionCards :columns="3">
      <NvSectionCard description="结果行" :value="resultCount" hint="来自后端 BOM facade" />
      <NvSectionCard description="警告" :value="warningCount" hint="缺失下级版本等诊断" />
      <NvSectionCard description="错误" :value="errorCount" hint="循环引用等阻断诊断" />
    </NvSectionCards>

    <form class="grid gap-4 rounded-md border bg-background p-4" @submit.prevent="submit">
      <div class="flex flex-wrap items-center gap-2" role="group" aria-label="分析视图">
        <NvButton
          v-for="option in analysisViews"
          :key="option.value"
          type="button"
          size="sm"
          :variant="form.view === option.value ? 'default' : 'outline'"
          @click="form.view = option.value"
        >
          {{ option.label }}
        </NvButton>
      </div>

      <div class="grid gap-3 md:grid-cols-[11rem_1fr_1fr_9rem]">
        <NvField>
          <NvFieldLabel for="bom-kind">BOM 类型</NvFieldLabel>
          <NvSelect id="bom-kind" v-model="form.kind">
            <NvSelectTrigger class="h-9"><NvSelectValue /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem
                v-for="option in kindOptions"
                :key="option.value"
                :value="option.value"
                >{{ option.label }}</NvSelectItem
              >
            </NvSelectContent>
          </NvSelect>
        </NvField>

        <NvField
          v-if="form.view !== 'where-used' && form.view !== 'diff'"
          :data-invalid="submitted && !form.rootCode.trim()"
        >
          <NvFieldLabel for="bom-root">{{ codeLabel }}</NvFieldLabel>
          <NvInput id="bom-root" v-model="form.rootCode" :placeholder="codePlaceholder" />
        </NvField>
        <NvField
          v-else-if="form.view === 'where-used'"
          :data-invalid="submitted && !form.componentCode.trim()"
        >
          <NvFieldLabel for="bom-component">组件物料</NvFieldLabel>
          <NvInput id="bom-component" v-model="form.componentCode" placeholder="如 RM-200" />
        </NvField>
        <NvField v-else>
          <NvFieldLabel>对比对象</NvFieldLabel>
          <NvInput :model-value="form.kind === 'engineering' ? 'EBOM' : 'MBOM'" disabled />
        </NvField>

        <NvField v-if="form.view !== 'diff'">
          <NvFieldLabel for="bom-effective">有效日期</NvFieldLabel>
          <NvDatePicker id="bom-effective" v-model="form.effectiveDate" />
        </NvField>

        <NvField v-if="form.view !== 'where-used' && form.view !== 'diff'">
          <NvFieldLabel for="bom-lot">批量</NvFieldLabel>
          <NvInput id="bom-lot" v-model="form.lotSize" type="number" min="0.000001" step="any" />
        </NvField>
      </div>

      <div v-if="form.view === 'diff'" class="grid gap-3 md:grid-cols-[1fr_8rem_1fr_8rem_auto]">
        <NvField :data-invalid="submitted && !form.fromBomCode.trim()">
          <NvFieldLabel for="bom-from-code">来源 BOM</NvFieldLabel>
          <NvInput id="bom-from-code" v-model="form.fromBomCode" placeholder="如 EBOM-FG" />
        </NvField>
        <NvField :data-invalid="submitted && !form.fromRevision.trim()">
          <NvFieldLabel for="bom-from-revision">来源修订</NvFieldLabel>
          <NvInput id="bom-from-revision" v-model="form.fromRevision" placeholder="A" />
        </NvField>
        <NvField :data-invalid="submitted && !form.toBomCode.trim()">
          <NvFieldLabel for="bom-to-code">目标 BOM</NvFieldLabel>
          <NvInput id="bom-to-code" v-model="form.toBomCode" placeholder="如 EBOM-FG" />
        </NvField>
        <NvField :data-invalid="submitted && !form.toRevision.trim()">
          <NvFieldLabel for="bom-to-revision">目标修订</NvFieldLabel>
          <NvInput id="bom-to-revision" v-model="form.toRevision" placeholder="B" />
        </NvField>
        <div class="flex items-end">
          <NvButton type="submit" :disabled="pending">
            <Spinner v-if="pending" aria-hidden="true" />
            <NetworkIcon v-else aria-hidden="true" />
            对比
          </NvButton>
        </div>
      </div>

      <div v-else-if="form.view !== 'where-used'" class="grid gap-3 md:grid-cols-[1fr_1fr_auto]">
        <NvField>
          <NvFieldLabel for="bom-code">指定 BOM</NvFieldLabel>
          <NvInput id="bom-code" v-model="form.bomCode" placeholder="留空自动选择" />
        </NvField>
        <NvField>
          <NvFieldLabel for="bom-revision">指定修订</NvFieldLabel>
          <NvInput id="bom-revision" v-model="form.revision" placeholder="留空自动选择" />
        </NvField>
        <div class="flex items-end">
          <NvButton type="submit" :disabled="pending">
            <Spinner v-if="pending" aria-hidden="true" />
            <NetworkIcon v-else aria-hidden="true" />
            分析
          </NvButton>
        </div>
      </div>
      <div v-else class="flex justify-end">
        <NvButton type="submit" :disabled="pending">
          <Spinner v-if="pending" aria-hidden="true" />
          <SearchIcon v-else aria-hidden="true" />
          反查
        </NvButton>
      </div>

      <p v-if="submitted && !canSubmit" class="text-sm text-destructive" role="alert">
        请填写当前视图需要的 BOM 版本或物料与有效日期。
      </p>
      <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
    </form>

    <NvToolbar v-if="form.view !== 'where-used'" search-placeholder="按物料、BOM 或路径筛选" />

    <NvDataTable
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
            <span class="inline-flex items-center gap-2">
              {{ row.itemCode || '无' }}
              <NvStatusBadge v-if="isContextNode(row)" label="追溯节点" tone="info" />
            </span>
            <span v-if="row.parentItemCode" class="text-xs text-muted-foreground"
              >上级 {{ row.parentItemCode }}</span
            >
          </div>
        </div>
      </template>
      <template #cell-requiredQuantity="{ row }">{{ formatQty(row.requiredQuantity) }}</template>
      <template #cell-bomCode="{ row }">
        {{ row.bomCode ? `${row.bomCode} / ${row.revision ?? '无'}` : '无' }}
      </template>
      <template #cell-flags="{ row }">
        <div class="flex flex-wrap gap-1">
          <NvStatusBadge v-for="flag in flagLabels(row)" :key="flag" :label="flag" tone="neutral" />
          <span v-if="!flagLabels(row).length" class="text-muted-foreground">无</span>
        </div>
      </template>
    </NvDataTable>

    <NvDataTable
      v-else-if="form.view === 'explosion'"
      :columns="explosionColumns"
      :rows="flattenedNodes"
      :row-key="(row) => row.key"
      :loading="pending"
      empty-message="当前条件没有 BOM 爆炸结果。"
    >
      <template #cell-itemCode="{ row }">
        <span
          class="inline-flex items-center gap-2"
          :style="{ paddingLeft: `${row.depth * 1.25}rem` }"
        >
          {{ row.itemCode || '无' }}
          <NvStatusBadge v-if="isContextNode(row)" label="追溯节点" tone="info" />
        </span>
      </template>
      <template #cell-lineQuantity="{ row }">{{ formatQty(row.lineQuantity) }}</template>
      <template #cell-requiredQuantity="{ row }">{{ formatQty(row.requiredQuantity) }}</template>
      <template #cell-yield="{ row }">
        {{ formatRate(row.scrapRate) }} / {{ formatRate(row.yieldRate) }}
      </template>
      <template #cell-alternates="{ row }">
        <div class="grid gap-0.5 text-sm">
          <span>{{ row.substituteSkuCodes || row.alternateGroup || '无' }}</span>
          <span v-if="row.referenceDesignators" class="text-xs text-muted-foreground">{{
            row.referenceDesignators
          }}</span>
        </div>
      </template>
    </NvDataTable>

    <NvDataTable
      v-else-if="form.view === 'diff'"
      :columns="diffColumns"
      :rows="diffRows"
      :row-key="(row) => `${row.changeType}:${row.oldItemCode ?? ''}:${row.newItemCode ?? ''}`"
      :loading="pending"
      empty-message="当前两个 BOM 版本没有结构差异。"
    >
      <template #cell-changeType="{ row }">
        <NvStatusBadge
          :label="diffChangeLabel(row.changeType)"
          :tone="diffChangeTone(row.changeType)"
        />
      </template>
      <template #cell-itemCode="{ row }">{{ diffItemLabel(row) }}</template>
      <template #cell-quantity="{ row }">
        {{ formatQuantityDiff(row) }}
      </template>
      <template #cell-uom="{ row }">
        {{ formatTextDiff(row.oldUnitOfMeasureCode, row.newUnitOfMeasureCode) }}
      </template>
      <template #cell-rates="{ row }">
        {{ formatRateDiff(row.oldScrapRate, row.newScrapRate) }} /
        {{ formatRateDiff(row.oldYieldRate, row.newYieldRate) }}
      </template>
      <template #cell-fields="{ row }">
        <div class="flex flex-wrap gap-1">
          <NvStatusBadge
            v-for="label in fieldChangeLabels(row)"
            :key="label"
            :label="label"
            tone="neutral"
          />
        </div>
      </template>
    </NvDataTable>

    <NvDataTable
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
          <NvStatusBadge v-for="flag in flagLabels(row)" :key="flag" :label="flag" tone="neutral" />
          <span v-if="!flagLabels(row).length" class="text-muted-foreground">无</span>
        </div>
      </template>
    </NvDataTable>

    <section v-if="diagnostics.length" class="grid gap-3">
      <h2 class="text-base font-semibold">诊断</h2>
      <NvDataTable
        :columns="diagnosticColumns"
        :rows="diagnostics"
        :row-key="(row) => `${row.severity}:${row.itemCode}:${row.path}:${row.code}`"
        :searchable="false"
        :column-settings="false"
        empty-message="没有诊断。"
      >
        <template #cell-severity="{ row }">
          <NvStatusBadge
            :label="diagnosticLabel(row.severity)"
            :tone="diagnosticTone(row.severity)"
          />
        </template>
      </NvDataTable>
    </section>
  </BusinessLayout>
</template>
