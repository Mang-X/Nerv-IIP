<script setup lang="ts">
import {
  NvButton,
  NvDatePicker,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvSpinner,
} from '@nerv-iip/ui'
import { PlayIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { usePlanningWorkbenchController } from './usePlanningWorkbenchController'

type View = ShallowUnwrapRef<ReturnType<typeof usePlanningWorkbenchController>>

defineProps<{
  activeMrpRun: View['activeMrpRun']
  hasPermission: View['hasPermission']
  mrpRunInProgress: View['mrpRunInProgress']
  runMrpPending: View['runMrpPending']
  runRequest: View['runRequest']
  submitMrpRun: View['submitMrpRun']
}>()

const mrpOpen = defineModel<View['mrpOpen']>('mrpOpen', { required: true })
</script>

<template>
  <NvDialog v-if="hasPermission('business.planning.mrp.run')" v-model:open="mrpOpen">
    <NvDialogTrigger as-child>
      <NvButton size="sm" type="button" variant="outline">
        <PlayIcon aria-hidden="true" />
        运行 MRP
      </NvButton>
    </NvDialogTrigger>
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>运行 MRP</NvDialogTitle>
        <NvDialogDescription class="sr-only">选择本次运算的计划范围。</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitMrpRun">
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel>开始日期</NvFieldLabel>
            <NvDatePicker
              v-model="runRequest.horizonStart"
              placeholder="选择开始日期"
              class="w-full"
            />
          </NvField>
          <NvField>
            <NvFieldLabel>结束日期</NvFieldLabel>
            <NvDatePicker
              v-model="runRequest.horizonEnd"
              placeholder="选择结束日期"
              class="w-full"
            />
          </NvField>
        </NvFieldGroup>
        <div
          v-if="mrpRunInProgress"
          class="flex items-center gap-2 rounded-md border border-border bg-muted/40 px-3 py-2 text-sm text-muted-foreground"
        >
          <NvSpinner aria-hidden="true" />
          <span>
            已受理，{{
              activeMrpRun.status === 'running' ? '正在计算' : '排队等待计算'
            }}…可关闭此窗口，后台继续执行，完成后自动刷新。
          </span>
        </div>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="mrpOpen = false">
            {{ mrpRunInProgress ? '关闭' : '取消' }}
          </NvButton>
          <NvButton type="submit" :disabled="runMrpPending || mrpRunInProgress">
            <NvSpinner v-if="runMrpPending" aria-hidden="true" />
            运行 MRP
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
