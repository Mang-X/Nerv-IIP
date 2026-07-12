<script setup lang="ts">
import type {
  BusinessConsoleCreateMaintenanceSparePartRequest,
  BusinessConsoleMaintenanceSparePartItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMaintenanceSpareParts } from '@/composables/useBusinessMaintenance'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  Spinner,
  toast,
} from '@nerv-iip/ui'
import { PackageSearchIcon, PlusIcon, RefreshCwIcon, WrenchIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '备件需求',
    requiredPermissions: ['business.maintenance.work-orders.read'],
  },
})

const {
  filters,
  spareParts,
  sparePartsError,
  sparePartsPending,
  sparePartsTotal,
  refreshSpareParts,
  createSparePart,
  createSparePartPending,
  createSparePartError,
} = useMaintenanceSpareParts()
const { page, pageSize } = usePagedList(filters)

const createOpen = shallowRef(false)
const createForm = reactive({
  workOrderId: '',
  skuCode: '',
  quantity: '1',
  uomCode: 'EA',
})
const createError = shallowRef('')

const listErrorMessage = computed(() => formatError(sparePartsError.value))
const createErrorMessage = computed(
  () => createError.value || formatError(createSparePartError.value),
)

type SparePartRow = BusinessConsoleMaintenanceSparePartItem
const columns: NvDataTableColumn<SparePartRow>[] = [
  {
    key: 'sparePartLineId',
    header: '备件需求',
    cellClass: 'font-medium',
    accessor: (r) => sparePartNo(r),
  },
  { key: 'workOrderId', header: '维修工单', accessor: (r) => r.workOrderId ?? '未关联' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '未记录' },
  { key: 'skuCode', header: '备件物料', accessor: (r) => r.skuCode ?? '未记录' },
  { key: 'quantity', header: '需求数量', align: 'end', accessor: (r) => quantityLabel(r) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function sparePartNo(row: SparePartRow) {
  const id = row.sparePartLineId ?? ''
  return id ? `SP-${id.slice(-8).toUpperCase()}` : '备件需求'
}
function rowKey(row: SparePartRow) {
  return row.sparePartLineId ?? `${row.workOrderId ?? ''}-${row.skuCode ?? ''}`
}
function quantityLabel(row: SparePartRow) {
  const quantity = row.quantity ?? 0
  return `${quantity} ${row.uomCode ?? ''}`.trim()
}

function openCreate() {
  createForm.workOrderId = ''
  createForm.skuCode = ''
  createForm.quantity = '1'
  createForm.uomCode = 'EA'
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.workOrderId.trim() || !createForm.skuCode.trim()) {
    createError.value = '请填写维修工单与备件物料。'
    return
  }
  const quantity = Number(createForm.quantity)
  if (!(quantity > 0)) {
    createError.value = '需求数量需为正数。'
    return
  }

  const body: BusinessConsoleCreateMaintenanceSparePartRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    workOrderId: createForm.workOrderId.trim(),
    skuCode: createForm.skuCode.trim(),
    quantity,
    uomCode: createForm.uomCode.trim() || undefined,
  }

  try {
    await createSparePart(body)
    createOpen.value = false
    toast.success('备件需求已创建')
  } catch {
    // 失败信息由对话框错误区呈现。
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="备件需求"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="`${sparePartsTotal} 条备件需求`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/inventory/availability"
            ><PackageSearchIcon aria-hidden="true" />库存可用量</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="sparePartsPending"
          @click="refreshSpareParts"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建备件需求
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="sparePartsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="spareParts"
      :row-key="rowKey"
      :loading="sparePartsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无备件需求。维修工单需要更换物料时在此登记需求。"
    >
      <template #cell-workOrderId="{ row }">
        <RouterLink
          :to="{ path: '/maintenance/work-orders', query: { workOrderId: row.workOrderId } }"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.workOrderId ?? '未关联' }}
        </RouterLink>
      </template>
      <template #cell-skuCode="{ row }">
        <RouterLink
          :to="{ path: '/inventory/availability', query: { skuCode: row.skuCode } }"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.skuCode ?? '未记录' }}
        </RouterLink>
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`备件需求操作 ${sparePartNo(row)}`">
          <NvDropdownMenuItem as-child>
            <RouterLink
              :to="{ path: '/maintenance/work-orders', query: { workOrderId: row.workOrderId } }"
            >
              <WrenchIcon aria-hidden="true" />
              关联工单
            </RouterLink>
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink :to="{ path: '/inventory/availability', query: { skuCode: row.skuCode } }">
              <PackageSearchIcon aria-hidden="true" />
              查看库存
            </RouterLink>
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建备件需求</NvDialogTitle>
          <NvDialogDescription
            >备件需求关联维修工单；库存可用量以库存管理页面为准。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="sp-work-order">维修工单</NvFieldLabel>
              <NvInput
                id="sp-work-order"
                v-model="createForm.workOrderId"
                autocomplete="off"
                placeholder="如 WO-..."
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="sp-sku">备件物料</NvFieldLabel>
              <NvInput
                id="sp-sku"
                v-model="createForm.skuCode"
                autocomplete="off"
                placeholder="如 BRG-6205"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="sp-quantity">需求数量</NvFieldLabel>
              <NvInput
                id="sp-quantity"
                v-model="createForm.quantity"
                type="number"
                min="0.0001"
                step="0.0001"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="sp-uom">单位</NvFieldLabel>
              <NvInput
                id="sp-uom"
                v-model="createForm.uomCode"
                autocomplete="off"
                placeholder="EA"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="createErrorMessage" :errors="[createErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createSparePartPending">
              <Spinner v-if="createSparePartPending" aria-hidden="true" />
              <PackageSearchIcon v-else aria-hidden="true" />
              创建备件需求
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
