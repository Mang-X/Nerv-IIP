<script setup lang="ts">
/**
 * 新建车间。车间的上级是工厂。
 *
 * 既是 `DirectoryPicker` 的 `workshop` 新增弹窗（约定见 `directoryCreators.ts`），也是工厂结构页
 * 在工厂下新建车间的入口。`context.siteCode` 给了就把工厂带出为只读归属，没给就让用户自己选。
 */
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import type {
  DirectoryCreateContext,
  DirectoryCreatedItem,
} from '@/components/business/directoryCreators'
import { useBusinessWorkshops } from '@/composables/useBusinessMasterData'
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
const businessContext = useBusinessContextStore()
const returnFocus = useReturnFocusOnClose()
const workshops = useBusinessWorkshops()
const { resolveSite } = useMasterDataDisplayNames({ sites: true })
const carriedSiteName = computed(() => resolveSite(carriedSiteCode) ?? carriedSiteCode)

const form = reactive({ name: '', siteCode: carriedSiteCode })
const showErrors = shallowRef(false)
const canSubmit = computed(() => !!form.name.trim() && !!form.siteCode)

async function submit() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const name = form.name.trim()
  try {
    const response = await workshops.createWorkshop({
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      name,
      siteCode: form.siteCode,
    })
    const created = response.data!
    notifySuccess(`车间「${name}」已创建。`)
    emit('created', { code: created.code!, name: created.displayName || name })
    open.value = false
  } catch (error) {
    notifyOperationFailure('创建车间失败', error, '创建车间失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg" @close-auto-focus="returnFocus">
      <NvDialogHeader>
        <NvDialogTitle>新建车间</NvDialogTitle>
        <NvDialogDescription class="sr-only">{{
          carriedSiteCode ? `所属工厂：${carriedSiteName}` : '新建车间并选择所属工厂'
        }}</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submit">
        <CarriedContextSummary
          v-if="carriedSiteCode"
          label="归属"
          :items="[{ label: '所属工厂', value: carriedSiteName }]"
        />
        <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3">
          <NvField :data-invalid="showErrors && !form.name.trim()">
            <NvFieldLabel for="workshop-name"
              >车间名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="workshop-name" v-model="form.name" autocomplete="off" required />
          </NvField>
          <NvField v-if="!carriedSiteCode" :data-invalid="showErrors && !form.siteCode">
            <NvFieldLabel for="workshop-site"
              >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="workshop-site"
              v-model="form.siteCode"
              directory-type="site"
              :invalid="showErrors && !form.siteCode"
            />
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="workshops.createWorkshopPending.value">
            <Spinner v-if="workshops.createWorkshopPending.value" aria-hidden="true" />
            保存车间
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
