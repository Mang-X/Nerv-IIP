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

const open = defineModel<boolean>('open', { required: true })
const props = defineProps<{ pending: boolean; clear: () => Promise<boolean> }>()
async function confirm() {
  if (await props.clear()) open.value = false
}
</script>

<template>
  <NvAlertDialog v-model:open="open">
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>确认清空当前方案草稿？</NvAlertDialogTitle>
        <NvAlertDialogDescription
          >当前方案的手工编辑和待排操作将被删除，无法恢复。其他方案的草稿会保留。</NvAlertDialogDescription
        >
      </NvAlertDialogHeader>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel>取消</NvAlertDialogCancel>
        <NvButton type="button" variant="destructive" :disabled="pending" @click="confirm"
          >确认清空</NvButton
        >
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
