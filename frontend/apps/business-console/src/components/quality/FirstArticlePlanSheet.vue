<script setup lang="ts">
import type {
  BusinessConsoleCreateInspectionPlanRequest,
  BusinessConsoleInspectionPlanCharacteristicInput,
} from '@nerv-iip/api-client'
import type { EntityPickerOption } from '@nerv-iip/ui'
import {
  NvButton,
  NvEntityPicker,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
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
  Spinner,
} from '@nerv-iip/ui'
import { PlusIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, ref, watch } from 'vue'

import { useQualityFirstArticlePlanActions } from '@/composables/useBusinessQuality'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{
  open: boolean
  organizationId: string
  environmentId: string
  skuOptions: EntityPickerOption[]
  skusPending: boolean
  workCenterOptions: EntityPickerOption[]
  workCentersPending: boolean
}>()
const emit = defineEmits<{
  'update:open': [value: boolean]
  completed: []
}>()

const { createAndActivateFirstArticlePlan, createFirstArticlePlanPending } =
  useQualityFirstArticlePlanActions()

interface CharacteristicDraft {
  characteristicCode: string
  name: string
  method: string
  severity: string
  samplingRule: string
}

const form = reactive({
  planCode: '',
  skuCode: '',
  workCenterId: '',
  characteristics: [emptyCharacteristic()] as CharacteristicDraft[],
})
const submitted = ref(false)

const openModel = computed({
  get: () => props.open,
  set: (value: boolean) => emit('update:open', value),
})

const blockers = computed(() => {
  const messages: string[] = []
  if (!props.organizationId.trim() || !props.environmentId.trim()) {
    messages.push('业务范围尚未就绪，请先选择组织与环境。')
  }
  if (!form.planCode.trim()) messages.push('请填写方案编号。')
  if (!form.skuCode.trim()) messages.push('请选择适用物料。')
  if (!form.workCenterId.trim()) messages.push('请选择工序工作中心。')
  if (form.characteristics.length === 0) messages.push('请至少添加一个检验项。')
  form.characteristics.forEach((item, index) => {
    const prefix = `第 ${index + 1} 个检验项`
    if (!item.characteristicCode.trim()) messages.push(`${prefix}：请填写检验项编号。`)
    if (!item.name.trim()) messages.push(`${prefix}：请填写检验项名称。`)
    if (!item.method.trim()) messages.push(`${prefix}：请填写检验方法。`)
    if (!item.samplingRule.trim()) messages.push(`${prefix}：请填写抽样要求。`)
  })
  const normalizedCodes = form.characteristics.map((item) =>
    item.characteristicCode.trim().toLowerCase(),
  )
  if (normalizedCodes.some((code, index) => code && normalizedCodes.indexOf(code) !== index)) {
    messages.push('检验项编号不能重复。')
  }
  return messages
})

function emptyCharacteristic(): CharacteristicDraft {
  return {
    characteristicCode: '',
    name: '',
    method: '首件检验',
    severity: 'major',
    samplingRule: '首件全检',
  }
}

function addCharacteristic() {
  form.characteristics.push(emptyCharacteristic())
}

function removeCharacteristic(index: number) {
  form.characteristics.splice(index, 1)
}

function characteristicCodeInvalid(index: number) {
  if (!submitted.value) return false
  const code = form.characteristics[index]?.characteristicCode.trim().toLowerCase() ?? ''
  if (!code) return true
  return form.characteristics.some(
    (item, candidateIndex) =>
      candidateIndex !== index && item.characteristicCode.trim().toLowerCase() === code,
  )
}

function resetForm() {
  form.planCode = ''
  form.skuCode = ''
  form.workCenterId = ''
  form.characteristics = [emptyCharacteristic()]
  submitted.value = false
}

watch(
  () => props.open,
  (open) => {
    if (open) resetForm()
  },
  { immediate: true },
)

function toCharacteristic(
  item: CharacteristicDraft,
): BusinessConsoleInspectionPlanCharacteristicInput {
  return {
    characteristicCode: item.characteristicCode.trim(),
    name: item.name.trim(),
    method: item.method.trim(),
    severity: item.severity,
    required: true,
    samplingRule: item.samplingRule.trim(),
    characteristicType: 'attribute',
  }
}

async function submit() {
  submitted.value = true
  if (blockers.value.length > 0) return
  const body: BusinessConsoleCreateInspectionPlanRequest = {
    organizationId: props.organizationId,
    environmentId: props.environmentId,
    planCode: form.planCode.trim(),
    category: 'first-article',
    skuCode: form.skuCode,
    workCenterId: form.workCenterId,
    characteristics: form.characteristics.map(toCharacteristic),
  }
  try {
    const result = await createAndActivateFirstArticlePlan(body)
    emit('completed')
    if (!result.activated) {
      notifyOperationFailure(
        '首件方案启用失败',
        result.activationError,
        '方案已创建但未启用，请在方案列表中重新启用。',
      )
      return
    }
    notifySuccess(`首件检验方案 ${form.planCode.trim()} 已创建并启用。`)
    openModel.value = false
    resetForm()
  } catch (error) {
    notifyOperationFailure('首件方案创建失败', error, '首件检验方案创建失败，请稍后重试。')
  }
}
</script>

