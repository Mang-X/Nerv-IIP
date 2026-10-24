# 对话框 / 警告对话框（NvDialog / NvAlertDialog）

用于创建/编辑表单和破坏性操作确认的模态覆盖层。应用代码使用 `Nv*` 系列，
它们来自 `@nerv-iip/ui`；无前缀的 `Dialog*` / `AlertDialog*` 部件是
shadcn 原版 primitives，仅限组件库内部使用。

## 何时使用哪一种

| 使用 `NvDialog`      | 使用 `NvAlertDialog` |
| -------------------- | -------------------- |
| 创建实体表单（简短） | 确认删除             |
| 编辑实体表单（简短） | 确认禁用/撤销        |
| 查看详情覆盖层       | 任何不可逆操作       |
| 多步骤向导           | —                    |

对于需要保留列表上下文的较长创建/编辑表单，应优先使用
`NvSheet`（参见 `business-console-primitives.md`）。对于锚定在触发器上的轻量
行内确认，可使用 `NvPopconfirm`；但不可逆操作仍必须使用完整的 `NvAlertDialog`。

## NvDialog 用法（创建/编辑表单）

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

## 长表单的滚动边界

包含动态行、较多字段或校验增长时，用 `NvDialogContent scrollable`：
标题栏在上、`NvDialogBody` 为唯一滚动正文、`NvDialogFooter` 独立在下。
三段共用弹窗表面；操作区横跨弹窗宽度，以轻分隔线划界，不做嵌套白卡片。
桌面取消紧邻主按钮并靠右；窄屏按钮按组件统一规则纵向排列。

```vue
<NvDialogContent scrollable class="sm:max-w-lg">
  <NvDialogHeader>
    <NvDialogTitle>发布新版本</NvDialogTitle>
    <NvDialogDescription>核对物料明细后发布。</NvDialogDescription>
  </NvDialogHeader>
  <form @submit.prevent="submit">
    <NvDialogBody>
      <div class="grid gap-4"><!-- 字段、动态行和校验提示 --></div>
    </NvDialogBody>
    <NvDialogFooter>
      <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
      <NvButton type="submit" :disabled="pending">发布版本</NvButton>
    </NvDialogFooter>
  </form>
</NvDialogContent>
```

- `form` 只负责提交，不能再承担局部滚动；不得在 Footer 上补 `static`。
- 不把 Footer 放进 Body，不叠加 Content/form 的限高和 `overflow-y-auto`。
- Body 自带边距和焦点滚动空间，内部只编排字段间距；不靠负外边距贴边。
- 未开启 `scrollable` 的旧组合保持原有遮罩滚动，不在同一个窗口混用新旧模式。

## NvAlertDialog 用法（确认破坏性操作）

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

## 禁止

- 不得将普通 `NvDialog` 用于破坏性操作确认；应使用 `NvAlertDialog`。
- 不得将提交 `NvButton` 放在 `NvDialogFooter` 外部。
- 不得遗漏 `NvDialogDescription`；屏幕阅读器无障碍访问必须提供它。
- 不得在任何位置使用 `window.confirm`。
- 提交期间不得禁用取消按钮。
- 变更请求处于 pending 时不得让 `@update:open` 关闭对话框；应守卫关闭操作。
