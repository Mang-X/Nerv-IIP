<script setup lang="ts">
import { openBusinessConsoleErpAccountingPeriodMutationOptions } from '@nerv-iip/api-client'
import { useMutation } from '@pinia/colada'
import { NvButton, NvField, NvFieldGroup, NvFieldLabel, NvInput, NvPageHeader } from '@nerv-iip/ui'
import { computed, reactive, shallowRef } from 'vue'
import { useRoute } from 'vue-router'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import FoundationReturnContext from '@/components/erp/FoundationReturnContext.vue'
import { queryText } from '@/components/erp/costRecovery'
import { useBusinessContextStore } from '@/stores/businessContext'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { today } from '@/utils/format'

definePage({
  meta: {
    requiresAuth: true,
    title: '开立会计期间',
    requiredPermissions: ['business.erp.finance.manage'],
  },
})
const route = useRoute()
const context = useBusinessContextStore()
const date = queryText(route.query, 'date') || today()
const draft = reactive({ periodCode: '', startDate: date, endDate: date })
const mutation = useMutation(openBusinessConsoleErpAccountingPeriodMutationOptions())
const ready = computed(() => Boolean(context.organizationId && context.environmentId))
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  periodCode: !draft.periodCode.trim(),
  startDate: !draft.startDate,
  endDate: !draft.endDate || draft.endDate < draft.startDate,
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))
async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  try {
    await mutation.mutateAsync({
      body: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        ...draft,
      },
    })
    notifySuccess('会计期间已开立，请返回准备检查核对覆盖结果')
  } catch (error) {
    notifyOperationFailure('开立会计期间', error, '会计期间未能开立，请核对期间编码与日期')
  }
}
</script>
<template>
  <BusinessLayout>
    <NvPageHeader title="开立会计期间" :breadcrumbs="[{ label: '财务' }]" />
    <FoundationReturnContext />
    <p class="text-sm text-muted-foreground">
      准备检查需要唯一覆盖
      {{ date }} 的会计期间。请财务填写实际期间编码及起止日期；日期重叠或既有期间问题需由财务核对。
    </p>
    <form
      novalidate
      class="mt-4 grid max-w-xl gap-4 rounded-md border p-4"
      @submit.prevent="submit"
    >
      <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
        请完整填写期间编码、开始日期与结束日期（已标红），结束日期不得早于开始日期。
      </p>
      <NvFieldGroup>
        <NvField>
          <NvFieldLabel for="period-code">期间编码</NvFieldLabel>
          <NvInput
            id="period-code"
            v-model="draft.periodCode"
            required
            maxlength="50"
            :invalid="showErrors && invalid.periodCode"
            :aria-invalid="showErrors && invalid.periodCode"
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="period-start">开始日期</NvFieldLabel>
          <NvInput
            id="period-start"
            v-model="draft.startDate"
            type="date"
            required
            :invalid="showErrors && invalid.startDate"
            :aria-invalid="showErrors && invalid.startDate"
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="period-end">结束日期</NvFieldLabel>
          <NvInput
            id="period-end"
            v-model="draft.endDate"
            type="date"
            required
            :min="draft.startDate"
            :invalid="showErrors && invalid.endDate"
            :aria-invalid="showErrors && invalid.endDate"
          />
        </NvField>
      </NvFieldGroup>
      <NvButton type="submit" :disabled="!ready || mutation.isLoading.value">开立会计期间</NvButton>
    </form>
  </BusinessLayout>
</template>
