<script setup lang="ts">
import type { BusinessConsoleInventoryLocationResponse } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useInventoryLocations } from '@/composables/useBusinessInventory'
import { FALLBACK_INVENTORY_SITE_CODE } from '@/composables/useInventoryScope'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
  Spinner,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef } from 'vue'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '库位',
    requiredPermissions: ['business.inventory.locations.manage'],
  },
})

const {
  filters,
  locationCodeExists,
  locationRows,
  locationsError,
  locationsPage,
  locationsPageSize,
  locationsPending,
  locationsTotal,
  refreshLocations,
  saveLocation,
  saveLocationPending,
} = useInventoryLocations()

// 库位类型：value 与库存服务存的码值一致；线边库位必须是 line-side，线边库存才看得到它。
const LOCATION_TYPE_OPTIONS = [
  { value: 'storage', label: '存储库位' },
  { value: 'line-side', label: '线边库位' },
  { value: 'staging', label: '暂存区' },
  { value: 'quality-hold', label: '不合格品隔离区' },
]
function locationTypeLabel(value?: string | null) {
  if (!value) return '—'
  return LOCATION_TYPE_OPTIONS.find((o) => o.value === value)?.label ?? value
}

const STATUS_OPTIONS = [
  { value: 'active', label: '启用' },
  { value: 'inactive', label: '停用' },
]

const siteCatalog = useBusinessMasterDataResources('site')
const catalogSiteOptions = computed(() =>
  siteCatalog.resources.value.flatMap((site) =>
    site.code ? [{ value: site.code, label: site.displayName?.trim() || site.code }] : [],
  ),
)
function siteLabel(code?: string | null) {
  if (!code) return '—'
  return catalogSiteOptions.value.find((o) => o.value === code)?.label ?? code
}

const search = computed({
  get: () => filters.keyword ?? '',
  set: (value: string) => {
    filters.keyword = value.trim() ? value : undefined
  },
})

const listErrorMessage = computed(() => inlineErrorMessage(locationsError.value))

