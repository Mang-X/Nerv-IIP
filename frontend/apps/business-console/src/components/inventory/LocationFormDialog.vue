<script setup lang="ts">
/**
 * 新建 / 编辑库位。
 *
 * 既是库位维护页的新建与编辑弹窗，也是 `DirectoryPicker` 的 `location` 新增弹窗（约定见
 * `directoryCreators.ts`）：表单里要选的库位还没建时，就地建好后自动选中。
 * 传了 `location` 是编辑（编码即身份，只读）；`context.siteCode` 给了就预填工厂。
 */
import type { BusinessConsoleInventoryLocationResponse } from '@nerv-iip/api-client'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import type {
  DirectoryCreateContext,
  DirectoryCreatedItem,
} from '@/components/business/directoryCreators'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useInventoryLocationSave } from '@/composables/useBusinessInventory'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { FALLBACK_INVENTORY_SITE_CODE } from '@/composables/useInventoryScope'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  NvButton,
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
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, ref } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { LOCATION_STATUS_OPTIONS, LOCATION_TYPE_OPTIONS } from './locationOptions'

const props = defineProps<{
  context?: DirectoryCreateContext
  /** 要编辑的库位；不传是新建。 */
  location?: BusinessConsoleInventoryLocationResponse
}>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ created: [item: DirectoryCreatedItem] }>()

const businessContext = useBusinessContextStore()
const { locationCodeExists, saveLocation, saveLocationPending } = useInventoryLocationSave()

const siteCatalog = useBusinessMasterDataResources('site')
const catalogSiteOptions = computed(() =>
  siteCatalog.resources.value.flatMap((site) =>
    site.code ? [{ value: site.code, label: site.displayName?.trim() || site.code }] : [],
  ),
)

// 编码即身份：编辑态只读。
const editingCode = props.location?.locationCode ?? null
const form = reactive({
  locationCode: '',
  locationType: props.location?.locationType ?? 'storage',
  siteCode: props.location?.siteCode ?? props.context?.siteCode?.trim() ?? '',
  parentLocationCode: props.location?.parentLocationCode ?? '',
  status: props.location?.status ?? 'active',
})
// 没选过工厂时默认第一个工厂；工厂目录回来之前先不给默认值（取不到工厂时才用兜底工厂），
// 免得下拉先挂上一个只有编码的临时项。
const siteCode = computed({
  get: () =>
    form.siteCode ||
    catalogSiteOptions.value[0]?.value ||
    (siteCatalog.resourcesPending.value ? '' : FALLBACK_INVENTORY_SITE_CODE),
  set: (value: string) => {
    form.siteCode = value
  },
})
const siteOptions = computed(() => {
  const options = [...catalogSiteOptions.value]
  // 工厂主数据没加载到、或正在编辑的库位挂在目录外的工厂上时，当前值也要能显示和保留。
  if (siteCode.value && !options.some((o) => o.value === siteCode.value)) {
    options.unshift({ value: siteCode.value, label: siteCode.value })
  }
  return options
})

const showErrors = ref(false)
const duplicateCode = ref(false)
const codeValid = computed(() => !!editingCode || form.locationCode.trim().length > 0)
const typeValid = computed(() => form.locationType.trim().length > 0)
const siteValid = computed(() => siteCode.value.trim().length > 0)
const canSubmit = computed(() => codeValid.value && typeValid.value && siteValid.value)

async function submit() {
  duplicateCode.value = false
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const locationCode = editingCode ?? form.locationCode.trim()
  try {
    if (!editingCode && (await locationCodeExists(locationCode))) {
      duplicateCode.value = true
      return
    }
    await saveLocation({
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      locationCode,
      locationType: form.locationType,
      siteCode: siteCode.value,
      parentLocationCode: form.parentLocationCode.trim() || null,
      status: form.status,
    })
    notifySuccess(
      editingCode ? `库位「${locationCode}」已更新。` : `已新建库位「${locationCode}」。`,
    )
    if (!editingCode) emit('created', { code: locationCode, name: locationCode })
    open.value = false
  } catch (error) {
    notifyOperationFailure('保存库位失败', error, '保存库位失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>{{ editingCode ? '编辑库位' : '新建库位' }}</NvDialogTitle>
        <NvDialogDescription>收发料、完工入库与线边库存都按库位记账</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-5" @submit.prevent="submit">
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
          <NvField v-if="!editingCode" :data-invalid="(showErrors && !codeValid) || duplicateCode">
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
                <NvSelectItem v-for="o in LOCATION_TYPE_OPTIONS" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField :data-invalid="showErrors && !siteValid">
            <NvFieldLabel for="location-site"
              >工厂 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="siteCode">
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
            <NvInput id="location-parent" v-model="form.parentLocationCode" placeholder="可不填" />
          </NvField>
          <NvField>
            <NvFieldLabel for="location-status">状态</NvFieldLabel>
            <NvSelect v-model="form.status">
              <NvSelectTrigger id="location-status"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="o in LOCATION_STATUS_OPTIONS"
                  :key="o.value"
                  :value="o.value"
                  >{{ o.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
        </NvFieldGroup>

        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="saveLocationPending">
            <Spinner v-if="saveLocationPending" aria-hidden="true" />
            {{ editingCode ? '保存修改' : '创建库位' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
