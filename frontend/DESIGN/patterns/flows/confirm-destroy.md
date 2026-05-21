# Flow: Confirm Destroy

Two-step pattern for confirming an irreversible action (delete, disable, revoke).

## Rules

- Always use `AlertDialog` — never `Dialog`, never `window.confirm`.
- The destructive trigger in a row `DropdownMenuItem` only opens the confirm dialog; it does not call the API.
- The API call happens inside `AlertDialogAction`.

## Structure

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle, Button } from '@nerv-iip/ui'
import { toast } from '@nerv-iip/ui'

const target = ref<Entity | null>(null)
const confirmOpen = ref(false)
const pending = ref(false)

function openConfirm(entity: Entity) {
  target.value = entity
  confirmOpen.value = true
}

async function handleDestroy() {
  if (!target.value) return
  pending.value = true
  try {
    await deleteEntity(target.value.id)
    toast.success(`${target.value.name} deleted`)
    emit('deleted', target.value)
  } finally {
    pending.value = false
    confirmOpen.value = false
    target.value = null
  }
}
</script>

<template>
  <!-- Row action trigger (inside DropdownMenuItem) -->
  <DropdownMenuItem variant="destructive" @select="openConfirm(item)">
    Delete
  </DropdownMenuItem>

  <!-- Confirm dialog (outside the table loop, single instance) -->
  <AlertDialog v-model:open="confirmOpen">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>Delete {{ target?.name }}?</AlertDialogTitle>
        <AlertDialogDescription>
          This action cannot be undone. The entity will be permanently removed.
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel :disabled="pending">Cancel</AlertDialogCancel>
        <AlertDialogAction as-child>
          <Button
            variant="destructive"
            type="button"
            :disabled="pending"
            @click="handleDestroy"
          >
            Delete
          </Button>
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
```

## Do NOT

- Do not nest `AlertDialog` inside a `v-for` loop — declare it once outside and control it with `target`.
- Do not disable Cancel during the API call (users should be able to abort).
- Do not skip `AlertDialogDescription` — it provides context and is required for accessibility.
