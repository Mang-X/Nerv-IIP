<script setup lang="ts">
import BusinessContextBar from '@/components/business/BusinessContextBar.vue'
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessStatusBadge from '@/components/business/BusinessStatusBadge.vue'
import { useBusinessPlanning } from '@/composables/useBusinessPlanning'
import {
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { BoxesIcon, ClipboardListIcon, LinkIcon, PlayIcon, RefreshCwIcon, RouteIcon } from 'lucide-vue-next'
import { computed, reactive } from 'vue'

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
  filters,
  mrpRuns,
  mrpRunsError,
  mrpRunsPending,
  pegging,
  peggingError,
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
  syncContext,
} = useBusinessPlanning()

const acceptTarget = reactive({
  downstreamDocumentId: '',
})

const demandTypeOptions = [
  { label: '销售订单', value: 'sales-order' },
  { label: '预测', value: 'forecast' },
  { label: '安全库存', value: 'safety-stock' },
]
const suggestionStatusOptions = [
  { label: '待评审', value: 'open' },
  { label: '已接受', value: 'accepted' },
]

const loading = computed(
  () =>
    demandsPending.value ||
    mrpRunsPending.value ||
    suggestionsPending.value ||
    peggingPending.value ||
    createDemandPending.value ||
    runMrpPending.value ||
    acceptSuggestionPending.value,
)
const errorMessage = computed(
  () =>
    formatError(demandsError.value) ||
    formatError(mrpRunsError.value) ||
    formatError(suggestionsError.value) ||
    formatError(peggingError.value) ||
    formatError(createDemandError.value) ||
    formatError(runMrpError.value) ||
    formatError(acceptSuggestionError.value),
)
const demandQuantity = computed(() => demands.value.reduce((sum, item) => sum + (item.quantity ?? 0), 0))
const proposedWorkOrders = computed(
  () => suggestions.value.filter((item) => item.suggestionType === 'planned-work-order' && isOpenSuggestion(item.status)).length,
)
const proposedPurchases = computed(
  () => suggestions.value.filter((item) => item.suggestionType === 'planned-purchase' && isOpenSuggestion(item.status)).length,
)

function onContextChanged() {
  syncContext()
}

async function submitDemand() {
  await createOrUpdateDemand()
}

async function submitMrpRun() {
  await runMrp()
}

async function acceptPlanningSuggestion(suggestionId: string | undefined, suggestionType: string | undefined) {
  if (!suggestionId) return

  const isWorkOrder = suggestionType === 'planned-work-order'
  await acceptSuggestion(suggestionId, {
    downstreamService: isWorkOrder ? 'MES' : 'ERP',
    downstreamDocumentType: isWorkOrder ? 'planned-work-order' : 'planned-purchase-order',
    downstreamDocumentId: acceptTarget.downstreamDocumentId || `${isWorkOrder ? 'WO-PLAN' : 'PO-PLAN'}-${suggestionId}`,
  })
}

