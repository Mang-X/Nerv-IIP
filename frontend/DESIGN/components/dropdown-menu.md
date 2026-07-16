# DropdownMenu (NvDropdownMenu)

Contextual action menu. Primary use: row actions in tables, topbar user menu.
App code uses the `NvDropdownMenu*` family from `@nerv-iip/ui`; the
un-prefixed `DropdownMenu*` parts are the shadcn 原版 primitives —
library-internal only.

## Usage

```vue
<!-- Table row actions (the standard pattern) -->
<NvDropdownMenu>
  <NvDropdownMenuTrigger as-child>
    <NvButton
      size="icon-sm"
      variant="ghost"
      type="button"
      :aria-label="`Open actions for ${item.name}`"
      :disabled="!canManage"
    >
      <MoreHorizontalIcon class="size-4" aria-hidden="true" />
    </NvButton>
  </NvDropdownMenuTrigger>
  <NvDropdownMenuContent align="end">
    <NvDropdownMenuItem @select="emit('edit', item)">Edit</NvDropdownMenuItem>
    <NvDropdownMenuItem @select="emit('resetPassword', item)">Reset password</NvDropdownMenuItem>
    <NvDropdownMenuSeparator />
    <NvDropdownMenuItem variant="destructive" @select="emit('disable', item)">
      Disable
    </NvDropdownMenuItem>
  </NvDropdownMenuContent>
</NvDropdownMenu>

<!-- Topbar user menu -->
<NvDropdownMenu>
  <NvDropdownMenuTrigger as-child>
    <NvButton variant="ghost" class="flex items-center gap-2 px-2">
      <span class="text-sm font-medium">{{ user.loginName }}</span>
    </NvButton>
  </NvDropdownMenuTrigger>
  <NvDropdownMenuContent align="end" class="w-48">
    <NvDropdownMenuLabel class="font-normal text-muted-foreground text-xs">
      {{ user.email }}
    </NvDropdownMenuLabel>
    <NvDropdownMenuSeparator />
    <NvDropdownMenuItem @select="emit('signOut')">
      <LogOutIcon class="size-4" aria-hidden="true" />
      Sign out
    </NvDropdownMenuItem>
  </NvDropdownMenuContent>
</NvDropdownMenu>
```

## Destructive items

Use `variant="destructive"` on `NvDropdownMenuItem` for irreversible actions.
The item should only open a confirm `NvAlertDialog` — never call the API
directly.

## Do NOT

- Do not put more than ~6 items in a dropdown menu — consider a dialog/sheet with a form instead.
- Do not put navigation items in a dropdown menu — use `RouterLink` directly.
- Do not use `NvDropdownMenuCheckboxItem` for filter toggles in a toolbar — use `NvSelect` (or `NvDataTable` column filters) instead.
