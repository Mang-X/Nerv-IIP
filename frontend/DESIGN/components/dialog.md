# Dialog / AlertDialog

Modal overlays for create/edit forms and destructive confirmations.

## When to use which

| Use `Dialog` | Use `AlertDialog` |
|---|---|
| Create entity form | Confirm delete |
| Edit entity form | Confirm disable/revoke |
| View details overlay | Any irreversible action |
| Multi-step wizard | — |

## Dialog Usage (Create/Edit Form)

```vue
<Dialog v-model:open="dialogOpen">
  <DialogTrigger as-child>
    <Button type="button">Create User</Button>
  </DialogTrigger>
  <DialogContent class="sm:max-w-lg">
    <DialogHeader>
      <DialogTitle>Create user</DialogTitle>
      <DialogDescription>Add a new user to the system.</DialogDescription>
    </DialogHeader>

    <!-- Form body -->
    <form class="grid gap-4" @submit.prevent="handleSubmit">
      <Field>
        <FieldLabel for="login-name">Login name</FieldLabel>
        <Input id="login-name" v-model="form.loginName" required />
        <FieldError v-if="errors.loginName">{{ errors.loginName }}</FieldError>
      </Field>

      <DialogFooter>
        <Button variant="outline" type="button" @click="dialogOpen = false">Cancel</Button>
        <Button type="submit" :disabled="pending">Create</Button>
      </DialogFooter>
    </form>
  </DialogContent>
</Dialog>
```

## AlertDialog Usage (Confirm Destructive)

```vue
<AlertDialog v-model:open="confirmOpen">
  <AlertDialogContent>
    <AlertDialogHeader>
      <AlertDialogTitle>Disable user?</AlertDialogTitle>
      <AlertDialogDescription>
        {{ targetUser.loginName }} will no longer be able to sign in.
      </AlertDialogDescription>
    </AlertDialogHeader>
    <AlertDialogFooter>
      <AlertDialogCancel>Cancel</AlertDialogCancel>
      <AlertDialogAction as-child>
        <Button variant="destructive" type="button" :disabled="pending" @click="handleDisable">
          Disable
        </Button>
      </AlertDialogAction>
    </AlertDialogFooter>
  </AlertDialogContent>
</AlertDialog>
```

## Do NOT

- Do not use a plain `Dialog` for destructive confirmations — use `AlertDialog`.
- Do not put the submit `Button` outside `DialogFooter`.
- Do not forget `DialogDescription` — it's required for screen reader accessibility.
- Do not use `window.confirm` anywhere.
- Do not disable the Cancel button during submission.
