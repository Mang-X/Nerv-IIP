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
  errors.loginName = form.loginName.trim() ? '' : '请输入登录名。'
  errors.email = form.email.trim() ? '' : '请输入邮箱。'
  errors.password = form.password ? '' : '请输入密码。'

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
        <DialogTitle>新建用户</DialogTitle>
        <DialogDescription>
          创建一个控制台用户，填写登录名、邮箱与初始密码。
        </DialogDescription>
      </DialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <FieldGroup>
          <Field>
            <FieldLabel for="iam-create-login-name">登录名</FieldLabel>
            <Input
              id="iam-create-login-name"
              v-model="form.loginName"
              :aria-invalid="Boolean(errors.loginName)"
              autocomplete="username"
            />
            <FieldError v-if="errors.loginName" :errors="[errors.loginName]" />
          </Field>

          <Field>
            <FieldLabel for="iam-create-email">邮箱</FieldLabel>
            <Input
              id="iam-create-email"
              v-model="form.email"
              :aria-invalid="Boolean(errors.email)"
              autocomplete="email"
              type="email"
            />
            <FieldError v-if="errors.email" :errors="[errors.email]" />
          </Field>

          <Field>
            <FieldLabel for="iam-create-password">密码</FieldLabel>
            <Input
              id="iam-create-password"
              v-model="form.password"
              :aria-invalid="Boolean(errors.password)"
              autocomplete="new-password"
              type="password"
            />
            <FieldError v-if="errors.password" :errors="[errors.password]" />
          </Field>
        </FieldGroup>

        <DialogFooter show-close-button>
          <Button type="submit">
            新建用户
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