function selectRun(runId: string | undefined) {
  if (!runId) return
  runSelection.runId = runId
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

function isAcceptedSuggestion(status?: string | null) {
  return status?.toLowerCase() === 'accepted'
}

function isOpenSuggestion(status?: string | null) {
  return status?.toLowerCase() === 'open'
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <section class="grid gap-4">
    <BusinessPageHeader
      domain="计划"
      title="需求与 MRP"
      summary="把销售订单、预测和安全库存需求转成可追溯的生产与采购计划建议。"
    >
      <template #actions>
        <Button
          size="sm"
          type="button"
          variant="outline"
          :disabled="loading"
          @click="refreshPlanning"
        >
          <RefreshCwIcon data-icon="inline-start" />
          刷新
        </Button>
      </template>
    </BusinessPageHeader>

    <BusinessContextBar
      v-model:environment-id="filters.environmentId"
      v-model:organization-id="filters.organizationId"
      :show-line="false"
      :show-shift="false"
      :show-site="false"
      :show-work-center="false"
      title="计划范围"
      @update:environment-id="onContextChanged"
      @update:organization-id="onContextChanged"
    >
      <BusinessFormStatus :error="errorMessage" />
    </BusinessContextBar>

    <div class="grid gap-3 sm:grid-cols-3">
      <BusinessMetricCell label="需求总量" :value="demandQuantity" detail="当前组织环境" />
      <BusinessMetricCell label="生产建议" :value="proposedWorkOrders" detail="待评审" />
      <BusinessMetricCell label="采购建议" :value="proposedPurchases" detail="待评审" />
    </div>

    <div class="grid gap-4 xl:grid-cols-[minmax(320px,0.9fr)_minmax(0,1.1fr)]">
      <form class="rounded-lg border bg-background" @submit.prevent="submitDemand">
        <div class="flex items-center gap-2 border-b px-4 py-3">
          <ClipboardListIcon class="size-4 text-muted-foreground" />
          <h2 class="text-sm font-semibold text-foreground">需求录入</h2>
        </div>
        <FieldGroup class="grid gap-3 p-4 sm:grid-cols-2">
          <Field>
            <FieldLabel>需求类型</FieldLabel>
            <Select v-model="demandForm.demandType">
              <SelectTrigger aria-label="需求类型">
                <SelectValue placeholder="需求类型" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in demandTypeOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="planning-source">来源单号</FieldLabel>
            <Input id="planning-source" v-model="demandForm.sourceReference" placeholder="SO-2026-001" />
          </Field>
          <Field>
            <FieldLabel for="planning-sku">SKU</FieldLabel>
            <Input id="planning-sku" v-model="demandForm.skuCode" />
          </Field>
          <Field>
            <FieldLabel for="planning-site">工厂</FieldLabel>
            <Input id="planning-site" v-model="demandForm.siteCode" />
          </Field>
          <Field>
            <FieldLabel for="planning-uom">单位</FieldLabel>
            <Input id="planning-uom" v-model="demandForm.uomCode" />
          </Field>
          <Field>
            <FieldLabel for="planning-quantity">数量</FieldLabel>
            <Input id="planning-quantity" v-model.number="demandForm.quantity" min="0.0001" step="0.0001" type="number" />
          </Field>
          <Field>
            <FieldLabel for="planning-due">需求日期</FieldLabel>
            <Input id="planning-due" v-model="demandForm.dueDate" type="date" />
          </Field>
          <Field>
            <FieldLabel for="planning-idempotency">幂等键</FieldLabel>
            <Input id="planning-idempotency" v-model="demandForm.idempotencyKey" placeholder="可选" />
          </Field>
        </FieldGroup>
        <div class="flex justify-end border-t px-4 py-3">
          <Button size="sm" type="submit" :disabled="createDemandPending">
            <BoxesIcon data-icon="inline-start" />
            保存需求
          </Button>
        </div>
      </form>

      <form class="rounded-lg border bg-background" @submit.prevent="submitMrpRun">
        <div class="flex items-center gap-2 border-b px-4 py-3">
          <RouteIcon class="size-4 text-muted-foreground" />
          <h2 class="text-sm font-semibold text-foreground">MRP 运行</h2>
        </div>
        <FieldGroup class="grid gap-3 p-4 sm:grid-cols-2">
          <Field>
            <FieldLabel for="planning-horizon-start">开始日期</FieldLabel>
            <Input id="planning-horizon-start" v-model="runRequest.horizonStart" type="date" />
          </Field>
          <Field>
            <FieldLabel for="planning-horizon-end">结束日期</FieldLabel>
            <Input id="planning-horizon-end" v-model="runRequest.horizonEnd" type="date" />
          </Field>
          <Field>
            <FieldLabel>建议状态</FieldLabel>
            <Select v-model="suggestionFilters.status">
              <SelectTrigger aria-label="建议状态">
                <SelectValue placeholder="建议状态" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="option in suggestionStatusOptions" :key="option.value" :value="option.value">
                  {{ option.label }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field>
            <FieldLabel for="planning-selected-run">Pegging Run</FieldLabel>
            <Input id="planning-selected-run" v-model="runSelection.runId" placeholder="选择一次 MRP 运行" />
          </Field>
        </FieldGroup>
        <div class="flex justify-end border-t px-4 py-3">
          <Button size="sm" type="submit" :disabled="runMrpPending">
            <PlayIcon data-icon="inline-start" />
            运行 MRP
          </Button>
        </div>
      </form>
    </div>

    <div class="overflow-hidden rounded-lg border bg-background">
      <div class="flex items-center justify-between border-b px-4 py-3">
        <h2 class="text-sm font-semibold text-foreground">需求池</h2>
        <span class="text-sm text-muted-foreground">{{ demands.length }} 条</span>
      </div>
      <div class="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>来源</TableHead>
              <TableHead>类型</TableHead>
              <TableHead>SKU</TableHead>
              <TableHead>工厂</TableHead>
              <TableHead>数量</TableHead>
              <TableHead>日期</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="item in demands" :key="item.demandSourceId">
              <TableCell class="font-medium">{{ item.sourceReference }}</TableCell>
              <TableCell>{{ item.demandType }}</TableCell>
              <TableCell>{{ item.skuCode }}</TableCell>
              <TableCell>{{ item.siteCode }}</TableCell>
              <TableCell>{{ formatQuantity(item.quantity, item.uomCode) }}</TableCell>
              <TableCell>{{ formatDate(item.dueDate) }}</TableCell>
            </TableRow>
            <TableEmpty v-if="!demands.length && !demandsPending" :colspan="6">
              当前范围没有计划需求。
            </TableEmpty>
            <TableEmpty v-if="demandsPending" :colspan="6">正在加载需求...</TableEmpty>
          </TableBody>
        </Table>
      </div>
    </div>

    <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">MRP 运行记录</h2>
          <span class="text-sm text-muted-foreground">{{ mrpRuns.length }} 条</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Run</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>建议</TableHead>
                <TableHead>工程快照</TableHead>
                <TableHead>库存快照</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="item in mrpRuns"
                :key="item.runId"
                class="cursor-pointer"
                @click="selectRun(item.runId)"
              >
                <TableCell class="font-medium">{{ item.runId }}</TableCell>
                <TableCell><BusinessStatusBadge :value="item.status" /></TableCell>
                <TableCell>{{ item.suggestionCount ?? 0 }}</TableCell>
                <TableCell>{{ formatSource(item.productionEngineeringSnapshotSource) }}</TableCell>
                <TableCell>{{ formatSource(item.inventorySnapshotSource) }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!mrpRuns.length && !mrpRunsPending" :colspan="5">
                尚未运行 MRP。
              </TableEmpty>
              <TableEmpty v-if="mrpRunsPending" :colspan="5">正在加载 MRP 运行...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center gap-2 border-b px-4 py-3">
          <LinkIcon class="size-4 text-muted-foreground" />
          <h2 class="text-sm font-semibold text-foreground">Pegging 追溯</h2>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>需求来源</TableHead>
                <TableHead>父项</TableHead>
                <TableHead>组件</TableHead>
                <TableHead>数量</TableHead>
                <TableHead>工程引用</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="item in pegging" :key="`${item.suggestionId}:${item.componentSkuCode}`">
                <TableCell class="font-medium">{{ item.demandSourceReference }}</TableCell>
                <TableCell>{{ item.parentSkuCode }}</TableCell>
                <TableCell>{{ item.componentSkuCode ?? '-' }}</TableCell>
                <TableCell>{{ item.quantity ?? 0 }}</TableCell>
                <TableCell>{{ item.manufacturingBomReference ?? item.productionVersionReference ?? '-' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!pegging.length && !peggingPending" :colspan="5">
                选择一条 MRP 运行查看 pegging。
              </TableEmpty>
              <TableEmpty v-if="peggingPending" :colspan="5">正在加载 pegging...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </div>

    <div class="overflow-hidden rounded-lg border bg-background">
      <div class="grid gap-3 border-b px-4 py-3 lg:grid-cols-[1fr_260px]">
        <div>
          <h2 class="text-sm font-semibold text-foreground">计划建议评审</h2>
          <p class="mt-1 text-sm text-muted-foreground">生产建议接受到 MES，采购建议接受到 ERP。</p>
        </div>
        <Field>
          <FieldLabel for="planning-downstream-id">下游单据号</FieldLabel>
          <Input id="planning-downstream-id" v-model="acceptTarget.downstreamDocumentId" placeholder="可选" />
        </Field>
      </div>
      <div class="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>建议</TableHead>
              <TableHead>类型</TableHead>
              <TableHead>SKU</TableHead>
              <TableHead>数量</TableHead>
              <TableHead>需求日</TableHead>
              <TableHead>原因</TableHead>
              <TableHead>状态</TableHead>
              <TableHead>操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="item in suggestions" :key="item.suggestionId">
              <TableCell class="font-medium">{{ item.suggestionId }}</TableCell>
              <TableCell>{{ item.suggestionType }}</TableCell>
              <TableCell>{{ item.skuCode }}</TableCell>
              <TableCell>{{ formatQuantity(item.quantity, item.uomCode) }}</TableCell>
              <TableCell>{{ formatDate(item.requiredDate) }}</TableCell>
              <TableCell>{{ item.reasonCode }}</TableCell>
              <TableCell><BusinessStatusBadge :value="item.status" /></TableCell>
              <TableCell>
                <Button
                  size="sm"
                  type="button"
                  variant="outline"
                  :disabled="acceptSuggestionPending || isAcceptedSuggestion(item.status)"
                  @click="acceptPlanningSuggestion(item.suggestionId, item.suggestionType)"
                >
                  接受
                </Button>
              </TableCell>
            </TableRow>
            <TableEmpty v-if="!suggestions.length && !suggestionsPending" :colspan="8">
              当前范围没有计划建议。
            </TableEmpty>
            <TableEmpty v-if="suggestionsPending" :colspan="8">正在加载计划建议...</TableEmpty>
          </TableBody>
        </Table>
      </div>
    </div>
  </section>
</template>
