<script setup lang="ts">
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSpinner,
} from '@nerv-iip/ui'
import { CalendarCogIcon, FactoryIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrdersPage } from '../useWorkOrdersPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrdersPage>>

defineProps<{
  canCreateRush: View['canCreateRush']
  createRushWorkOrderPending: View['createRushWorkOrderPending']
  operationTasksPending: View['operationTasksPending']
  productionVersionsPending: View['productionVersionsPending']
  rushForm: View['rushForm']
  rushOperationTaskOptions: View['rushOperationTaskOptions']
  rushVersionOptions: View['rushVersionOptions']
  submitRushWorkOrder: View['submitRushWorkOrder']
}>()

const rushSheetOpen = defineModel<View['rushSheetOpen']>('rushSheetOpen', { required: true })
</script>

<template>
  <NvDialog v-model:open="rushSheetOpen">
    <NvDialogContent class="sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>创建急单</NvDialogTitle>
        <NvDialogDescription class="sr-only"
          >临时插单，填写物料、生产版本、数量、交期与工作中心后提交。</NvDialogDescription
        >
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitRushWorkOrder">
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="rush-sku">物料 <span class="text-destructive">*</span></NvFieldLabel>
            <DirectoryPicker
              id="rush-sku"
              v-model="rushForm.skuId"
              directory-type="material"
              creatable
              clearable
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="rush-version"
              >生产版本 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvEntityPicker
              id="rush-version"
              v-model="rushForm.productionVersionId"
              :options="rushVersionOptions"
              title="选择生产版本"
              :placeholder="rushForm.skuId ? '选择当前有效的生产版本' : '先选物料'"
              :disabled="!rushForm.skuId"
              empty-text="该物料暂无生产版本，请先在工程数据维护"
              :loading="productionVersionsPending"
              aria-label="生产版本"
              clearable
              :show-code="false"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="rush-quantity"
              >数量 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput
              id="rush-quantity"
              v-model="rushForm.quantity"
              inputmode="decimal"
              required
              type="number"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="rush-due">交期 <span class="text-destructive">*</span></NvFieldLabel>
            <NvInput id="rush-due" v-model="rushForm.dueUtc" required type="datetime-local" />
          </NvField>
          <NvField>
            <NvFieldLabel for="rush-work-center"
              >工作中心 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="rush-work-center"
              v-model="rushForm.workCenterId"
              directory-type="work-center"
              creatable
              clearable
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="rush-operation-task">工序任务</NvFieldLabel>
            <NvEntityPicker
              id="rush-operation-task"
              v-model="rushForm.operationTaskId"
              :options="rushOperationTaskOptions"
              title="选择工序任务"
              :placeholder="rushForm.workCenterId ? '可留空' : '先选工作中心'"
              :disabled="!rushForm.workCenterId"
              empty-text="该工作中心暂无在办工序任务"
              :loading="operationTasksPending"
              aria-label="工序任务"
              clearable
              :show-code="false"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="rush-operation-sequence">工序序号</NvFieldLabel>
            <NvInput
              id="rush-operation-sequence"
              v-model="rushForm.operationSequence"
              inputmode="numeric"
              type="number"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="rush-duration"
              >工时分钟 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput
              id="rush-duration"
              v-model="rushForm.durationMinutes"
              inputmode="numeric"
              required
              type="number"
            />
          </NvField>
        </NvFieldGroup>

        <NvDialogFooter class="sm:justify-between">
          <NvButton size="sm" type="button" variant="ghost" as-child>
            <RouterLink to="/scheduling"
              ><CalendarCogIcon aria-hidden="true" />排产工作台</RouterLink
            >
          </NvButton>
          <div class="flex gap-2">
            <NvButton type="button" variant="outline" @click="rushSheetOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="createRushWorkOrderPending || !canCreateRush">
              <NvSpinner v-if="createRushWorkOrderPending" aria-hidden="true" />
              <FactoryIcon v-else aria-hidden="true" />
              创建急单
            </NvButton>
          </div>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