const columns: NvDataTableColumn<BusinessConsoleInventoryLocationResponse>[] = [
  { key: 'locationCode', header: '库位编码', cellClass: 'font-medium' },
  { key: 'locationType', header: '类型', width: 'w-36' },
  { key: 'siteCode', header: '工厂' },
  { key: 'parentLocationCode', header: '上级库位' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-20' },
]

// ── 新建 / 编辑 ─────────────────────────────────────────────────
interface LocationForm {
  locationCode: string
  locationType: string
  siteCode: string
  parentLocationCode: string
  status: string
}

function blankForm(): LocationForm {
  return {
    locationCode: '',
    locationType: 'storage',
    siteCode: catalogSiteOptions.value[0]?.value ?? FALLBACK_INVENTORY_SITE_CODE,
    parentLocationCode: '',
    status: 'active',
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const duplicateCode = ref(false)
// null = 新建；否则为正在编辑的库位编码（编码即身份，编辑态只读）。
const editingCode = shallowRef<string | null>(null)
const form = reactive<LocationForm>(blankForm())

const siteOptions = computed(() => {
  const options = [...catalogSiteOptions.value]
  // 工厂主数据没加载到、或正在编辑的库位挂在目录外的工厂上时，当前值也要能显示和保留。
  if (form.siteCode && !options.some((o) => o.value === form.siteCode)) {
    options.unshift({ value: form.siteCode, label: form.siteCode })
  }
  return options
})

const codeValid = computed(() => !!editingCode.value || form.locationCode.trim().length > 0)
const typeValid = computed(() => form.locationType.trim().length > 0)
const siteValid = computed(() => form.siteCode.trim().length > 0)
const canSubmit = computed(() => codeValid.value && typeValid.value && siteValid.value)

function openCreate() {
  editingCode.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  duplicateCode.value = false
  formOpen.value = true
}

function openEdit(row: BusinessConsoleInventoryLocationResponse) {
  if (!row.locationCode) return
  editingCode.value = row.locationCode
  showErrors.value = false
  duplicateCode.value = false
  Object.assign(form, {
    locationCode: row.locationCode,
    locationType: row.locationType ?? '',
    siteCode: row.siteCode ?? '',
    parentLocationCode: row.parentLocationCode ?? '',
    status: row.status ?? 'active',
  })
  formOpen.value = true
}

async function submitForm() {
  duplicateCode.value = false
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const locationCode = editingCode.value ?? form.locationCode.trim()
  try {
    if (!editingCode.value && (await locationCodeExists(locationCode))) {
      duplicateCode.value = true
      return
    }
    await saveLocation({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      locationCode,
      locationType: form.locationType,
      siteCode: form.siteCode,
      parentLocationCode: form.parentLocationCode.trim() || null,
      status: form.status,
    })
    notifySuccess(
      editingCode.value ? `库位「${locationCode}」已更新。` : `已新建库位「${locationCode}」。`,
    )
    showErrors.value = false
    formOpen.value = false
    editingCode.value = null
  } catch (error) {
    notifyOperationFailure('保存库位失败', error, '保存库位失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="库位"
      :breadcrumbs="[{ label: '库存管理' }]"
      :count="`${locationsTotal} 个库位`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="locationsPending"
          @click="refreshLocations"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建库位
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="search" search-placeholder="按库位编码筛选" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="locationsPage"
      :page-size="String(locationsPageSize)"
      :total-items="locationsTotal"
      @update:page="locationsPage = $event"
      @update:page-size="(v) => (locationsPageSize = Number(v))"
      :columns="columns"
      :rows="locationRows"
      row-key="locationCode"
      :loading="locationsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="还没有库位。"
    >
      <template #empty>
        <template v-if="search">
          <p class="text-sm font-medium">没有符合条件的库位</p>
          <NvButton size="sm" type="button" variant="outline" @click="search = ''">
            清空筛选
          </NvButton>
        </template>
        <template v-else>
          <p class="text-sm font-medium">还没有库位</p>
          <p class="max-w-md text-sm text-muted-foreground">
            新建原料库、成品库、线边库等库位后，收发料与线边库存才有去处。
          </p>
          <NvButton size="sm" type="button" @click="openCreate">
            <PlusIcon aria-hidden="true" />
            新建库位
          </NvButton>
        </template>
      </template>
      <template #cell-locationType="{ row }">
        {{ locationTypeLabel(row.locationType) }}
      </template>
      <template #cell-siteCode="{ row }">
        {{ siteLabel(row.siteCode) }}
      </template>
      <template #cell-parentLocationCode="{ row }">
        <span v-if="row.parentLocationCode">{{ row.parentLocationCode }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          :label="row.status === 'active' ? '启用' : '停用'"
          :tone="row.status === 'active' ? 'success' : 'neutral'"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end">
          <NvButton type="button" variant="ghost" size="sm" @click="openEdit(row)">编辑</NvButton>
        </div>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="formOpen">
      <NvDialogContent class="sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>{{ editingCode ? '编辑库位' : '新建库位' }}</NvDialogTitle>
          <NvDialogDescription>收发料、完工入库与线边库存都按库位记账</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-5" @submit.prevent="submitForm">
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请填写带 * 的必填项（已标红）。
          </p>

          <CarriedContextSummary
            v-if="editingCode"
            label="正在编辑的库位"
            :items="[{ label: '库位编码', value: editingCode }]"
          />

          <FormSectionTitle>基本信息</FormSectionTitle>
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField
              v-if="!editingCode"
              :data-invalid="(showErrors && !codeValid) || duplicateCode"
            >
              <NvFieldLabel for="location-code"
                >库位编码 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="location-code"
                v-model="form.locationCode"
                placeholder="例如：loc-line-02"
                @update:model-value="duplicateCode = false"
              />
              <p v-if="duplicateCode" class="text-sm text-destructive" role="alert">
                库位编码已存在，请在列表里编辑它。
              </p>
            </NvField>
            <NvField :data-invalid="showErrors && !typeValid">
              <NvFieldLabel for="location-type"
                >类型 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvSelect v-model="form.locationType">
                <NvSelectTrigger id="location-type"
                  ><NvSelectValue placeholder="选择库位类型"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="o in LOCATION_TYPE_OPTIONS"
                    :key="o.value"
                    :value="o.value"
                    >{{ o.label }}</NvSelectItem
                  >
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField :data-invalid="showErrors && !siteValid">
              <NvFieldLabel for="location-site"
                >工厂 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvSelect v-model="form.siteCode">
                <NvSelectTrigger id="location-site"
                  ><NvSelectValue placeholder="选择工厂"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in siteOptions" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="location-parent">上级库位</NvFieldLabel>
              <NvInput
                id="location-parent"
                v-model="form.parentLocationCode"
                placeholder="可不填"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="location-status">状态</NvFieldLabel>
              <NvSelect v-model="form.status">
                <NvSelectTrigger id="location-status"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in STATUS_OPTIONS" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
          </NvFieldGroup>

          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="formOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="saveLocationPending">
              <Spinner v-if="saveLocationPending" aria-hidden="true" />
              {{ editingCode ? '保存修改' : '创建库位' }}
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
