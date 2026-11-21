<script setup lang="ts">
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import {
  NvButton,
  NvEntityPicker,
  NvField,
  NvFieldDescription,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
} from '@nerv-iip/ui'
import { PlusIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useMaintenanceWorkOrdersPage } from '../useMaintenanceWorkOrdersPage'

type View = ShallowUnwrapRef<ReturnType<typeof useMaintenanceWorkOrdersPage>>

defineProps<{
  alarmOptions: View['alarmOptions']
  alarmsPending: View['alarmsPending']
  canManageWorkOrders: View['canManageWorkOrders']
  createCarried: View['createCarried']
  createCarriedItems: View['createCarriedItems']
  createErrorMessage: View['createErrorMessage']
  createForm: View['createForm']
  createWorkOrderPending: View['createWorkOrderPending']
  directoryMessage: View['directoryMessage']
  directoryState: View['directoryState']
  downtimeReasonOptions: View['downtimeReasonOptions']
  downtimeReasons: View['downtimeReasons']
  openReasonCreate: View['openReasonCreate']
  priorityOptions: View['priorityOptions']
  reasonsTotal: View['reasonsTotal']
  submitCreate: View['submitCreate']
  workerOptions: View['workerOptions']
  workersPending: View['workersPending']
}>()

const createOpen = defineModel<View['createOpen']>('createOpen', { required: true })

const createDeviceModel = defineModel<View['createDeviceModel']>('createDeviceModel', {
  required: true,
})

const reasonKeyword = defineModel<View['reasonKeyword']>('reasonKeyword', { required: true })
</script>

<template>
  <NvSheet v-model:open="createOpen">
    <NvSheetContent class="flex w-full flex-col overflow-y-auto sm:max-w-xl">
      <NvSheetHeader>
        <NvSheetTitle>新建维护工单</NvSheetTitle>
        <!-- 开单对象在下方呈现；此处仅供读屏播报。 -->
        <NvSheetDescription class="sr-only">
          为设备 {{ createForm.deviceAssetId || '（待选择）' }} 开具维护工单。
        </NvSheetDescription>
      </NvSheetHeader>
      <form class="grid gap-4 px-4 pb-4" @submit.prevent="submitCreate">
        <!-- 从报警行带入：设备与来源报警只读呈现，不做成看起来还能改的输入位。 -->
        <CarriedContextSummary v-if="createCarried" label="开单对象" :items="createCarriedItems" />

        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField v-if="!createCarried">
            <NvFieldLabel for="mwo-device">设备</NvFieldLabel>
            <DirectoryPicker
              id="mwo-device"
              v-model="createDeviceModel"
              directory-type="equipment"
              creatable
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-priority">优先级</NvFieldLabel>
            <NvSelect v-model="createForm.priority">
              <NvSelectTrigger id="mwo-priority" aria-label="优先级"
                ><NvSelectValue
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in priorityOptions" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-opened-by">开单人</NvFieldLabel>
            <NvSearchSelect
              id="mwo-opened-by"
              v-model="createForm.openedByUserId"
              :options="workerOptions"
              :loading="workersPending"
              aria-label="开单人"
              placeholder="选择开单人"
              search-placeholder="搜索姓名 / 工号…"
            />
          </NvField>
          <NvField v-if="!createCarried">
            <NvFieldLabel for="mwo-alarm">关联报警</NvFieldLabel>
            <NvEntityPicker
              id="mwo-alarm"
              v-model="createForm.sourceAlarmId"
              :options="alarmOptions"
              :show-code="false"
              :disabled="!createForm.deviceAssetId.trim()"
              title="选择关联报警"
              :placeholder="createForm.deviceAssetId.trim() ? '可选' : '先选设备'"
              empty-text="该设备当前没有报警"
              :loading="alarmsPending"
              clearable
              aria-label="关联报警"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-technician">指派技师</NvFieldLabel>
            <WorkerSelect
              id="mwo-technician"
              v-model="createForm.assignedTechnicianUserId"
              placeholder="未指派"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-est-labor">预估工时（分钟）</NvFieldLabel>
            <NvInput
              id="mwo-est-labor"
              v-model="createForm.estimatedLaborMinutes"
              type="number"
              min="0"
              step="1"
              placeholder="可选"
            />
          </NvField>
          <NvField class="sm:col-span-2">
            <NvFieldLabel for="mwo-unavailability-mode">设备占用登记</NvFieldLabel>
            <NvSearchSelect
              id="mwo-unavailability-mode"
              v-model="createForm.unavailabilityMode"
              :options="[
                { value: 'none', label: '不登记设备不可用' },
                { value: 'reason', label: '登记设备不可用' },
              ]"
              aria-label="设备占用登记"
            />
            <NvFieldDescription>
              登记后从建单时刻占用设备，工单完工时自动释放。
            </NvFieldDescription>
          </NvField>
          <NvField v-if="createForm.unavailabilityMode === 'reason'" class="sm:col-span-2">
            <NvFieldLabel for="mwo-asset-unavailable-reason">设备占用原因</NvFieldLabel>
            <NvEntityPicker
              id="mwo-asset-unavailable-reason"
              v-model="createForm.assetUnavailableReasonCode"
              v-model:search="reasonKeyword"
              title="停机原因"
              :options="downtimeReasonOptions"
              :server-search="true"
              :total-count="reasonsTotal"
              :loading="directoryState === 'loading'"
              :empty-text="directoryMessage"
              placeholder="请选择停机原因"
            />
            <NvFieldDescription v-if="directoryMessage" role="status">
              {{ directoryMessage }}；也可选择“不登记设备不可用”继续建单。
            </NvFieldDescription>
            <div class="flex flex-wrap gap-2">
              <NvButton
                v-if="directoryState === 'failed'"
                type="button"
                variant="outline"
                @click="downtimeReasons.refresh()"
                >重试读取停机原因</NvButton
              >
              <NvButton
                v-if="canManageWorkOrders"
                type="button"
                variant="outline"
                size="sm"
                @click="openReasonCreate('create')"
              >
                <PlusIcon aria-hidden="true" />
                新增停机原因
              </NvButton>
            </div>
          </NvField>
          <NvField v-if="createForm.unavailabilityMode === 'reason'" class="sm:col-span-2">
            <NvFieldLabel for="mwo-create-expected-restore">预计恢复时间</NvFieldLabel>
            <NvInput
              id="mwo-create-expected-restore"
              v-model="createForm.expectedRestoreAtLocal"
              type="datetime-local"
              step="1"
            />
            <NvFieldDescription>可选，按本机时区填写；预测不代表设备已恢复。</NvFieldDescription>
          </NvField>
        </NvFieldGroup>

        <NvFieldError v-if="createErrorMessage" :errors="[createErrorMessage]" />

        <NvSheetFooter class="px-0">
          <NvButton type="button" variant="outline" @click="createOpen = false">取消</NvButton>
          <NvButton type="submit" :disabled="createWorkOrderPending">
            <NvSpinner v-if="createWorkOrderPending" aria-hidden="true" />
            创建维护工单
          </NvButton>
        </NvSheetFooter>
      </form>
    </NvSheetContent>
  </NvSheet>
</template>