<template>
  <NvSheet v-model:open="openModel">
    <NvSheetContent data-testid="first-article-plan-sheet" size="xl">
      <NvSheetHeader>
        <NvSheetTitle>配置首件检验方案</NvSheetTitle>
        <NvSheetDescription>
          方案适用于指定物料在指定工序的首件确认；启用后可用于首件检验记录，生产报工门禁另行接入。
        </NvSheetDescription>
      </NvSheetHeader>

      <form class="grid gap-5" @submit.prevent="submit">
        <div
          v-if="submitted && blockers.length"
          id="first-article-plan-errors"
          class="rounded-lg border border-destructive/40 bg-destructive/10 p-3"
          role="alert"
        >
          <p class="text-sm font-medium text-destructive">请补齐以下内容：</p>
          <ul class="mt-1 list-disc pl-5 text-sm text-destructive">
            <li v-for="message in blockers" :key="message">{{ message }}</li>
          </ul>
        </div>

        <NvFieldGroup class="grid gap-3 sm:grid-cols-3">
          <NvField :data-invalid="submitted && !form.planCode.trim()">
            <NvFieldLabel for="first-article-plan-code">方案编号</NvFieldLabel>
            <NvInput
              id="first-article-plan-code"
              v-model="form.planCode"
              :aria-invalid="submitted && !form.planCode.trim()"
            />
          </NvField>
          <NvField :data-invalid="submitted && !form.skuCode.trim()">
            <NvFieldLabel for="first-article-sku">适用物料</NvFieldLabel>
            <NvEntityPicker
              id="first-article-sku"
              v-model="form.skuCode"
              :options="skuOptions"
              :loading="skusPending"
              title="选择适用物料"
              placeholder="选择物料"
              source-text="数据来自物料主数据"
              aria-label="适用物料"
              :aria-invalid="submitted && !form.skuCode.trim()"
            />
          </NvField>
          <NvField :data-invalid="submitted && !form.workCenterId.trim()">
            <NvFieldLabel for="first-article-work-center">工序工作中心</NvFieldLabel>
            <NvEntityPicker
              id="first-article-work-center"
              v-model="form.workCenterId"
              :options="workCenterOptions"
              :loading="workCentersPending"
              title="选择工序工作中心"
              placeholder="选择工作中心"
              source-text="数据来自工作中心主数据"
              aria-label="工序工作中心"
              :aria-invalid="submitted && !form.workCenterId.trim()"
            />
          </NvField>
        </NvFieldGroup>

        <section class="grid gap-3" aria-labelledby="first-article-characteristics-title">
          <div class="flex items-center justify-between gap-3">
            <div>
              <h3 id="first-article-characteristics-title" class="font-semibold">检验项</h3>
              <p class="text-sm text-muted-foreground">至少配置一项首件必须确认的质量要求。</p>
            </div>
            <NvButton type="button" size="sm" variant="outline" @click="addCharacteristic">
              <PlusIcon aria-hidden="true" />
              添加检验项
            </NvButton>
          </div>

          <div
            v-for="(item, index) in form.characteristics"
            :key="index"
            class="grid gap-3 rounded-lg border p-3 md:grid-cols-[1fr_1fr_1fr_140px_auto]"
          >
            <NvField :data-invalid="characteristicCodeInvalid(index)">
              <NvFieldLabel :for="`first-article-item-code-${index}`">检验项编号</NvFieldLabel>
              <NvInput
                :id="`first-article-item-code-${index}`"
                v-model="item.characteristicCode"
                :aria-invalid="characteristicCodeInvalid(index)"
              />
            </NvField>
            <NvField :data-invalid="submitted && !item.name.trim()">
              <NvFieldLabel :for="`first-article-item-name-${index}`">检验项名称</NvFieldLabel>
              <NvInput
                :id="`first-article-item-name-${index}`"
                v-model="item.name"
                :aria-invalid="submitted && !item.name.trim()"
              />
            </NvField>
            <NvField :data-invalid="submitted && !item.method.trim()">
              <NvFieldLabel :for="`first-article-item-method-${index}`">检验方法</NvFieldLabel>
              <NvInput
                :id="`first-article-item-method-${index}`"
                v-model="item.method"
                :aria-invalid="submitted && !item.method.trim()"
              />
            </NvField>
            <NvField>
              <NvFieldLabel>重要程度</NvFieldLabel>
              <NvSelect v-model="item.severity">
                <NvSelectTrigger :aria-label="`第 ${index + 1} 个检验项重要程度`">
                  <NvSelectValue />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="critical">关键</NvSelectItem>
                  <NvSelectItem value="major">重要</NvSelectItem>
                  <NvSelectItem value="minor">一般</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <div class="flex items-end justify-end">
              <NvButton
                type="button"
                size="icon-sm"
                variant="ghost"
                :aria-label="`移除第 ${index + 1} 个检验项`"
                @click="removeCharacteristic(index)"
              >
                <Trash2Icon aria-hidden="true" />
              </NvButton>
            </div>
            <NvField class="md:col-span-3" :data-invalid="submitted && !item.samplingRule.trim()">
              <NvFieldLabel :for="`first-article-item-sampling-${index}`">抽样要求</NvFieldLabel>
              <NvInput
                :id="`first-article-item-sampling-${index}`"
                v-model="item.samplingRule"
                :aria-invalid="submitted && !item.samplingRule.trim()"
              />
              <NvFieldDescription
                >首件通常采用全检；如工艺另有要求，请按现场标准填写。</NvFieldDescription
              >
            </NvField>
          </div>
        </section>

        <NvSheetFooter>
          <NvButton type="button" variant="outline" @click="openModel = false">取消</NvButton>
          <NvButton
            type="submit"
            :disabled="createFirstArticlePlanPending"
            :aria-describedby="
              submitted && blockers.length ? 'first-article-plan-errors' : undefined
            "
          >
            <Spinner v-if="createFirstArticlePlanPending" aria-hidden="true" />
            创建并启用
          </NvButton>
        </NvSheetFooter>
      </form>
    </NvSheetContent>
  </NvSheet>
</template>
