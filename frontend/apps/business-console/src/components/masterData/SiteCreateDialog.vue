<script setup lang="ts">
/** 新建工厂（工厂结构的根）。 */
import type { BusinessConsoleCreateSiteRequest } from '@nerv-iip/api-client'
import { useCreateMasterDataResource } from '@/composables/useBusinessMasterData'
import { useReturnFocusOnClose } from '@/composables/useReturnFocusOnClose'
import { DEFAULT_TIME_ZONE, TIME_ZONE_OPTIONS } from '@/data/timeZones'
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
  NvSearchSelect,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, shallowRef } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const open = defineModel<boolean>('open', { default: false })

const context = useBusinessContextStore()
const returnFocus = useReturnFocusOnClose()
const sites = useCreateMasterDataResource<BusinessConsoleCreateSiteRequest>('site')

// 时区只能是合法 IANA 标识符（手输 `GMT+8` 之类会让排产 / 日历算错），一律从常用清单里选。
const form = reactive({ name: '', timezone: DEFAULT_TIME_ZONE })
const showErrors = shallowRef(false)
const canSubmit = computed(() => !!form.name.trim() && !!form.timezone)

async function submit() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const name = form.name.trim()
  try {
    await sites.create({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      name,
      timezone: form.timezone,
    })
    notifySuccess(`工厂「${name}」已创建。`)
    open.value = false
  } catch (error) {
    notifyOperationFailure('创建工厂失败', error, '创建工厂失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg" @close-auto-focus="returnFocus">
      <NvDialogHeader>
        <NvDialogTitle>新建工厂</NvDialogTitle>
        <NvDialogDescription class="sr-only">工厂结构根节点</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submit">
        <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="showErrors && !form.name.trim()">
            <NvFieldLabel for="site-name"
              >工厂名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="site-name" v-model="form.name" autocomplete="off" required />
          </NvField>
          <NvField :data-invalid="showErrors && !form.timezone">
            <NvFieldLabel for="site-tz">时区 <span class="text-destructive">*</span></NvFieldLabel>
            <NvSearchSelect
              id="site-tz"
              v-model="form.timezone"
              :options="TIME_ZONE_OPTIONS"
              placeholder="选择时区"
              aria-label="时区"
            />
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="sites.pending.value">
            <Spinner v-if="sites.pending.value" aria-hidden="true" />
            保存工厂
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
