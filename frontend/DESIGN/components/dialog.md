# Dialog / AlertDialog (NvDialog / NvAlertDialog)

Modal overlays for create/edit forms and destructive confirmations. App code
uses the `Nv*` families from `@nerv-iip/ui`; the un-prefixed `Dialog*` /
`AlertDialog*` parts are the shadcn 原版 primitives — library-internal only.

## When to use which

| Use `NvDialog`             | Use `NvAlertDialog`     |
| -------------------------- | ----------------------- |
| Create entity form (short) | Confirm delete          |
| Edit entity form (short)   | Confirm disable/revoke  |
| View details overlay       | Any irreversible action |
| Multi-step wizard          | —                       |

For longer create/edit forms that should preserve list context, prefer
`NvSheet` (see `business-console-primitives.md`). For a lightweight inline
confirm anchored to the trigger, `NvPopconfirm` exists — but irreversible
actions still get a full `NvAlertDialog`.

## NvDialog Usage (Create/Edit Form)

```vue
<NvDialog v-model:open="dialogOpen">
  <NvDialogTrigger as-child>
    <NvButton type="button">Create User</NvButton>
  </NvDialogTrigger>
  <NvDialogContent class="sm:max-w-lg">
    <NvDialogHeader>
      <NvDialogTitle>Create user</NvDialogTitle>
      <NvDialogDescription>Add a new user to the system.</NvDialogDescription>
    </NvDialogHeader>

    <form class="grid gap-4" @submit.prevent="handleSubmit">
      <NvField>
        <NvFieldLabel for="login-name">Login name</NvFieldLabel>
        <NvInput id="login-name" v-model="form.loginName" required />
        <NvFieldError v-if="errors.loginName">{{ errors.loginName }}</NvFieldError>
      </NvField>

      <NvDialogFooter>
        <NvButton variant="outline" type="button" @click="dialogOpen = false">Cancel</NvButton>
        <NvButton type="submit" :loading="pending">Create</NvButton>
      </NvDialogFooter>
    </form>
  </NvDialogContent>
</NvDialog>
```

## NvAlertDialog Usage (Confirm Destructive)

```vue
<NvAlertDialog v-model:open="confirmOpen">
  <NvAlertDialogContent>
    <NvAlertDialogHeader>
      <NvAlertDialogTitle>Disable user?</NvAlertDialogTitle>
      <NvAlertDialogDescription>
        {{ targetUser.loginName }} will no longer be able to sign in.
      </NvAlertDialogDescription>
    </NvAlertDialogHeader>
    <NvAlertDialogFooter>
      <NvAlertDialogCancel>Cancel</NvAlertDialogCancel>
      <NvAlertDialogAction as-child>
        <NvButton variant="destructive" type="button" :loading="pending" @click="handleDisable">
          Disable
        </NvButton>
      </NvAlertDialogAction>
    </NvAlertDialogFooter>
  </NvAlertDialogContent>
</NvAlertDialog>
```

## Do NOT

- Do not use a plain `NvDialog` for destructive confirmations — use `NvAlertDialog`.
- Do not put the submit `NvButton` outside `NvDialogFooter`.
- Do not forget `NvDialogDescription` — it's required for screen reader accessibility.
- Do not use `window.confirm` anywhere.
- Do not disable the Cancel button during submission.
- Do not let `@update:open` close the dialog while a mutation is pending — guard the close.
