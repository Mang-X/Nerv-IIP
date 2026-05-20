<script setup lang="ts">
import {
  Alert,
  AlertDescription,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  Spinner,
} from '@nerv-iip/ui'
import { LogInIcon } from 'lucide-vue-next'
import { reactive } from 'vue'

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
  <Card class="w-full">
    <form class="flex flex-col gap-4" @submit.prevent="submit">
      <CardHeader>
        <CardTitle>Sign in</CardTitle>
        <CardDescription>Use your Console account to continue.</CardDescription>
      </CardHeader>

      <CardContent class="flex flex-col gap-4">
        <Alert v-if="error" role="alert" variant="destructive">
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>

        <FieldGroup>
          <Field :data-invalid="Boolean(error) || undefined" :data-disabled="pending || undefined">
            <FieldLabel for="login-name">Login name</FieldLabel>
            <Input
              id="login-name"
              v-model="form.loginName"
              :aria-invalid="Boolean(error)"
              autocomplete="username"
              :disabled="pending"
              name="loginName"
              required
              type="text"
            />
            <FieldDescription>Seeded local admin uses admin.</FieldDescription>
          </Field>

          <Field :data-invalid="Boolean(error) || undefined" :data-disabled="pending || undefined">
            <FieldLabel for="password">Password</FieldLabel>
            <Input
              id="password"
              v-model="form.password"
              :aria-invalid="Boolean(error)"
              autocomplete="current-password"
              :disabled="pending"
              name="password"
              required
              type="password"
            />
          </Field>
        </FieldGroup>
      </CardContent>

      <CardFooter>
        <Button class="w-full" :disabled="pending" type="submit">
          <Spinner v-if="pending" data-icon="inline-start" />
          <LogInIcon v-else data-icon="inline-start" />
          {{ pending ? 'Signing in' : 'Sign in' }}
        </Button>
      </CardFooter>
    </form>
  </Card>
</template>
