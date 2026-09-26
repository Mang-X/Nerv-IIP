<script setup lang="ts">
/**
 * 新建产线。产线的上级是工厂，车间可选（小厂不设车间时产线直挂工厂）。
 *
 * 既是 `DirectoryPicker` 的 `production-line` 新增弹窗（约定见 `directoryCreators.ts`），也是工厂
 * 结构页在车间下新建产线的入口。`context` 里给了的 `siteCode` / `workshopCode` 带出为只读归属，
 * 没给的由用户自己选。
 */
import type { BusinessConsoleCreateProductionLineRequest } from '@nerv-iip/api-client'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import type {
  DirectoryCreateContext,
  DirectoryCreatedItem,
} from '@/components/business/directoryCreators'
import { useCreateMasterDataResource } from '@/composables/useBusinessMasterData'
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

const carriedSiteCode = props.context?.siteCode?.trim() ?? ''
const carriedWorkshopCode = props.context?.workshopCode?.trim() ?? ''
const businessContext = useBusinessContextStore()
const returnFocus = useReturnFocusOnClose()
const lines =
  useCreateMasterDataResource<BusinessConsoleCreateProductionLineRequest>('production-line')
const { resolveSite, resolveWorkshop } = useMasterDataDisplayNames({ sites: true, workshops: true })

const form = reactive({ name: '', siteCode: carriedSiteCode, workshopCode: carriedWorkshopCode })
const showErrors = shallowRef(false)
const canSubmit = computed(() => !!form.name.trim() && !!form.siteCode)
const carriedItems = computed(() => [
  {
    label: '所属工厂',
    value: carriedSiteCode && (resolveSite(carriedSiteCode) ?? carriedSiteCode),
  },
  {
    label: '所属车间',
    value: carriedWorkshopCode && (resolveWorkshop(carriedWorkshopCode) ?? carriedWorkshopCode),
  },
])

// 换了工厂，原先选的车间可能不在新工厂下，清掉让用户重选。
function setSite(siteCode: string) {
  if (siteCode !== form.siteCode && !carriedWorkshopCode) form.workshopCode = ''
  form.siteCode = siteCode
}

async function submit() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const name = form.name.trim()
  try {
    const response = await lines.create({
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      name,
      siteCode: form.siteCode,
      ...(form.workshopCode ? { workshopCode: form.workshopCode } : {}),
    })
    const created = response.data!
    notifySuccess(`产线「${name}」已创建。`)
    emit('created', { code: created.code!, name: created.displayName || name })
    open.value = false
  } catch (error) {
    notifyOperationFailure('创建产线失败', error, '创建产线失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg" @close-auto-focus="returnFocus">
      <NvDialogHeader>
        <NvDialogTitle>新建产线</NvDialogTitle>
        <NvDialogDescription class="sr-only">新建产线并确定所属工厂与车间</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submit">
        <CarriedContextSummary label="归属" :items="carriedItems" />
        <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3">
          <NvField :data-invalid="showErrors && !form.name.trim()">
            <NvFieldLabel for="line-name"
              >产线名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="line-name" v-model="form.name" autocomplete="off" required />
          </NvField>
          <NvField v-if="!carriedSiteCode" :data-invalid="showErrors && !form.siteCode">
            <NvFieldLabel for="line-site"
              >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="line-site"
              directory-type="site"
              :model-value="form.siteCode"
              :invalid="showErrors && !form.siteCode"
              @update:model-value="setSite"
            />
          </NvField>
          <NvField v-if="!carriedWorkshopCode">
            <NvFieldLabel for="line-workshop">所属车间</NvFieldLabel>
            <DirectoryPicker
              id="line-workshop"
              v-model="form.workshopCode"
              directory-type="workshop"
              :parent="{ siteCode: form.siteCode }"
              placeholder="无（直挂工厂）"
              clearable
            />
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="lines.pending.value">
            <Spinner v-if="lines.pending.value" aria-hidden="true" />
            保存产线
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
