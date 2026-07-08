---
title: NvButton 按钮
---

<script setup>
import { NvButton } from '@nerv-iip/ui'
import { PlusIcon } from 'lucide-vue-next'
</script>

# NvButton 按钮

触发操作或导航。`NvButton` 在原版基础上叠加品牌色、图标插槽与一致的交互态。

## 变体

<Demo>
  <NvButton variant="brand">主操作</NvButton>
  <NvButton variant="default">默认</NvButton>
  <NvButton variant="secondary">次要</NvButton>
  <NvButton variant="outline">描边</NvButton>
  <NvButton variant="ghost">幽灵</NvButton>
  <NvButton variant="destructive">危险</NvButton>
  <NvButton variant="link">链接</NvButton>
</Demo>

```vue
<NvButton variant="brand">主操作</NvButton>
<NvButton variant="outline">描边</NvButton>
```

## 尺寸

<Demo>
  <NvButton size="sm">小号</NvButton>
  <NvButton>默认</NvButton>
  <NvButton size="lg">大号</NvButton>
  <NvButton size="icon" aria-label="新建"><PlusIcon aria-hidden="true" /></NvButton>
  <NvButton disabled>禁用</NvButton>
</Demo>

## 属性

| 属性       | 说明     | 类型                                                                       | 默认      |
| ---------- | -------- | -------------------------------------------------------------------------- | --------- |
| `variant`  | 视觉变体 | `brand \| default \| secondary \| outline \| ghost \| destructive \| link` | `default` |
| `size`     | 尺寸     | `sm \| default \| lg \| icon`                                              | `default` |
| `disabled` | 是否禁用 | `boolean`                                                                  | `false`   |
