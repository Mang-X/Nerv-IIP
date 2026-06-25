<script setup lang="ts">
import {
  Alert,
  AlertDescription,
  ButtonPro,
  CardPro,
  CardProContent,
  CardProDescription,
  CardProFooter,
  CardProHeader,
  CardProTitle,
  FieldPro,
  FieldProDescription,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  Spinner,
} from '@nerv-iip/ui'
import { LogInIcon } from 'lucide-vue-next'
import { reactive } from 'vue'
import { useI18n } from 'vue-i18n'

withDefaults(
  defineProps<{
    error?: string
    pending?: boolean
  }>(),
  {
    pending: false,
  },
)

const emit = defineEmits<{
  submit: [credentials: { loginName: string; password: string }]
}>()

const { t } = useI18n()

const form = reactive({
  loginName: '',
  password: '',
})

function submit() {
  emit('submit', {
    loginName: form.loginName.trim(),
    password: form.password,
  })
}
</script>

<template>
  <CardPro class="border-none shadow-none">
    <form class="flex flex-col gap-4" @submit.prevent="submit">
      <CardProHeader class="text-center">
        <CardProTitle class="text-xl">{{ t('login.title') }}</CardProTitle>
        <CardProDescription>{{ t('login.description') }}</CardProDescription>
      </CardProHeader>

      <CardProContent class="flex flex-col gap-4">
        <Alert v-if="error" role="alert" variant="destructive">
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>

        <FieldProGroup>
          <FieldPro :data-invalid="Boolean(error) || undefined" :data-disabled="pending || undefined">
            <FieldProLabel for="login-name">{{ t('login.loginName') }}</FieldProLabel>
            <InputPro
              id="login-name"
              v-model="form.loginName"
              :aria-invalid="Boolean(error)"
              autocomplete="username"
              :disabled="pending"
              name="loginName"
              required
              type="text"
            />
            <FieldProDescription>{{ t('login.loginNameHint') }}</FieldProDescription>
          </FieldPro>

          <FieldPro :data-invalid="Boolean(error) || undefined" :data-disabled="pending || undefined">
            <FieldProLabel for="password">{{ t('login.password') }}</FieldProLabel>
            <InputPro
              id="password"
              v-model="form.password"
              :aria-invalid="Boolean(error)"
              autocomplete="current-password"
              :disabled="pending"
              name="password"
              required
              type="password"
            />
          </FieldPro>
        </FieldProGroup>
      </CardProContent>

      <CardProFooter>
        <ButtonPro class="w-full" :disabled="pending" type="submit">
          <Spinner v-if="pending" data-icon="inline-start" />
          <LogInIcon v-else data-icon="inline-start" />
          {{ pending ? t('login.pending') : t('login.title') }}
        </ButtonPro>
      </CardProFooter>
    </form>
  </CardPro>
</template>
