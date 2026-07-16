# Avatar

User identity display with image, fallback initials, and optional status badge.

> NvUI status: the PC layer has no `NvAvatar` — `Avatar` / `AvatarImage` /
> `AvatarFallback` are the current canonical exports from `@nerv-iip/ui`
> (原版 primitives kept as the app-facing name until a brand rebuild exists).
> On the mobile surface use `NvMobileAvatar` from `@nerv-iip/ui-mobile`.

## Usage

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

## Initials Helper

```ts
function initials(name: string): string {
  return name
    .split(/[\s._-]+/)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase() ?? '')
    .join('')
}
```

## Sizes

| Context              | Class              |
| -------------------- | ------------------ |
| Topbar user menu     | `size-8` (default) |
| User list/table      | `size-7`           |
| Large profile header | `size-12`          |

## Do NOT

- Do not show Avatar for system/service accounts — use a generic icon instead.
- Do not use `AvatarImage` without a fallback — the image may fail to load.
