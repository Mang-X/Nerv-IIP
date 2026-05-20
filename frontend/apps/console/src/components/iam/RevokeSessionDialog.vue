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

const props = withDefaults(defineProps<{
  currentSessionId?: string
  pending?: boolean
  session?: ConsoleIamSessionResponse
}>(), {
  pending: false,
})

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  confirm: [sessionId: string]
}>()

function confirmRevoke() {
  const sessionId = props.session?.sessionId
  if (!sessionId) {
    return
  }

  emit('confirm', sessionId)
}
</script>

<template>
  <AlertDialog v-model:open="open">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>Revoke session</AlertDialogTitle>
        <AlertDialogDescription>
          Revoking {{ props.session?.sessionId }} ends the refresh path for this session.
          <span v-if="props.session?.sessionId === props.currentSessionId">This is your current session and you may be signed out.</span>
        </AlertDialogDescription>
      </AlertDialogHeader>

      <AlertDialogFooter>
        <AlertDialogCancel :disabled="props.pending">
          Cancel
        </AlertDialogCancel>
        <Button
          type="button"
          variant="destructive"
          :disabled="props.pending || !props.session?.sessionId"
          @click="confirmRevoke"
        >
          {{ props.pending ? 'Revoking...' : 'Revoke session' }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
