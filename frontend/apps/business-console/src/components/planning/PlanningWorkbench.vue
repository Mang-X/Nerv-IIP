<script setup lang="ts">
import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleMrpPeggingItem,
  BusinessConsoleMrpRunItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn, StatusTone } from '@nerv-iip/ui'
import { useBusinessSkus, useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { useBusinessPlanning } from '@/composables/useBusinessPlanning'
import {
  Button,
  DataTable,
  DatePicker,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@nerv-iip/ui'
import { PlayIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'

const {
  acceptSuggestion,
  acceptSuggestionError,
  acceptSuggestionPending,
  createDemandError,
  createDemandPending,
  createOrUpdateDemand,
  demandForm,
  demands,
  demandsError,
  demandsPending,
  mrpRuns,
  mrpRunsError,
  mrpRunsPending,
  pegging,
  peggingPending,
  refreshPlanning,
  runMrp,
  runMrpError,
  runMrpPending,
  runRequest,
  runSelection,
  suggestionFilters,
  suggestions,
  suggestionsError,
  suggestionsPending,
} = useBusinessPlanning()

// 主数据：SKU / 工厂 / 计量单位（Select 显名称、绑定编码，码→名解析复用）。
const { skus } = useBusinessSkus()
const { resources: sites } = useBusinessMasterDataResources('site')
const { resources: units } = useBusinessMasterDataResources('unit-of-measure')

const skuNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const sku of skus.value) {
    if (sku.code) map.set(sku.code, sku.displayName ?? sku.code)
  }
  return map
})
const siteNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const site of sites.value) {
    if (site.code) map.set(site.code, site.displayName ?? site.code)
  }
  return map
})
function skuLabel(code?: string | null) {
  if (!code) return '—'
  return skuNameByCode.value.get(code) ?? code
}
function siteLabel(code?: string | null) {
  if (!code) return '—'
  return siteNameByCode.value.get(code) ?? code
}

const skuOptions = computed(() =>
  skus.value
    .filter((s) => s.code)
    .map((s) => ({ value: s.code as string, label: `${s.displayName ?? s.code} · ${s.code}` })),
)
const siteOptions = computed(() =>
  sites.value
    .filter((s) => s.code)
    .map((s) => ({ value: s.code as string, label: `${s.displayName ?? s.code} · ${s.code}` })),
)
const uomOptions = computed(() =>
  units.value
    .filter((u) => u.code)
    .map((u) => ({ value: u.code as string, label: `${u.displayName ?? u.code} · ${u.code}` })),
)

const canSubmitDemand = computed(() =>
  !!demandForm.skuCode?.trim()
  && !!demandForm.siteCode?.trim()
  && !!demandForm.uomCode?.trim()
  && (demandForm.quantity ?? 0) > 0,
)

const errorMessage = computed(() =>
  [demandsError, mrpRunsError, suggestionsError, createDemandError, runMrpError, acceptSuggestionError]
    .map((ref) => formatError(ref.value)).find(Boolean) ?? '',
)
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

const demandOpen = shallowRef(false)
const mrpOpen = shallowRef(false)

const demandTypeOptions = [
  { label: '销售订单', value: 'sales-order' },
  { label: '预测', value: 'forecast' },
  { label: '安全库存', value: 'safety-stock' },
]
const suggestionStatusOptions = [
  { label: '待评审', value: 'open' },
  { label: '已接受', value: 'accepted' },
]

const demandQuantity = computed(() => demands.value.reduce((sum, item) => sum + (item.quantity ?? 0), 0))
const proposedWorkOrders = computed(() => suggestions.value.filter((i) => i.suggestionType === 'planned-work-order' && isOpen(i.status)).length)
const proposedPurchases = computed(() => suggestions.value.filter((i) => i.suggestionType === 'planned-purchase' && isOpen(i.status)).length)

