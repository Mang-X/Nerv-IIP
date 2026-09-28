<script setup lang="ts">
import type {
  ConsoleIamUserResponse,
  ConsoleResetIamUserPasswordRequest,
} from '@nerv-iip/api-client'
import {
  Button,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  NvInput,
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
  errors.newPassword = form.newPassword ? '' : '请输入新密码。'

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
  <NvDialog v-model:open="open">
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>重置密码</NvDialogTitle>
        <NvDialogDescription>
          为 {{ user?.loginName || '该用户' }} 设置新密码。
        </NvDialogDescription>
      </NvDialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <FieldGroup>
          <Field>
            <FieldLabel for="iam-reset-password">新密码</FieldLabel>
            <NvInput
              id="iam-reset-password"
              v-model="form.newPassword"
              :aria-invalid="Boolean(errors.newPassword)"
              :invalid="Boolean(errors.newPassword)"
              autocomplete="new-password"
              type="password"
            />
            <FieldError v-if="errors.newPassword" :errors="[errors.newPassword]" />
          </Field>
        </FieldGroup>

        <NvDialogFooter show-close-button>
          <Button type="submit"> 重置密码 </Button>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
