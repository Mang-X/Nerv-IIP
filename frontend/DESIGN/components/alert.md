# Alert

页面流中的内联信息消息。用于持续显示、不可手动关闭的反馈。

> NvUI 状态：目前尚无 `NvAlert`；`Alert` / `AlertTitle` /
> `AlertDescription` 是当前从 `@nerv-iip/ui` 导出的规范名称
> （在品牌层重建前，原版 primitive 继续作为应用侧名称）。

## 变体

| 变体          | 使用场景                                  |
| ------------- | ----------------------------------------- |
| `default`     | 中性信息、操作指引                        |
| `destructive` | API 错误、表单提交失败、权限错误          |

## 用法

```vue
<!-- API/server error above a form or table -->
<Alert v-if="error" variant="destructive">
  <AlertDescription>{{ error }}</AlertDescription>
</Alert>

<!-- Informational notice -->
<Alert>
  <AlertTitle>Read-only mode</AlertTitle>
  <AlertDescription>
    You do not have permission to modify IAM settings. Contact your administrator.
  </AlertDescription>
</Alert>
```

## Alert 与 toast

| 使用 Alert                             | 使用 toast()                              |
| -------------------------------------- | ----------------------------------------- |
| 阻塞当前操作的持续错误                 | 变更后的短暂成功反馈                      |
| 页面级数据获取返回的服务器错误         | 用户已能从上下文获知的短暂错误            |
| 权限或认证警告                         | 后台任务完成                              |

## 禁止

- 不得将 `Alert` 用于成功状态；请使用 `toast.success(...)`。
- 不得手动关闭 Alert；条件解除后，`v-if` 会自动移除它。
- 不得将 `Alert` 用于 `NvDialog` 内的校验错误；应改为在各字段使用 `NvFieldError`。
- 不得堆叠多个 Alert；如有需要，应合并为一个包含列表的 Alert。
