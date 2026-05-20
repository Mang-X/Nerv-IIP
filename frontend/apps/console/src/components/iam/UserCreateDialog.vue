<script setup lang="ts">
import type { ConsoleCreateIamUserRequest } from '@nerv-iip/api-client'
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

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  submit: [payload: Required<ConsoleCreateIamUserRequest>]
}>()

const form = reactive({
  email: '',
  loginName: '',
  password: '',
})
const errors = reactive({
  email: '',
  loginName: '',
  password: '',
})

function resetForm() {
  form.email = ''
  form.loginName = ''
  form.password = ''
  clearErrors()
}

function clearErrors() {
  errors.email = ''
  errors.loginName = ''
  errors.password = ''
}

function validate() {
  clearErrors()
  errors.loginName = form.loginName.trim() ? '' : 'Login name is required.'
  errors.email = form.email.trim() ? '' : 'Email is required.'
  errors.password = form.password ? '' : 'Password is required.'

  return !errors.loginName && !errors.email && !errors.password
}

function handleSubmit() {
  if (!validate()) {
    return
  }

  emit('submit', {
    email: form.email.trim(),
    loginName: form.loginName.trim(),
    password: form.password,
  })
  open.value = false
  resetForm()
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
        <DialogTitle>Create user</DialogTitle>
        <DialogDescription>
          Add a console user with a login name, email address, and initial password.
        </DialogDescription>
      </DialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <FieldGroup>
          <Field>
            <FieldLabel for="iam-create-login-name">Login name</FieldLabel>
            <Input
              id="iam-create-login-name"
              v-model="form.loginName"
              :aria-invalid="Boolean(errors.loginName)"
              autocomplete="username"
            />
            <FieldError :errors="[errors.loginName]" />
          </Field>

          <Field>
            <FieldLabel for="iam-create-email">Email</FieldLabel>
            <Input
              id="iam-create-email"
              v-model="form.email"
              :aria-invalid="Boolean(errors.email)"
              autocomplete="email"
              type="email"
            />
            <FieldError :errors="[errors.email]" />
          </Field>

          <Field>
            <FieldLabel for="iam-create-password">Password</FieldLabel>
            <Input
              id="iam-create-password"
              v-model="form.password"
              :aria-invalid="Boolean(errors.password)"
              autocomplete="new-password"
              type="password"
            />
            <FieldError :errors="[errors.password]" />
          </Field>
        </FieldGroup>

        <DialogFooter show-close-button>
          <Button type="submit">
            Create user
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
