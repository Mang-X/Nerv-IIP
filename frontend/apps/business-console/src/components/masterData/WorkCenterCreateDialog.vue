<script setup lang="ts">
/**
 * 新建工作中心。工作中心是排产与成本口径的产能单元，挂在产线下（同时记所属工厂）。
 *
 * 既是 `DirectoryPicker` 的 `work-center` 新增弹窗（约定见 `directoryCreators.ts`），也是工厂结构页
 * 在产线下新建工作中心的入口。`context` 里给了的 `siteCode` / `lineCode` 带出为只读归属，没给的
 * 由用户自己选。
 */
import type { BusinessConsoleCreateWorkCenterRequest } from '@nerv-iip/api-client'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import type {
  DirectoryCreateContext,
  DirectoryCreatedItem,
} from '@/components/business/directoryCreators'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import {
  useBusinessMasterDataResources,
  useCreateMasterDataResource,
} from '@/composables/useBusinessMasterData'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useReturnFocusOnClose } from '@/composables/useReturnFocusOnClose'
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
  NvFieldDescription,
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
import { computed, reactive, shallowRef, watch } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{ context?: DirectoryCreateContext }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ created: [item: DirectoryCreatedItem] }>()

const DEFAULT_CAPACITY_MINUTES = 480

const carriedSiteCode = props.context?.siteCode?.trim() ?? ''
const carriedLineCode = props.context?.lineCode?.trim() ?? ''
const businessContext = useBusinessContextStore()
const returnFocus = useReturnFocusOnClose()
const workCenters =
  useCreateMasterDataResource<BusinessConsoleCreateWorkCenterRequest>('work-center')
const { resolveSite, resolveLine } = useMasterDataDisplayNames({ sites: true, lines: true })
const calendars = useBusinessMasterDataResources('work-calendar')
calendars.filters.take = 200

const form = reactive({
  name: '',
  plantCode: carriedSiteCode,
  lineCode: carriedLineCode,
  defaultCalendarCode: '',
  capacityMinutesPerDay: String(DEFAULT_CAPACITY_MINUTES),
})
const showErrors = shallowRef(false)
const capacityValid = computed(() => (Number(form.capacityMinutesPerDay) || 0) > 0)
const canSubmit = computed(
  () =>
    !!form.name.trim() &&
    !!form.plantCode &&
    !!form.lineCode &&
    !!form.defaultCalendarCode.trim() &&
    capacityValid.value,
)
const carriedItems = computed(() => [
  {
    label: '所属工厂',
    value: carriedSiteCode && (resolveSite(carriedSiteCode) ?? carriedSiteCode),
  },
  {
    label: '所属产线',
    value: carriedLineCode && (resolveLine(carriedLineCode) ?? carriedLineCode),
  },
])

// 工作日历从已维护的日历中选，不让用户手抄编码；只有一条时自动选中。
const calendarOptions = computed(() => calendars.resources.value.filter((c) => Boolean(c.code)))
watch(
  calendarOptions,
  (options) => {
    if (!form.defaultCalendarCode && options.length === 1) {
      form.defaultCalendarCode = options[0]!.code!
    }
  },
  { immediate: true },
)

// 换了工厂，原先选的产线可能不在新工厂下，清掉让用户重选。
function setPlant(plantCode: string) {
  if (plantCode !== form.plantCode && !carriedLineCode) form.lineCode = ''
  form.plantCode = plantCode
}

async function submit() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const name = form.name.trim()
  try {
    const response = await workCenters.create({
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      name,
      plantCode: form.plantCode,
      lineCode: form.lineCode,
      defaultCalendarCode: form.defaultCalendarCode.trim(),
      capacityMinutesPerDay: Number(form.capacityMinutesPerDay),
      resourceType: 'work-center',
      capacityUnit: 'minutes',
      finiteCapacity: true,
    })
    const created = response.data!
    notifySuccess(`工作中心「${name}」已创建。`)
    emit('created', { code: created.code!, name: created.displayName || name })
    open.value = false
  } catch (error) {
    notifyOperationFailure('创建工作中心失败', error, '创建工作中心失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg" @close-auto-focus="returnFocus">
      <NvDialogHeader>
        <NvDialogTitle>新建工作中心</NvDialogTitle>
        <NvDialogDescription class="sr-only">新建工作中心并确定所属工厂与产线</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submit">
        <CarriedContextSummary label="归属" :items="carriedItems" />
        <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <FormSectionTitle>基础信息</FormSectionTitle>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField class="sm:col-span-2" :data-invalid="showErrors && !form.name.trim()">
            <NvFieldLabel for="wc-name"
              >工作中心名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="wc-name" v-model="form.name" autocomplete="off" required />
          </NvField>
          <NvField v-if="!carriedSiteCode" :data-invalid="showErrors && !form.plantCode">
            <NvFieldLabel for="wc-plant"
              >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="wc-plant"
              directory-type="site"
              :model-value="form.plantCode"
              :invalid="showErrors && !form.plantCode"
              @update:model-value="setPlant"
            />
          </NvField>
          <NvField v-if="!carriedLineCode" :data-invalid="showErrors && !form.lineCode">
            <NvFieldLabel for="wc-line"
              >所属产线 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="wc-line"
              v-model="form.lineCode"
              directory-type="production-line"
              :parent="{ siteCode: form.plantCode }"
              :invalid="showErrors && !form.lineCode"
            />
          </NvField>
        </NvFieldGroup>
        <FormSectionTitle>产能</FormSectionTitle>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="showErrors && !form.defaultCalendarCode.trim()">
            <NvFieldLabel for="wc-cal"
              >默认工作日历 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-if="calendarOptions.length" v-model="form.defaultCalendarCode">
              <NvSelectTrigger id="wc-cal"
                ><NvSelectValue placeholder="请选择工作日历"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="c in calendarOptions" :key="c.code" :value="c.code!">
                  {{ c.displayName ?? c.code }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
            <template v-else>
              <NvInput id="wc-cal" v-model="form.defaultCalendarCode" autocomplete="off" required />
              <!-- 尚无可选日历时的取值来源（非显而易见），保留一行。 -->
              <NvFieldDescription>先在「排班与日历」页建工作日历。</NvFieldDescription>
            </template>
          </NvField>
          <NvField :data-invalid="showErrors && !capacityValid">
            <NvFieldLabel for="wc-cap"
              >日产能（分钟） <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput
              id="wc-cap"
              v-model="form.capacityMinutesPerDay"
              type="number"
              min="1"
              inputmode="numeric"
            />
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="workCenters.pending.value">
            <Spinner v-if="workCenters.pending.value" aria-hidden="true" />
            保存工作中心
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
