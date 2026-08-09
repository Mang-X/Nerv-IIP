# 下拉菜单（NvDropdownMenu）

上下文操作菜单。主要用于表格行操作和顶栏用户菜单。应用代码使用
`NvDropdownMenu*` 系列，它们来自 `@nerv-iip/ui`；无前缀的 `DropdownMenu*` 部件是
shadcn 原版 primitives，仅限组件库内部使用。

## 用法

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

## 破坏性菜单项

对于不可逆操作，使用 `variant="destructive"` 的目标必须是 `NvDropdownMenuItem`。
该菜单项只能打开确认用的 `NvAlertDialog`，不得直接调用 API。

## 禁止

- 不得在下拉菜单中放入超过约 6 个项目；应考虑使用带表单的对话框/抽屉。
- 不得将导航项目放入下拉菜单；应直接使用 `RouterLink`。
- 不得将 `NvDropdownMenuCheckboxItem` 用于工具栏中的筛选切换；应使用 `NvSelect`（或 `NvDataTable` 列筛选器）。
