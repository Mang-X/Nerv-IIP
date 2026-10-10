<script setup lang="ts">
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { useAuthStore } from '@/stores/auth'
import {
  Alert,
  AlertDescription,
  Button,
  Card,
  CardContent,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  Input,
  NvPageHeader,
  NvSpinner,
  toast,
} from '@nerv-iip/ui'
import { storeToRefs } from 'pinia'
import { reactive, shallowRef } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '修改密码',
  },
})

const auth = useAuthStore()
const { passwordChangeRequired } = storeToRefs(auth)
const router = useRouter()
const pending = shallowRef(false)
const rejection = shallowRef('')
const form = reactive({ currentPassword: '', newPassword: '', confirmPassword: '' })
const errors = reactive({ currentPassword: '', newPassword: '', confirmPassword: '' })

function validate() {
  errors.currentPassword = form.currentPassword ? '' : '请输入当前密码。'
  errors.newPassword = form.newPassword ? '' : '请输入新密码。'
  errors.confirmPassword =
    form.confirmPassword === form.newPassword ? '' : '两次输入的新密码不一致。'
  return !errors.currentPassword && !errors.newPassword && !errors.confirmPassword
}

async function submit() {
  rejection.value = ''
  if (!validate()) return

  pending.value = true
  try {
    await auth.changePassword(form.currentPassword, form.newPassword)
  } catch (error) {
    rejection.value = error instanceof Error ? error.message : '修改密码失败，请稍后重试。'
    return
  } finally {
    pending.value = false
  }

  toast.success('密码已修改')
  await router.push('/')
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <NvPageHeader title="修改密码" />

      <Card class="w-full max-w-md">
        <CardContent>
          <form class="grid gap-4" novalidate @submit.prevent="submit">
            <Alert v-if="passwordChangeRequired" role="status">
              <AlertDescription>
                首次登录或密码已被管理员重置，请先修改密码后再继续使用。
              </AlertDescription>
            </Alert>
            <Alert v-if="rejection" role="alert" variant="destructive">
              <AlertDescription>{{ rejection }}</AlertDescription>
            </Alert>

            <FieldGroup>
              <Field :data-invalid="Boolean(errors.currentPassword) || undefined">
                <FieldLabel for="current-password">当前密码</FieldLabel>
                <Input
                  id="current-password"
                  v-model="form.currentPassword"
                  :aria-invalid="Boolean(errors.currentPassword)"
                  autocomplete="current-password"
                  :disabled="pending"
                  type="password"
                />
                <FieldError v-if="errors.currentPassword" :errors="[errors.currentPassword]" />
              </Field>
              <Field :data-invalid="Boolean(errors.newPassword) || undefined">
                <FieldLabel for="new-password">新密码</FieldLabel>
                <Input
                  id="new-password"
                  v-model="form.newPassword"
                  :aria-invalid="Boolean(errors.newPassword)"
                  autocomplete="new-password"
                  :disabled="pending"
                  type="password"
                />
                <FieldError v-if="errors.newPassword" :errors="[errors.newPassword]" />
              </Field>
              <Field :data-invalid="Boolean(errors.confirmPassword) || undefined">
                <FieldLabel for="confirm-password">确认新密码</FieldLabel>
                <Input
                  id="confirm-password"
                  v-model="form.confirmPassword"
                  :aria-invalid="Boolean(errors.confirmPassword)"
                  autocomplete="new-password"
                  :disabled="pending"
                  type="password"
                />
                <FieldError v-if="errors.confirmPassword" :errors="[errors.confirmPassword]" />
              </Field>
            </FieldGroup>

            <Button type="submit" :disabled="pending">
              <NvSpinner v-if="pending" />
              修改密码
            </Button>
          </form>
        </CardContent>
      </Card>
    </section>
  </DefaultLayout>
</template>
