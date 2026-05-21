# DropdownMenu

Contextual action menu. Primary use: row actions in tables, topbar user menu.

## Usage

```vue
<!-- Table row actions (the standard pattern) -->
<DropdownMenu>
  <DropdownMenuTrigger as-child>
    <Button
      size="icon"
      variant="ghost"
      type="button"
      :aria-label="`Open actions for ${item.name}`"
      :disabled="!canManage"
    >
      <MoreHorizontalIcon class="size-4" aria-hidden="true" />
    </Button>
  </DropdownMenuTrigger>
  <DropdownMenuContent align="end">
    <DropdownMenuItem @select="emit('edit', item)">Edit</DropdownMenuItem>
    <DropdownMenuItem @select="emit('resetPassword', item)">Reset password</DropdownMenuItem>
    <DropdownMenuSeparator />
    <DropdownMenuItem variant="destructive" @select="emit('disable', item)">
      Disable
    </DropdownMenuItem>
  </DropdownMenuContent>
</DropdownMenu>

<!-- Topbar user menu -->
<DropdownMenu>
  <DropdownMenuTrigger as-child>
    <Button variant="ghost" class="flex items-center gap-2 px-2">
      <Avatar class="size-7">
        <AvatarFallback>{{ initials(user.loginName) }}</AvatarFallback>
      </Avatar>
      <span class="text-sm font-medium">{{ user.loginName }}</span>
    </Button>
  </DropdownMenuTrigger>
  <DropdownMenuContent align="end" class="w-48">
    <DropdownMenuLabel class="font-normal text-muted-foreground text-xs">
      {{ user.email }}
    </DropdownMenuLabel>
    <DropdownMenuSeparator />
    <DropdownMenuItem @select="emit('signOut')">
      <LogOutIcon class="size-4" aria-hidden="true" />
      Sign out
    </DropdownMenuItem>
  </DropdownMenuContent>
</DropdownMenu>
```

## Destructive items

Use `variant="destructive"` on `DropdownMenuItem` for irreversible actions. The item should only open a confirm `AlertDialog` — never call the API directly.

## Do NOT

- Do not put more than ~6 items in a DropdownMenu — consider a Dialog with a form instead.
- Do not put navigation items in a DropdownMenu — use `RouterLink` directly.
- Do not use `DropdownMenuCheckboxItem` for filter toggles in a toolbar — use `Select` instead.
