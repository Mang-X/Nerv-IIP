# 头像（Avatar）

展示用户身份，包括头像图片、回退首字母和可选状态徽标。

> NvUI 状态：PC 层目前没有 `NvAvatar`；`Avatar` / `AvatarImage` /
> `AvatarFallback` 是当前从 `@nerv-iip/ui` 导出的规范名称
> （在品牌层重建前，原版 primitive 继续作为应用侧名称）。
> 在移动端界面请使用 `NvMobileAvatar`，它从 `@nerv-iip/ui-mobile` 导出。

## 用法

```vue
<!-- Basic with initials fallback (most common in this project) -->
<Avatar>
  <AvatarImage :src="user.avatarUrl" :alt="user.loginName" />
  <AvatarFallback>{{ initials(user.loginName) }}</AvatarFallback>
</Avatar>

<!-- Small — in topbar/sidebar -->
<Avatar class="size-8">
  <AvatarFallback class="text-xs">{{ initials(user.loginName) }}</AvatarFallback>
</Avatar>
```

## 首字母辅助函数

```ts
function initials(name: string): string {
  return name
    .split(/[\s._-]+/)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase() ?? '')
    .join('')
}
```

## 尺寸

| 场景               | 类名             |
| ------------------ | ---------------- |
| 顶栏用户菜单       | `size-8`（默认） |
| 用户列表/表格      | `size-7`         |
| 大型个人资料页头部 | `size-12`        |

## 禁止

- 不得为系统或服务账号显示 Avatar；应改用通用图标。
- 不得在没有回退内容时使用 `AvatarImage`；图片可能加载失败。
