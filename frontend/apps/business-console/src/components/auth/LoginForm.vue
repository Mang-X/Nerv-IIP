<script setup lang="ts">
import {
  Alert,
  AlertDescription,
  NvButton,
  NvCard,
  NvCardContent,
  NvCardDescription,
  NvCardFooter,
  NvCardHeader,
  NvCardTitle,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  Spinner,
} from '@nerv-iip/ui'
import { LogInIcon } from '@lucide/vue'
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
  <NvCard>
    <form class="flex flex-col gap-4 pt-6" @submit.prevent="submit">
      <NvCardHeader class="text-center">
        <NvCardTitle class="text-xl">{{ t('login.title') }}</NvCardTitle>
        <NvCardDescription>{{ t('login.description') }}</NvCardDescription>
      </NvCardHeader>

      <NvCardContent class="flex flex-col gap-4">
        <Alert v-if="error" role="alert" variant="destructive">
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>

        <NvFieldGroup>
          <NvField
            :data-invalid="Boolean(error) || undefined"
            :data-disabled="pending || undefined"
          >
            <NvFieldLabel for="login-name">{{ t('login.loginName') }}</NvFieldLabel>
            <NvInput
              id="login-name"
              v-model="form.loginName"
              :aria-invalid="Boolean(error)"
              autocomplete="username"
              :disabled="pending"
              name="loginName"
              required
              type="text"
            />
            <NvFieldDescription>{{ t('login.loginNameHint') }}</NvFieldDescription>
          </NvField>

          <NvField
            :data-invalid="Boolean(error) || undefined"
            :data-disabled="pending || undefined"
          >
            <NvFieldLabel for="password">{{ t('login.password') }}</NvFieldLabel>
            <NvInput
              id="password"
              v-model="form.password"
              :aria-invalid="Boolean(error)"
              autocomplete="current-password"
              :disabled="pending"
              name="password"
              required
              type="password"
            />
          </NvField>
        </NvFieldGroup>
      </NvCardContent>

      <NvCardFooter>
        <NvButton class="w-full" :disabled="pending" type="submit">
          <Spinner v-if="pending" data-icon="inline-start" />
          <LogInIcon v-else data-icon="inline-start" />
          {{ pending ? t('login.pending') : t('login.title') }}
        </NvButton>
      </NvCardFooter>
    </form>
  </NvCard>
</template>
