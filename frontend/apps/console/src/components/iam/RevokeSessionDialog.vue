<script setup lang="ts">
import type { ConsoleIamSessionResponse } from '@nerv-iip/api-client'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  Button,
} from '@nerv-iip/ui'
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    currentSessionId?: string
    pending?: boolean
    session?: ConsoleIamSessionResponse
  }>(),
  {
    pending: false,
  },
)

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  confirm: [sessionId: string]
}>()

const canConfirm = computed(() => {
  const sessionId = props.session?.sessionId
  return Boolean(sessionId) && sessionId !== props.currentSessionId
})

function confirmRevoke() {
  const sessionId = props.session?.sessionId
  if (!sessionId || sessionId === props.currentSessionId) {
    return
  }

  emit('confirm', sessionId)
}
</script>

<template>
  <AlertDialog v-model:open="open">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>吊销会话</AlertDialogTitle>
        <AlertDialogDescription>
          吊销 {{ props.session?.sessionId }} 将终止该会话的刷新链路。
          <span v-if="props.session?.sessionId === props.currentSessionId"
            >这是你当前的会话，可能会被登出。</span
          >
        </AlertDialogDescription>
      </AlertDialogHeader>

      <AlertDialogFooter>
        <AlertDialogCancel :disabled="props.pending"> 取消 </AlertDialogCancel>
        <Button
          type="button"
          variant="destructive"
          :disabled="props.pending || !canConfirm"
          @click="confirmRevoke"
        >
          {{ props.pending ? '吊销中…' : '吊销会话' }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
