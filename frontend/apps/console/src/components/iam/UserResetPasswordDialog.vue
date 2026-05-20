<script setup lang="ts">
import type { ConsoleIamUserResponse, ConsoleResetIamUserPasswordRequest } from '@nerv-iip/api-client'
import {
  Button,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  Input,
} from '@nerv-iip/ui'
import { reactive, watch } from 'vue'

defineProps<{
  user?: ConsoleIamUserResponse
}>()

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  submit: [payload: Required<ConsoleResetIamUserPasswordRequest>]
}>()

const form = reactive({
  newPassword: '',
})
const errors = reactive({
  newPassword: '',
})

function resetForm() {
  form.newPassword = ''
  errors.newPassword = ''
}

function validate() {
  errors.newPassword = form.newPassword ? '' : 'New password is required.'

  return !errors.newPassword
}

function handleSubmit() {
  if (!validate()) {
    return
  }

  emit('submit', {
    newPassword: form.newPassword,
  })
  resetForm()
  open.value = false
}

watch(open, (isOpen) => {
  if (!isOpen) {
    resetForm()
  }
})
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent>
      <DialogHeader>
        <DialogTitle>Reset password</DialogTitle>
        <DialogDescription>
          Set a new password for {{ user?.loginName || 'this user' }}.
        </DialogDescription>
      </DialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <FieldGroup>
          <Field>
            <FieldLabel for="iam-reset-password">New password</FieldLabel>
            <Input
              id="iam-reset-password"
              v-model="form.newPassword"
              :aria-invalid="Boolean(errors.newPassword)"
              autocomplete="new-password"
              type="password"
            />
            <FieldError v-if="errors.newPassword" :errors="[errors.newPassword]" />
          </Field>
        </FieldGroup>

        <DialogFooter show-close-button>
          <Button type="submit">
            Reset password
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
