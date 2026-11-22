<script setup lang="ts">
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useAlarmPage } from '../useAlarmPage'

type View = ShallowUnwrapRef<ReturnType<typeof useAlarmPage>>

defineProps<{
  ackTargets: View['ackTargets']
  batchAck: View['batchAck']
  confirmBatchAck: View['confirmBatchAck']
}>()
</script>

<template>
  <NvAlertDialog v-model:open="batchAck.open">
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>批量确认报警</NvAlertDialogTitle>
        <NvAlertDialogDescription>
          将确认选中的 {{ ackTargets.length }} 条报警。
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel :disabled="batchAck.submitting">取消</NvAlertDialogCancel>
        <!-- 普通 NvButton，不用 NvAlertDialogAction：后者点击即无条件关框（confirm-destroy 规则 3），
             `batchAck.submitting` 的 disabled 一瞬都看不到。variant 不写 = 其默认值 'default'。 -->
        <NvButton type="button" :disabled="batchAck.submitting" @click="confirmBatchAck">
          确认 {{ ackTargets.length }} 条
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
