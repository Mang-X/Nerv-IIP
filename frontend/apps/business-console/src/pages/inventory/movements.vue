<script setup lang="ts">
import type {
  BusinessConsoleInventoryMovementLineResponse,
  BusinessConsolePostStockMovementRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import { INVENTORY_MOVEMENT_TYPE_LABELS, labelFor } from '@/data/businessLabels'
import { useInventoryMovement } from '@/composables/useBusinessInventory'
import { useInventoryScopeCatalog } from '@/composables/useInventoryScope'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useSkuNames } from '@/composables/useSkuNames'
import {
  useWarehouseCodeCatalog,
  WAREHOUSE_CATALOG_SOURCE_TEXT,
  WAREHOUSE_LOCATION_EMPTY_TEXT,
  WAREHOUSE_LOT_EMPTY_TEXT,
  WAREHOUSE_SERIAL_EMPTY_TEXT,
} from '@/composables/useWarehouseCodeCatalog'
import { useBusinessContextStore } from '@/stores/businessContext'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvEntityPicker,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
} from '@nerv-iip/ui'
import { SendIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '库存移动过账',
    requiredPermissions: ['business.inventory.movements.create'],
  },
})

const route = useRoute()
const businessContext = useBusinessContextStore()
const { movementRows, movementsPending, movementsTotal, postMovement, postMovementPending } =
  useInventoryMovement()
// 物料 / 工厂走主数据目录；库位/批次/序列号后端无读面，从既有台账与作业记录派生。
const { skuOptions, skusPending, siteOptions, sitesPending, resolveUomCode } =
  useInventoryScopeCatalog()
const { locationOptions, lotOptions, serialOptions, warehouseCatalogPending } =
  useWarehouseCodeCatalog()
/** 单位随物料的基本单位带出，不给手输：单位写错这笔移动就落不到正确台账。 */
function onSkuChange(skuCode: string) {
  form.skuCode = skuCode
  form.uomCode = skuCode ? resolveUomCode(skuCode) : ''
}

// 受控值：UI 说人话，下发仍是后端码值。
const QUALITY_OPTIONS = [
  { label: '可用', value: 'available' },
  { label: '待检', value: 'inspection' },
  { label: '冻结', value: 'blocked' },
  { label: '不合格', value: 'rejected' },
]

const form = reactive({
  movementType: 'receipt',
  sourceService: 'business-console',
  sourceDocumentId: '',
  sourceDocumentLineId: '',
  idempotencyKey: '',
  skuCode: '',
  uomCode: 'EA',
  siteCode: '',
  locationCode: '',
  lotNo: '',
  serialNo: '',
  qualityStatus: 'available',
  ownerType: 'owned',
  ownerId: '',
  quantity: '1',
})

// 移动类型码值 → 中文标签：界面说人话，下发仍是后端码值。
const MOVEMENT_TYPE_LABELS: Record<string, string> = {
  inbound: '入库',
  outbound: '出库',
  transfer: '移库',
  adjustment: '调整',
  'status-transfer-out': '状态转出',
  'status-transfer-in': '状态转入',
  'count-adjustment': '盘点调整',
  receipt: '入库',
  issue: '出库',
}

const movementSheetOpen = shallowRef(false)

// 上下文穿透：从来源单据（收货/完工入库/领料/盘点）带入 SKU/库位/批次。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
watch(
  () => route.query,
  (query) => {
    const source = firstQuery(query.sourceDocumentId) || firstQuery(query.workOrderId)
    if (source) form.sourceDocumentId = source
    const sku = firstQuery(query.skuCode) || firstQuery(query.skuId)
    if (sku) form.skuCode = sku
    const site = firstQuery(query.siteCode)
    if (site) form.siteCode = site
    const location = firstQuery(query.locationCode)
    if (location) form.locationCode = location
    const lot = firstQuery(query.lotNo) || firstQuery(query.materialLotId)
    if (lot) form.lotNo = lot
    const serial = firstQuery(query.serialNo)
    if (serial) form.serialNo = serial
  },
  { immediate: true },
)

const stableSubmissionKey = computed(() =>
  [
    form.movementType,
    form.sourceDocumentId,
    form.sourceDocumentLineId,
    form.skuCode,
    form.siteCode,
    form.locationCode,
    form.quantity,
  ]
    .map((part) => String(part || '').trim() || 'none')
    .join('|'),
)
const canSubmit = computed(
  () =>
    isNonEmpty(businessContext.organizationId) &&
    isNonEmpty(businessContext.environmentId) &&
    isNonEmpty(form.movementType) &&
    isNonEmpty(form.sourceDocumentId) &&
    isNonEmpty(form.skuCode) &&
    isNonEmpty(form.uomCode) &&
    isNonEmpty(form.siteCode) &&
    isNonEmpty(form.locationCode) &&
    toOptionalNumber(form.quantity) !== undefined,
)

