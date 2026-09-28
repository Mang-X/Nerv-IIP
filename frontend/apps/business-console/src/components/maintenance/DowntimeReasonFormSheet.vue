<script setup lang="ts">
/**
 * 新建 / 编辑停机原因（#3855），侧滑抽屉。
 *
 * 既是停机原因维护页的新建与编辑入口，也是维护工单「设备占用原因」旁的就地新增：
 * 目录里没有要用的原因时不必离开当前表单，建好后自动选中。
 * 传了 `reason` 是编辑（原因编码即身份，只读）。
 */
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import type { MaintenanceDowntimeReasonRow } from '@/composables/useMaintenanceDowntimeReasonDirectory'
import { useMaintenanceDowntimeReasonMutations } from '@/composables/useMaintenanceDowntimeReasonMutations'
import {
  DOWNTIME_LOSS_CATEGORY_OPTIONS,
  DOWNTIME_REASON_CATEGORY_OPTIONS,
} from '@/data/downtimeReasonReference'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  NvButton,
  NvField,
  NvFieldDescription,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSearchSelect,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, ref } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{
  /** 要编辑的原因；不传是新建。 */
  reason?: MaintenanceDowntimeReasonRow
  /** 目录里已有的原因编码，用来在提交前拦下重复编码。 */
  existingCodes?: readonly string[]
}>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ saved: [reasonCode: string] }>()

const businessContext = useBusinessContextStore()
const { createReason, updateReason, saving } = useMaintenanceDowntimeReasonMutations()

const editingCode = props.reason?.reasonCode ?? null
const knownCategory = (options: readonly { value: string }[], value?: string) =>
  options.some((option) => option.value === value) ? value! : ''
const form = reactive({
  reasonCode: '',
  description: props.reason?.description ?? '',
  reasonCategory: knownCategory(DOWNTIME_REASON_CATEGORY_OPTIONS, props.reason?.reasonCategory),
  lossCategory: knownCategory(DOWNTIME_LOSS_CATEGORY_OPTIONS, props.reason?.lossCategory),
})

const showErrors = ref(false)
const codeMissing = computed(() => !editingCode && !form.reasonCode.trim())
const codeDuplicated = computed(() => {
  const code = form.reasonCode.trim()
  return !editingCode && !!code && (props.existingCodes ?? []).includes(code)
})
const descriptionMissing = computed(() => !form.description.trim())
const errors = computed(() => {
  const list: string[] = []
  if (codeMissing.value) list.push('请填写原因编码。')
  if (codeDuplicated.value) list.push('该原因编码已存在，请换一个编码或直接编辑原有原因。')
  if (descriptionMissing.value) list.push('请填写原因名称。')
  if (!form.reasonCategory) list.push('请选择停机分类。')
  if (!form.lossCategory) list.push('请选择损失类别。')
  return list
})

async function submit() {
  showErrors.value = true
  if (errors.value.length > 0) return
  const scope = {
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }
  const reasonCode = editingCode ?? form.reasonCode.trim()
  try {
    if (editingCode) {
      await updateReason(editingCode, {
        ...scope,
        description: form.description.trim(),
        reasonCategory: form.reasonCategory,
        lossCategory: form.lossCategory,
      })
      notifySuccess(`停机原因「${form.description.trim()}」已更新。`)
    } else {
      await createReason({
        ...scope,
        reasonCode,
        description: form.description.trim(),
        reasonCategory: form.reasonCategory,
        lossCategory: form.lossCategory,
      })
      notifySuccess(`已新建停机原因「${form.description.trim()}」。`)
    }
    emit('saved', reasonCode)
    open.value = false
  } catch (error) {
    notifyOperationFailure('保存停机原因失败', error, '保存停机原因失败，请稍后重试。')
  }
}
</script>

<template>
  <NvSheet v-model:open="open">
    <NvSheetContent class="flex w-full flex-col overflow-y-auto sm:max-w-md">
      <NvSheetHeader>
        <NvSheetTitle>{{ editingCode ? '编辑停机原因' : '新建停机原因' }}</NvSheetTitle>
        <NvSheetDescription>
          报修登记设备占用、完工登记停机与生产停机都从这份目录里选原因。
        </NvSheetDescription>
      </NvSheetHeader>
      <form class="grid gap-4 px-4 pb-4" @submit.prevent="submit">
        <CarriedContextSummary
          v-if="editingCode"
          label="正在编辑的停机原因"
          :items="[{ label: '原因编码', value: editingCode }]"
        />
        <NvFieldGroup class="grid gap-3">
          <NvField
            v-if="!editingCode"
            :data-invalid="showErrors && (codeMissing || codeDuplicated)"
          >
            <NvFieldLabel for="dtr-code"
              >原因编码 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput
              id="dtr-code"
              v-model="form.reasonCode"
              maxlength="100"
              placeholder="如 DT-HYD"
              :aria-invalid="showErrors && (codeMissing || codeDuplicated)"
            />
            <NvFieldDescription>编码保存后不可修改，建议用大写字母与短横线。</NvFieldDescription>
          </NvField>
          <NvField :data-invalid="showErrors && descriptionMissing">
            <NvFieldLabel for="dtr-description"
              >原因名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput
              id="dtr-description"
              v-model="form.description"
              maxlength="500"
              placeholder="如 液压系统故障"
              :aria-invalid="showErrors && descriptionMissing"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="dtr-category"
              >停机分类 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSearchSelect
              id="dtr-category"
              v-model="form.reasonCategory"
              :options="[...DOWNTIME_REASON_CATEGORY_OPTIONS]"
              placeholder="请选择停机分类"
              aria-label="停机分类"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="dtr-loss"
              >损失类别 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSearchSelect
              id="dtr-loss"
              v-model="form.lossCategory"
              :options="[...DOWNTIME_LOSS_CATEGORY_OPTIONS]"
              placeholder="请选择损失类别"
              aria-label="损失类别"
            />
            <NvFieldDescription>
              决定这类停机在设备综合效率里计入哪一项损失；计划停机不计入损失。
            </NvFieldDescription>
          </NvField>
        </NvFieldGroup>

        <NvFieldError v-if="showErrors && errors.length" :errors="errors" />

        <NvSheetFooter class="px-0">
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="saving">
            <Spinner v-if="saving" aria-hidden="true" />
            保存
          </NvButton>
        </NvSheetFooter>
      </form>
    </NvSheetContent>
  </NvSheet>
</template>
