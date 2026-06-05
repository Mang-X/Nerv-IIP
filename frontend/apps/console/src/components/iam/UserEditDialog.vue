<script setup lang="ts">
import type { ConsoleIamUserResponse, ConsoleUpdateIamUserRequest } from '@nerv-iip/api-client'
import {
  Button,
  Checkbox,
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

const props = defineProps<{
  user?: ConsoleIamUserResponse
}>()

const open = defineModel<boolean>('open', { default: false })

const emit = defineEmits<{
  submit: [payload: Required<ConsoleUpdateIamUserRequest>]
}>()

const form = reactive({
  email: '',
  enabled: true,
  loginName: '',
})
const errors = reactive({
  email: '',
  loginName: '',
})

function syncUser() {
  form.email = props.user?.email ?? ''
  form.enabled = props.user?.enabled !== false
  form.loginName = props.user?.loginName ?? ''
  clearErrors()
}

function clearErrors() {
  errors.email = ''
  errors.loginName = ''
}

function validate() {
  clearErrors()
  errors.loginName = form.loginName.trim() ? '' : '请输入登录名。'
  errors.email = form.email.trim() ? '' : '请输入邮箱。'

  return !errors.loginName && !errors.email
}

function handleSubmit() {
  if (!validate()) {
    return
  }

  emit('submit', {
    email: form.email.trim(),
    enabled: form.enabled,
    loginName: form.loginName.trim(),
  })
  open.value = false
}

watch(() => props.user, syncUser, { immediate: true })
watch(open, (isOpen) => {
  if (isOpen) {
    syncUser()
  }
})
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent>
      <DialogHeader>
        <DialogTitle>编辑用户</DialogTitle>
        <DialogDescription>
          更新用户的登录名、邮箱与启用状态。
        </DialogDescription>
      </DialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <FieldGroup>
          <Field>
            <FieldLabel for="iam-edit-login-name">登录名</FieldLabel>
            <Input
              id="iam-edit-login-name"
              v-model="form.loginName"
              :aria-invalid="Boolean(errors.loginName)"
              autocomplete="username"
            />
            <FieldError v-if="errors.loginName" :errors="[errors.loginName]" />
          </Field>

          <Field>
            <FieldLabel for="iam-edit-email">邮箱</FieldLabel>
            <Input
              id="iam-edit-email"
              v-model="form.email"
              :aria-invalid="Boolean(errors.email)"
              autocomplete="email"
              type="email"
            />
            <FieldError v-if="errors.email" :errors="[errors.email]" />
          </Field>

          <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
            <div class="grid gap-1">
              <FieldLabel for="iam-edit-enabled">启用</FieldLabel>
            </div>
            <Checkbox id="iam-edit-enabled" v-model:checked="form.enabled" />
          </Field>
        </FieldGroup>

        <DialogFooter show-close-button>
          <Button type="submit">
            保存修改
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
