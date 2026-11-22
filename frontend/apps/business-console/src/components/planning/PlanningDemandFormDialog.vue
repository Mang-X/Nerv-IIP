<script setup lang="ts">
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
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

defineProps<{
  canSubmitDemand: View['canSubmitDemand']
  createDemandPending: View['createDemandPending']
  demandForm: View['demandForm']
  demandTypeOptions: View['demandTypeOptions']
  hasPermission: View['hasPermission']
  siteOptions: View['siteOptions']
  submitDemand: View['submitDemand']
  uomOptions: View['uomOptions']
}>()

const demandOpen = defineModel<View['demandOpen']>('demandOpen', { required: true })
</script>

<template>
  <NvDialog v-if="hasPermission(P.planningDemandsManage)" v-model:open="demandOpen">
    <NvDialogTrigger as-child>
      <NvButton size="sm" type="button">
        <PlusIcon aria-hidden="true" />
        新建需求
      </NvButton>
    </NvDialogTrigger>
    <NvDialogContent class="sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>新建需求</NvDialogTitle>
        <NvDialogDescription class="sr-only">录入一条计划需求。</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitDemand">
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel>需求类型</NvFieldLabel>
            <NvSelect v-model="demandForm.demandType">
              <NvSelectTrigger aria-label="需求类型"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in demandTypeOptions" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="demand-source">来源单号</NvFieldLabel>
            <NvInput
              id="demand-source"
              v-model="demandForm.sourceReference"
              placeholder="SO-2026-001"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="demand-sku">SKU</NvFieldLabel>
            <DirectoryPicker
              id="demand-sku"
              v-model="demandForm.skuCode"
              directory-type="material"
              creatable
              placeholder="选择 SKU"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="demand-site">工厂</NvFieldLabel>
            <NvSelect v-model="demandForm.siteCode">
              <NvSelectTrigger id="demand-site"
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
            <NvFieldLabel for="demand-uom">单位</NvFieldLabel>
            <NvSelect v-model="demandForm.uomCode">
              <NvSelectTrigger id="demand-uom"
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
            <NvFieldLabel for="demand-qty">数量</NvFieldLabel>
            <NvInput
              id="demand-qty"
              v-model.number="demandForm.quantity"
              min="0.0001"
              step="0.0001"
              type="number"
            />
          </NvField>
          <NvField>
            <NvFieldLabel>需求日期</NvFieldLabel>
            <NvDatePicker v-model="demandForm.dueDate" placeholder="选择需求日期" class="w-full" />
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="demandOpen = false">取消</NvButton>
          <NvButton type="submit" :disabled="createDemandPending || !canSubmitDemand">
            <NvSpinner v-if="createDemandPending" aria-hidden="true" />
            保存需求
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
