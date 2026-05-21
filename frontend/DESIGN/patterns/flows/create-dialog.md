# Flow: Create Dialog

Standard pattern for creating a new entity via a modal form.

## Structure

Open via a trigger button (usually in the toolbar). Form lives inside `DialogContent`. Submit calls an API composable. On success, close dialog and refresh list.

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { Button, Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle, DialogTrigger, Field, FieldError,
  FieldLabel, Input } from '@nerv-iip/ui'
import { toast } from '@nerv-iip/ui'

const open = ref(false)
const pending = ref(false)
const form = ref({ name: '' })
const errors = ref<Record<string, string>>({})

async function handleSubmit() {
  pending.value = true
  errors.value = {}
  try {
    await createEntity(form.value)
    toast.success('Entity created')
    open.value = false
    emit('created')
  } catch (err) {
    errors.value = parseApiErrors(err)
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogTrigger as-child>
      <Button type="button">Create</Button>
    </DialogTrigger>
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>Create entity</DialogTitle>
        <DialogDescription>Fill in the details below.</DialogDescription>
      </DialogHeader>

      <form class="grid gap-4" @submit.prevent="handleSubmit">
        <Field>
          <FieldLabel for="entity-name">Name</FieldLabel>
          <Input id="entity-name" v-model="form.name" required />
          <FieldError v-if="errors.name">{{ errors.name }}</FieldError>
        </Field>

        <DialogFooter>
          <Button variant="outline" type="button" :disabled="pending" @click="open = false">
            Cancel
          </Button>
          <Button type="submit" :disabled="pending">Create</Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
```

## Rules

- Always `v-model:open` to control dialog state.
- Always `type="submit"` on the confirm button; `type="button"` on Cancel.
- Always disable both buttons while `pending`.
- Always call `toast.success(...)` after success and close the dialog.
- Always reset `errors` before submitting.
- Never put the `<form>` outside `DialogContent`.
