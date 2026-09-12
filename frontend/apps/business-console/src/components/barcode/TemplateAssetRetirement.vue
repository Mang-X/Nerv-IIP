<script setup lang="ts">
import type { BusinessConsoleBarcodeTemplateAssetRetirementStatus } from '@nerv-iip/api-client'
import { computed, shallowRef, useId } from 'vue'
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvButton,
  NvField,
  NvFieldLabel,
  NvStatusBadge,
  Spinner,
} from '@nerv-iip/ui'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useBarcodeTemplateRetirement } from '@/composables/useBarcodeTemplateRetirement'
import { notifyError, notifySuccess } from '@/utils/notify'

const props = defineProps<{
  organizationId: string
  environmentId: string
  templateId: string
  fileId: string
  templateName: string
  inactive: boolean
}>()
const auth = useAuthStore()
const allowed = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.barcodeTemplateAssetsRetire),
)
const { context, busy, accepted, submitted, canSubmit, refresh, submit } =
  useBarcodeTemplateRetirement({
    organizationId: props.organizationId,
    environmentId: props.environmentId,
    templateId: props.templateId,
    fileId: props.fileId,
  })
const open = shallowRef(false)
const reason = shallowRef('')
const showErrors = shallowRef(false)
const reasonId = useId()
const displays: Record<
  BusinessConsoleBarcodeTemplateAssetRetirementStatus,
  { label: string; value: string }
> = {
  pending: { label: '处理中', value: 'pending' },
  'quota-released': { label: '配额已释放，文件仍保留', value: 'completed' },
  'execution-outcome-unknown': { label: '执行结果待核实，已暂停', value: 'warning' },
  'replay-window-expired': { label: '查询窗口已过期', value: 'disabled' },
}
const display = computed(() =>
  context.value?.status
    ? displays[context.value.status]
    : accepted.value
      ? { label: '已受理', value: 'pending' }
      : undefined,
)

async function load() {
  if (!allowed.value) return
  try {
    await refresh()
  } catch {
    notifyError('读取退役状态失败，请稍后刷新核实。')
  }
}
async function show() {
  open.value = true
  await load()
}
async function confirm() {
  if (!allowed.value || !props.inactive || !canSubmit.value) return
  if (!reason.value.trim()) {
    showErrors.value = true
    return
  }
  try {
    if (await submit(reason.value)) notifySuccess('模板资产退役申请已受理，请刷新查看处理状态。')
  } catch {
    notifyError('退役申请结果尚未确认，请先刷新核实。再次确认将使用原提交内容。')
  }
}
</script>

<template>
  <template v-if="allowed">
    <NvButton size="sm" variant="ghost" type="button" @click="show">资产退役</NvButton>
    <NvAlertDialog v-model:open="open">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>模板资产退役 · {{ templateName }}</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            退役后，当前模板文件不能再用于打印。配额释放不代表文件已物理删除。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <div v-if="display" role="status" class="py-3">
          <NvStatusBadge :value="display.value" :label="display.label" />
        </div>
        <p v-else-if="!inactive" class="text-sm text-muted-foreground">
          请先停用模板，再申请资产退役。
        </p>
        <form v-else-if="context" class="grid gap-4" @submit.prevent="confirm">
          <NvField :data-invalid="showErrors && !reason.trim()">
            <NvFieldLabel :for="reasonId">退役原因</NvFieldLabel>
            <textarea
              :id="reasonId"
              v-model="reason"
              :disabled="!!submitted"
              maxlength="500"
              class="min-h-24 rounded-md border bg-background px-3 py-2 text-sm"
            />
            <p v-if="showErrors && !reason.trim()" class="text-sm text-destructive" role="alert">
              请填写退役原因。
            </p>
          </NvField>
          <NvButton type="submit" variant="destructive" :disabled="!canSubmit">
            <Spinner v-if="busy" aria-hidden="true" />确认退役
          </NvButton>
        </form>
        <p v-else-if="busy" class="text-sm text-muted-foreground" role="status">
          正在读取退役状态…
        </p>
        <NvAlertDialogFooter>
          <NvButton type="button" variant="outline" :disabled="busy" @click="load"
            >刷新状态</NvButton
          >
          <NvButton type="button" variant="outline" @click="open = false">关闭</NvButton>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
  </template>
</template>