const demandColumns: DataTableColumn<BusinessConsoleDemandSourceItem>[] = [
  { key: 'sourceReference', header: '来源', cellClass: 'font-medium' },
  { key: 'demandType', header: '类型', width: 'w-24' },
  { key: 'skuCode', header: 'SKU' },
  { key: 'siteCode', header: '工厂' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
  { key: 'dueDate', header: '日期', width: 'w-28' },
]
const runColumns: DataTableColumn<BusinessConsoleMrpRunItem>[] = [
  // runId 是 GUID，不显裸 GUID；以「计划范围」(horizon) 作人读锚点，追溯按钮内部用 runId。
  { key: 'horizon', header: '计划范围', cellClass: 'font-medium' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'suggestionCount', header: '建议', align: 'end', width: 'w-20' },
  { key: 'productionEngineeringSnapshotSource', header: '工程快照' },
  { key: 'inventorySnapshotSource', header: '库存快照' },
  { key: 'actions', header: '', align: 'end', width: 'w-12' },
]
const peggingColumns: DataTableColumn<BusinessConsoleMrpPeggingItem>[] = [
  { key: 'demandSourceReference', header: '需求来源', cellClass: 'font-medium' },
  { key: 'parentSkuCode', header: '父项' },
  { key: 'componentSkuCode', header: '组件' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-24' },
  { key: 'engineeringRef', header: '工程引用' },
]
const suggestionColumns: DataTableColumn<BusinessConsolePlanningSuggestionItem>[] = [
  // suggestionId 是 GUID 且无人读号；不显裸 GUID，行由「类型 + SKU + 数量 + 原因」自识别。
  { key: 'suggestionType', header: '类型', width: 'w-28', cellClass: 'font-medium' },
  { key: 'skuCode', header: 'SKU' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
  { key: 'requiredDate', header: '需求日', width: 'w-28' },
  { key: 'reasonCode', header: '原因' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

async function submitDemand() {
  await createOrUpdateDemand()
  demandOpen.value = false
}
async function submitMrpRun() {
  await runMrp()
  mrpOpen.value = false
}
async function acceptPlanningSuggestion(suggestionId?: string, suggestionType?: string) {
  if (!suggestionId) return
  const isWorkOrder = suggestionType === 'planned-work-order'
  await acceptSuggestion(suggestionId, {
    downstreamService: isWorkOrder ? 'MES' : 'ERP',
    downstreamDocumentType: isWorkOrder ? 'planned-work-order' : 'planned-purchase-order',
    downstreamDocumentId: `${isWorkOrder ? 'WO-PLAN' : 'PO-PLAN'}-${suggestionId}`,
  })
}

function planningStatus(status?: string | null): { label: string, tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'accepted' || s === 'completed') return { label: s === 'accepted' ? '已接受' : '已完成', tone: 'success' }
  if (s === 'running' || s === 'inprogress') return { label: '运行中', tone: 'info' }
  if (s === 'failed') return { label: '失败', tone: 'danger' }
  if (s === 'open' || s === 'pending') return { label: '待评审', tone: 'warning' }
  return { label: status || '未知', tone: 'neutral' }
}
function demandTypeLabel(value?: string | null) {
  return ({ 'forecast': '预测', 'safety-stock': '安全库存', 'sales-order': '销售订单' } as Record<string, string>)[value ?? ''] ?? (value || '未指定')
}
function suggestionTypeLabel(value?: string | null) {
  return ({ 'planned-purchase': '采购建议', 'planned-work-order': '生产建议' } as Record<string, string>)[value ?? ''] ?? (value || '未指定')
}
function reasonLabel(value?: string | null) {
  const map: Record<string, string> = {
    'inventory_shortage': '库存不足',
    'material_shortage': '物料不足',
    'demand_pegging': '需求驱动',
    'safety_stock': '安全库存',
    'finished-good-net-requirement': '成品净需求',
    'component-net-requirement': '组件净需求',
    'safety-stock-replenishment': '安全库存补充',
  }
  // 未知码一律降级为通用中文，绝不回显原始英文码。
  return map[value ?? ''] ?? '按计划规则形成'
}
function isOpen(status?: string | null) {
  return status?.toLowerCase() === 'open'
}
function formatDate(value?: string | null) {
  return value ? value.slice(0, 10) : '-'
}
function formatQuantity(value?: number | null, uom?: string | null) {
  return `${value ?? 0} ${uom ?? ''}`.trim()
}
function formatSource(value?: string | null) {
  return value && value.length > 0 ? value : '未采集'
}
</script>

<template>
  <PageHeader title="需求与计划" :breadcrumbs="[{ label: '需求与计划' }]">
    <template #actions>
      <Button size="sm" type="button" variant="outline" :disabled="demandsPending" @click="refreshPlanning">
        <RefreshCwIcon aria-hidden="true" />
        刷新
      </Button>

      <Dialog v-model:open="mrpOpen">
        <DialogTrigger as-child>
          <Button size="sm" type="button" variant="outline">
            <PlayIcon aria-hidden="true" />
            运行 MRP
          </Button>
        </DialogTrigger>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>运行 MRP</DialogTitle>
            <DialogDescription>按计划周期对当前需求池运行物料需求计划，生成生产与采购建议。</DialogDescription>
          </DialogHeader>
          <form class="grid gap-4" @submit.prevent="submitMrpRun">
            <FieldGroup class="grid gap-3 sm:grid-cols-2">
              <Field>
                <FieldLabel>开始日期</FieldLabel>
                <DatePicker v-model="runRequest.horizonStart" placeholder="选择开始日期" class="w-full" />
              </Field>
              <Field>
                <FieldLabel>结束日期</FieldLabel>
                <DatePicker v-model="runRequest.horizonEnd" placeholder="选择结束日期" class="w-full" />
              </Field>
            </FieldGroup>
            <DialogFooter>
              <Button type="button" variant="outline" @click="mrpOpen = false">取消</Button>
              <Button type="submit" :disabled="runMrpPending">
                <Spinner v-if="runMrpPending" aria-hidden="true" />
                运行
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      <Dialog v-model:open="demandOpen">
        <DialogTrigger as-child>
          <Button size="sm" type="button">
            <PlusIcon aria-hidden="true" />
            新建需求
          </Button>
        </DialogTrigger>
        <DialogContent class="sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>新建需求</DialogTitle>
            <DialogDescription>录入销售订单、预测或安全库存需求，作为 MRP 与计划建议的来源。</DialogDescription>
          </DialogHeader>
          <form class="grid gap-4" @submit.prevent="submitDemand">
            <FieldGroup class="grid gap-3 sm:grid-cols-2">
              <Field>
                <FieldLabel>需求类型</FieldLabel>
                <Select v-model="demandForm.demandType">
                  <SelectTrigger aria-label="需求类型"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem v-for="o in demandTypeOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              <Field>
                <FieldLabel for="demand-source">来源单号</FieldLabel>
                <Input id="demand-source" v-model="demandForm.sourceReference" placeholder="SO-2026-001" />
              </Field>
              <Field>
                <FieldLabel for="demand-sku">SKU</FieldLabel>
                <Select v-model="demandForm.skuCode">
                  <SelectTrigger id="demand-sku"><SelectValue placeholder="选择 SKU" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              <Field>
                <FieldLabel for="demand-site">工厂</FieldLabel>
                <Select v-model="demandForm.siteCode">
                  <SelectTrigger id="demand-site"><SelectValue placeholder="选择工厂" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem v-for="o in siteOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              <Field>
                <FieldLabel for="demand-uom">单位</FieldLabel>
                <Select v-model="demandForm.uomCode">
                  <SelectTrigger id="demand-uom"><SelectValue placeholder="选择单位" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              <Field>
                <FieldLabel for="demand-qty">数量</FieldLabel>
                <Input id="demand-qty" v-model.number="demandForm.quantity" min="0.0001" step="0.0001" type="number" />
              </Field>
              <Field>
                <FieldLabel>需求日期</FieldLabel>
                <DatePicker v-model="demandForm.dueDate" placeholder="选择需求日期" class="w-full" />
              </Field>
            </FieldGroup>
            <DialogFooter>
              <Button type="button" variant="outline" @click="demandOpen = false">取消</Button>
              <Button type="submit" :disabled="createDemandPending || !canSubmitDemand">
                <Spinner v-if="createDemandPending" aria-hidden="true" />
                保存需求
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </template>
  </PageHeader>

  <SectionCards :columns="3">
    <SectionCard description="需求总量" :value="demandQuantity" hint="当前计划范围" />
    <SectionCard description="生产建议" :value="proposedWorkOrders" hint="待评审" />
    <SectionCard description="采购建议" :value="proposedPurchases" hint="待评审" />
  </SectionCards>

  <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

  <Tabs default-value="demands">
    <TabsList>
      <TabsTrigger value="demands">需求池 ({{ demands.length }})</TabsTrigger>
      <TabsTrigger value="runs">MRP 运行 ({{ mrpRuns.length }})</TabsTrigger>
      <TabsTrigger value="suggestions">计划建议 ({{ suggestions.length }})</TabsTrigger>
    </TabsList>

    <TabsContent value="demands">
      <DataTable :columns="demandColumns" :rows="demands" row-key="demandSourceId" :loading="demandsPending" empty-message="当前范围没有计划需求。">
        <template #cell-demandType="{ row }">{{ demandTypeLabel(row.demandType) }}</template>
        <template #cell-skuCode="{ row }">
          <div class="flex flex-col gap-0.5">
            <span>{{ skuLabel(row.skuCode) }}</span>
            <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
          </div>
        </template>
        <template #cell-siteCode="{ row }">
          <div class="flex flex-col gap-0.5">
            <span>{{ siteLabel(row.siteCode) }}</span>
            <span v-if="row.siteCode" class="text-xs text-muted-foreground">{{ row.siteCode }}</span>
          </div>
        </template>
        <template #cell-quantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.quantity, row.uomCode) }}</span></template>
        <template #cell-dueDate="{ row }">{{ formatDate(row.dueDate) }}</template>
      </DataTable>
    </TabsContent>

    <TabsContent value="runs" class="grid gap-4">
      <DataTable :columns="runColumns" :rows="mrpRuns" row-key="runId" :loading="mrpRunsPending" empty-message="尚未运行 MRP。">
        <template #cell-horizon="{ row }">{{ formatDate(row.horizonStart) }} ~ {{ formatDate(row.horizonEnd) }}</template>
        <template #cell-status="{ row }"><StatusBadge :label="planningStatus(row.status).label" :tone="planningStatus(row.status).tone" /></template>
        <template #cell-suggestionCount="{ row }"><span class="tabular-nums">{{ row.suggestionCount ?? 0 }}</span></template>
        <template #cell-productionEngineeringSnapshotSource="{ row }">{{ formatSource(row.productionEngineeringSnapshotSource) }}</template>
        <template #cell-inventorySnapshotSource="{ row }">{{ formatSource(row.inventorySnapshotSource) }}</template>
        <template #cell-actions="{ row }">
          <Button size="sm" type="button" variant="ghost" @click="runSelection.runId = row.runId ?? ''">查看追溯</Button>
        </template>
      </DataTable>

      <div class="grid gap-2">
        <div class="flex items-center gap-2">
          <span class="text-sm font-medium text-foreground">需求追溯</span>
          <span class="text-sm text-muted-foreground">{{ runSelection.runId ? `批次 ${runSelection.runId}` : '选择一次运行' }}</span>
        </div>
        <DataTable
          :columns="peggingColumns"
          :rows="pegging"
          :row-key="(r) => `${r.suggestionId}:${r.componentSkuCode}`"
          :loading="peggingPending"
          empty-message="选择一条 MRP 运行查看需求与物料来源。"
        >
          <template #cell-parentSkuCode="{ row }">
            <div class="flex flex-col gap-0.5">
              <span>{{ skuLabel(row.parentSkuCode) }}</span>
              <span v-if="row.parentSkuCode" class="text-xs text-muted-foreground">{{ row.parentSkuCode }}</span>
            </div>
          </template>
          <template #cell-componentSkuCode="{ row }">
            <div class="flex flex-col gap-0.5">
              <span>{{ skuLabel(row.componentSkuCode) }}</span>
              <span v-if="row.componentSkuCode" class="text-xs text-muted-foreground">{{ row.componentSkuCode }}</span>
            </div>
          </template>
          <template #cell-quantity="{ row }"><span class="tabular-nums">{{ row.quantity ?? 0 }}</span></template>
          <template #cell-engineeringRef="{ row }">{{ row.manufacturingBomReference ?? row.productionVersionReference ?? '-' }}</template>
        </DataTable>
      </div>
    </TabsContent>

    <TabsContent value="suggestions" class="grid gap-3">
      <div class="flex justify-end">
        <Select v-model="suggestionFilters.status">
          <SelectTrigger class="h-9 w-32" aria-label="建议状态"><SelectValue placeholder="建议状态" /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="o in suggestionStatusOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <DataTable :columns="suggestionColumns" :rows="suggestions" row-key="suggestionId" :loading="suggestionsPending" empty-message="当前范围没有计划建议。">
        <template #cell-suggestionType="{ row }">{{ suggestionTypeLabel(row.suggestionType) }}</template>
        <template #cell-skuCode="{ row }">
          <div class="flex flex-col gap-0.5">
            <span>{{ skuLabel(row.skuCode) }}</span>
            <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
          </div>
        </template>
        <template #cell-quantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.quantity, row.uomCode) }}</span></template>
        <template #cell-requiredDate="{ row }">{{ formatDate(row.requiredDate) }}</template>
        <template #cell-reasonCode="{ row }">{{ reasonLabel(row.reasonCode) }}</template>
        <template #cell-status="{ row }"><StatusBadge :label="planningStatus(row.status).label" :tone="planningStatus(row.status).tone" /></template>
        <template #cell-actions="{ row }">
          <Button
            size="sm"
            type="button"
            variant="outline"
            :disabled="acceptSuggestionPending || planningStatus(row.status).label === '已接受'"
            @click="acceptPlanningSuggestion(row.suggestionId, row.suggestionType)"
          >
            接受
          </Button>
        </template>
      </DataTable>
    </TabsContent>
  </Tabs>
</template>
