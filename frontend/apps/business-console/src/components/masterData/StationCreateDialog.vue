<script setup lang="ts">
/**
 * 新建工位。工位的上级是产线（厂区、车间沿产线继承），工作中心只是排产与成本归集的可选关联。
 *
 * 既是 `DirectoryPicker` 的 `station` 新增弹窗（约定见 `directoryCreators.ts`），也是工厂结构页
 * 在产线下新建工位的入口。`context.lineCode` 给了就把产线带出为只读归属——新建的工位必须挂在
 * 调用方已选的产线下，否则设备表单里会选中一个不在所选产线下的工位；没给就让用户自己选产线。
 */
import type { BusinessConsoleCreateStationRequest } from '@nerv-iip/api-client'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import type {
  DirectoryCreateContext,
  DirectoryCreatedItem,
} from '@/components/business/directoryCreators'
import {
  useBusinessMasterDataResources,
  useMasterDataResource,
} from '@/composables/useBusinessMasterData'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, shallowRef } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{ context?: DirectoryCreateContext }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ created: [item: DirectoryCreatedItem] }>()

const carriedLineCode = props.context?.lineCode?.trim() ?? ''
const stations = useMasterDataResource<BusinessConsoleCreateStationRequest>('station')
// 与产线选择器同一份整表查询，只用来把带出的产线编码显示成名称。
const lines = useBusinessMasterDataResources('production-line')
lines.filters.take = 500

const form = reactive({ name: '', lineCode: carriedLineCode, workCenterCode: '' })
const showErrors = shallowRef(false)
const canSubmit = computed(() => !!form.name.trim() && !!form.lineCode.trim())
const carriedLineName = computed(() => {
  const line = lines.resources.value.find((row) => row.code === carriedLineCode)
  return line?.displayName || carriedLineCode
})

// 换了产线，原先选的工作中心可能不在新产线下，清掉让用户重选。
function setLine(lineCode: string) {
  if (lineCode !== form.lineCode) form.workCenterCode = ''
  form.lineCode = lineCode
}

async function submit() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const name = form.name.trim()
  try {
    const response = await stations.create({
      organizationId: stations.filters.organizationId,
      environmentId: stations.filters.environmentId,
      name,
      lineCode: form.lineCode.trim(),
      ...(form.workCenterCode ? { workCenterCode: form.workCenterCode } : {}),
    })
    const created = response.data!
    notifySuccess(`工位「${name}」已创建。`)
    emit('created', { code: created.code!, name: created.displayName || name })
    open.value = false
  } catch (error) {
    notifyOperationFailure('创建工位失败', error, '创建工位失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>新建工位</NvDialogTitle>
        <NvDialogDescription class="sr-only">{{
          carriedLineCode ? `所属产线：${carriedLineName}` : '新建工位并选择所属产线'
        }}</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submit">
        <CarriedContextSummary
          v-if="carriedLineCode"
          label="归属"
          :items="[{ label: '所属产线', value: carriedLineName }]"
        />
        <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3">
          <NvField :data-invalid="showErrors && !form.name.trim()">
            <NvFieldLabel for="station-name"
              >工位名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="station-name" v-model="form.name" autocomplete="off" required />
          </NvField>
          <NvField v-if="!carriedLineCode" :data-invalid="showErrors && !form.lineCode">
            <NvFieldLabel for="station-line"
              >所属产线 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="station-line"
              directory-type="production-line"
              :model-value="form.lineCode"
              :invalid="showErrors && !form.lineCode"
              @update:model-value="setLine"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="station-wc">关联工作中心</NvFieldLabel>
            <DirectoryPicker
              id="station-wc"
              v-model="form.workCenterCode"
              directory-type="work-center"
              :parent="{ lineCode: form.lineCode }"
              placeholder="可留空"
              clearable
            />
            <NvFieldDescription>用于排产与成本归集。</NvFieldDescription>
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="stations.createPending.value">
            <Spinner v-if="stations.createPending.value" aria-hidden="true" />
            保存工位
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