// 读面只回编码，名称在主数据里，按编码 join 出中文名。
const { resolveSkuName } = useSkuNames()
const { resolveLocation } = useMasterDataDisplayNames({ locations: true })

/** 「名称 编码」串，供排序与导出用；名录查不到就只有编码，不编名字。 */
function skuText(code?: string | null, fallback = '未记录') {
  const name = resolveSkuName(code)
  return name ? `${name} ${code}` : (code ?? fallback)
}
/** 「工厂 / 库位」串：库位优先显中文名，查不到就只显编码。 */
function locationLabel(siteCode?: string | null, locationCode?: string | null) {
  const location = locationCode ? (resolveLocation(locationCode) ?? locationCode) : ''
  return [siteCode, location].filter(Boolean).join(' / ') || '—'
}

type MovementRow = BusinessConsoleInventoryMovementLineResponse
const columns: NvDataTableColumn<MovementRow>[] = [
  { key: 'sourceDocumentId', header: '来源单据', cellClass: 'font-medium' },
  {
    key: 'movementType',
    header: '类型',
    // 本页的 MOVEMENT_TYPE_LABELS 比共享词表更全（含状态转出入、盘点调整），用它。
    accessor: (r) =>
      r.movementType ? (MOVEMENT_TYPE_LABELS[r.movementType] ?? r.movementType) : '—',
  },
  { key: 'skuCode', header: '物料', accessor: (r) => skuText(r.skuCode) },
  {
    key: 'location',
    header: '库位',
    accessor: (r) => locationLabel(r.siteCode, r.locationCode),
  },
  { key: 'lotNo', header: '批次', accessor: (r) => r.lotNo || '—' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
  { key: 'postedAtUtc', header: '过账时间', accessor: (r) => formatDateTime(r.postedAtUtc) },
]

function formatQuantity(value: number | null | undefined) {
  if (value === null || value === undefined) return '—'
  return `${value > 0 ? '+' : ''}${new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value)}`
}
function formatDateTime(value: string | undefined) {
  if (!value) return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : new Intl.DateTimeFormat('zh-CN', { dateStyle: 'short', timeStyle: 'short' }).format(parsed)
}

async function submitMovement() {
  if (!canSubmit.value) return
  const body: BusinessConsolePostStockMovementRequest = {
    organizationId: businessContext.organizationId.trim(),
    environmentId: businessContext.environmentId.trim(),
    movementType: form.movementType,
    sourceService: form.sourceService.trim() || 'business-console',
    sourceDocumentId: form.sourceDocumentId.trim(),
    sourceDocumentLineId: optionalText(form.sourceDocumentLineId),
    idempotencyKey: optionalText(form.idempotencyKey) ?? `movement-${stableSubmissionKey.value}`,
    skuCode: form.skuCode.trim(),
    uomCode: form.uomCode.trim(),
    siteCode: form.siteCode.trim(),
    locationCode: form.locationCode.trim(),
    lotNo: optionalText(form.lotNo),
    serialNo: optionalText(form.serialNo),
    qualityStatus: form.qualityStatus.trim(),
    ownerType: form.ownerType.trim(),
    ownerId: optionalText(form.ownerId),
    quantity: toOptionalNumber(form.quantity),
  }
  let response
  try {
    response = await postMovement(body)
  } catch (error) {
    notifyError(error, '提交库存移动失败，请稍后重试。')
    return
  }
  // 列表来自服务端读面：过账成功后失效查询即可，新过账的流水刷新之后仍然在。
  movementSheetOpen.value = false
  notifySuccess(`来源单据 ${body.sourceDocumentId} 的库存移动已受理`)
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="库存移动过账"
      :breadcrumbs="[{ label: '库存' }]"
      :count="`${movementsTotal} 条库存流水`"
    >
      <template #actions>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`"
            >返回工单 {{ contextWorkOrderId }}</RouterLink
          >
        </NvButton>
        <NvButton size="sm" type="button" @click="movementSheetOpen = true">
          <SendIcon aria-hidden="true" />
          新建移动
        </NvButton>
      </template>
    </NvPageHeader>

    <NvDataTable
      :columns="columns"
      :rows="movementRows"
      :loading="movementsPending"
      row-key="movementId"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围内没有库存流水。库存移动一般由收货、完工入库、领料或盘点自动产生；确需补录时点右上角新建移动。"
    >
      <template #cell-skuCode="{ row }">
        <CodeWithNameCell :code="row.skuCode" :name="resolveSkuName(row.skuCode)" />
      </template>
      <template #cell-quantity="{ row }"
        ><span
          class="tabular-nums"
          :class="
            (row.quantity ?? 0) < 0 ? 'text-destructive' : 'text-emerald-600 dark:text-emerald-400'
          "
          >{{ formatQuantity(row.quantity) }}</span
        ></template
      >
    </NvDataTable>

    <NvDialog v-model:open="movementSheetOpen">
      <NvDialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>新建库存移动</NvDialogTitle>
          <!-- 界面上不再写说明书；仅供读屏播报对象范围。 -->
          <NvDialogDescription class="sr-only">人工补录一条库存移动过账。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitMovement">
          <NvFieldGroup class="grid gap-3 md:grid-cols-3">
            <NvField>
              <NvFieldLabel>移动类型</NvFieldLabel>
              <NvSelect v-model="form.movementType">
                <NvSelectTrigger aria-label="移动类型"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="receipt">入库</NvSelectItem>
                  <NvSelectItem value="issue">出库</NvSelectItem>
                  <NvSelectItem value="transfer">调拨</NvSelectItem>
                  <NvSelectItem value="adjustment">调整</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-source-document">来源单据</NvFieldLabel>
              <NvInput id="movement-source-document" v-model="form.sourceDocumentId" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-source-line">来源行</NvFieldLabel>
              <NvInput id="movement-source-line" v-model="form.sourceDocumentLineId" />
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-sku">物料</NvFieldLabel>
              <NvEntityPicker
                id="movement-sku"
                :model-value="form.skuCode"
                :options="skuOptions"
                title="选择物料"
                placeholder="选择物料"
                source-text="数据来自基础数据物料主数据"
                empty-text="暂无物料主数据，请先在基础数据维护物料"
                :loading="skusPending"
                clearable
                aria-label="物料"
                @update:model-value="onSkuChange"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-uom">单位</NvFieldLabel>
              <!-- 单位随物料的基本单位带出，不给手输：单位写错这笔移动就落不到正确台账。 -->
              <span
                id="movement-uom"
                class="inline-flex h-9 items-center rounded-md border border-input px-2.5 text-sm text-muted-foreground"
                >{{ form.uomCode || '选择物料后自动带出' }}</span
              >
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-site">工厂</NvFieldLabel>
              <NvEntityPicker
                id="movement-site"
                v-model="form.siteCode"
                :options="siteOptions"
                title="选择工厂"
                placeholder="选择工厂"
                source-text="数据来自基础数据工厂主数据"
                empty-text="暂无工厂主数据，请先在基础数据维护工厂"
                :loading="sitesPending"
                clearable
                aria-label="工厂"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-location">库位</NvFieldLabel>
              <NvEntityPicker
                id="movement-location"
                v-model="form.locationCode"
                :options="locationOptions"
                title="选择库位"
                placeholder="选择库位"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="库位"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-quantity">数量</NvFieldLabel>
              <NvInput
                id="movement-quantity"
                v-model="form.quantity"
                inputmode="decimal"
                required
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel>质量状态</NvFieldLabel>
              <NvSelect v-model="form.qualityStatus">
                <NvSelectTrigger aria-label="质量状态"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in QUALITY_OPTIONS" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-owner-id">货主</NvFieldLabel>
              <NvInput
                id="movement-owner-id"
                v-model="form.ownerId"
                placeholder="可选货主名称或编码"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-lot">批次</NvFieldLabel>
              <NvEntityPicker
                id="movement-lot"
                v-model="form.lotNo"
                :options="lotOptions"
                title="选择批次"
                placeholder="选择批次"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOT_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="批次"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="movement-serial">序列号</NvFieldLabel>
              <NvEntityPicker
                id="movement-serial"
                v-model="form.serialNo"
                :options="serialOptions"
                title="选择序列号"
                placeholder="选择序列号"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_SERIAL_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                aria-label="序列号"
              />
            </NvField>
          </NvFieldGroup>

          <div class="flex justify-end">
            <NvButton type="submit" :disabled="postMovementPending || !canSubmit">
              <Spinner v-if="postMovementPending" aria-hidden="true" />
              <SendIcon v-else aria-hidden="true" />
              提交库存移动
            </NvButton>
          </div>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
