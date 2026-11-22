<script setup lang="ts">
import { readFaceText } from '@/utils/readFace'
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  returnForm: View['returnForm']
  returnFormMaxQuantity: View['returnFormMaxQuantity']
  returnLineSideMaterialPending: View['returnLineSideMaterialPending']
  submitReturn: View['submitReturn']
}>()

const returnOpen = defineModel<View['returnOpen']>('returnOpen', { required: true })
</script>

<template>
  <!-- 线边退料 -->
  <NvAlertDialog v-model:open="returnOpen">
    <NvAlertDialogContent class="sm:max-w-md">
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>线边退料 · {{ readFaceText(returnForm.requestId) }}</NvAlertDialogTitle>
        <NvAlertDialogDescription>
          退料会把当前线边物料退回仓库；数量不得超过当前可退余额。
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvFieldGroup class="grid gap-3">
        <NvField>
          <NvFieldLabel for="return-quantity">退料数量</NvFieldLabel>
          <NvInput
            id="return-quantity"
            v-model="returnForm.quantity"
            inputmode="decimal"
            :max="returnFormMaxQuantity"
          />
        </NvField>
      </NvFieldGroup>
      <NvAlertDialogFooter>
        <NvButton
          type="button"
          variant="outline"
          :disabled="returnLineSideMaterialPending"
          @click="returnOpen = false"
        >
          返回
        </NvButton>
        <NvButton
          type="button"
          :disabled="returnLineSideMaterialPending"
          data-testid="submit-line-side-return"
          @click="submitReturn"
        >
          <NvSpinner v-if="returnLineSideMaterialPending" aria-hidden="true" />
          提交退料
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
