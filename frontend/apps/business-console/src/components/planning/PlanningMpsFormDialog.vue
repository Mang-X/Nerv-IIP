<script setup lang="ts">
import { listBusinessConsoleMasterDataSkus } from '@nerv-iip/api-client'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  NvButton,
  NvDatePicker,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
} from '@nerv-iip/ui'
import { PlusIcon } from '@lucide/vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type { ShallowUnwrapRef } from 'vue'
import type { usePlanningWorkbenchController } from './usePlanningWorkbenchController'

type View = ShallowUnwrapRef<ReturnType<typeof usePlanningWorkbenchController>>

const props = defineProps<{
  baseUomBySku: View['baseUomBySku']
  canSubmitMps: View['canSubmitMps']
  createMpsBucketPending: View['createMpsBucketPending']
  hasPermission: View['hasPermission']
  mpsEditingId: View['mpsEditingId']
  mpsForm: View['mpsForm']
  newMpsBucket: View['newMpsBucket']
  siteOptions: View['siteOptions']
  submitMpsBucket: View['submitMpsBucket']
  uomOptions: View['uomOptions']
  updateMpsBucketPending: View['updateMpsBucketPending']
}>()

const mpsOpen = defineModel<View['mpsOpen']>('mpsOpen', { required: true })
const businessContext = useBusinessContextStore()
let skuLookupGeneration = 0

function selectSku(code: string) {
  if (code === props.mpsForm.skuCode) return
  props.mpsForm.skuCode = code
  const trimmed = code.trim()
  const cachedUom = props.baseUomBySku.get(trimmed)
  if (cachedUom) {
    skuLookupGeneration += 1
    props.mpsForm.uomCode = cachedUom
    return
  }
  props.mpsForm.uomCode = ''
  if (!trimmed) return
  const generation = ++skuLookupGeneration
  const { organizationId, environmentId } = businessContext
  if (!organizationId || !environmentId) return
  void listBusinessConsoleMasterDataSkus({
    query: { organizationId, environmentId, keyword: trimmed, take: 20 },
    throwOnError: false,
  })
    .then((response) => {
      if (generation !== skuLookupGeneration || props.mpsForm.skuCode.trim() !== trimmed) return
      if (props.mpsForm.uomCode) return
      const items = response.data?.data?.items ?? []
      const exact = items.find((item) => item.code?.trim() === trimmed)
      if (exact?.baseUomCode) props.mpsForm.uomCode = exact.baseUomCode
    })
    .catch(() => {})
}
</script>

<template>
  <NvDialog v-if="hasPermission(P.planningMpsManage)" v-model:open="mpsOpen">
    <NvDialogTrigger as-child>
      <NvButton size="sm" type="button" variant="outline" @click="newMpsBucket">
        <PlusIcon aria-hidden="true" />
        新建 MPS
      </NvButton>
    </NvDialogTrigger>
    <NvDialogContent class="sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>{{ mpsEditingId ? '编辑主计划行' : '新建主计划行' }}</NvDialogTitle>
        <NvDialogDescription class="sr-only">录入一个计划周期的主生产计划。</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitMpsBucket">
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="mps-sku">SKU</NvFieldLabel>
            <DirectoryPicker
              id="mps-sku"
              :model-value="mpsForm.skuCode"
              @update:model-value="selectSku"
              directory-type="material"
              creatable
              placeholder="选择 SKU"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mps-site">工厂</NvFieldLabel>
            <NvSelect v-model="mpsForm.siteCode">
              <NvSelectTrigger id="mps-site"
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
            <NvFieldLabel for="mps-uom">单位</NvFieldLabel>
            <NvSelect v-model="mpsForm.uomCode">
              <NvSelectTrigger id="mps-uom"
                ><NvSelectValue placeholder="选择单位"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="mps-qty">计划数量</NvFieldLabel>
            <NvInput
              id="mps-qty"
              v-model.number="mpsForm.quantity"
              min="0.0001"
              step="0.0001"
              type="number"
            />
          </NvField>
          <NvField>
            <NvFieldLabel>计划周期日期</NvFieldLabel>
            <NvDatePicker v-model="mpsForm.bucketDate" placeholder="选择计划日期" class="w-full" />
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="mpsOpen = false">取消</NvButton>
          <NvButton
            type="submit"
            :disabled="createMpsBucketPending || updateMpsBucketPending || !canSubmitMps"
          >
            <NvSpinner v-if="createMpsBucketPending || updateMpsBucketPending" aria-hidden="true" />
            保存主计划行
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
