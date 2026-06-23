---
title: Button 按钮
---

<script setup>
import { ButtonPro } from '@nerv-iip/ui'
import { PlusIcon } from 'lucide-vue-next'
</script>

# Button 按钮

触发操作或导航。`ButtonPro` 在原版基础上叠加品牌色、图标插槽与一致的交互态。

## 变体

<Demo>
  <ButtonPro variant="brand">主操作</ButtonPro>
  <ButtonPro variant="default">默认</ButtonPro>
  <ButtonPro variant="secondary">次要</ButtonPro>
  <ButtonPro variant="outline">描边</ButtonPro>
  <ButtonPro variant="ghost">幽灵</ButtonPro>
  <ButtonPro variant="destructive">危险</ButtonPro>
  <ButtonPro variant="link">链接</ButtonPro>
</Demo>

```vue
<ButtonPro variant="brand">主操作</ButtonPro>
<ButtonPro variant="outline">描边</ButtonPro>
```

## 尺寸

<Demo>
  <ButtonPro size="sm">小号</ButtonPro>
  <ButtonPro>默认</ButtonPro>
  <ButtonPro size="lg">大号</ButtonPro>
  <ButtonPro size="icon" aria-label="新建"><PlusIcon aria-hidden="true" /></ButtonPro>
  <ButtonPro disabled>禁用</ButtonPro>
</Demo>

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `variant` | 视觉变体 | `brand \| default \| secondary \| outline \| ghost \| destructive \| link` | `default` |
| `size` | 尺寸 | `sm \| default \| lg \| icon` | `default` |
| `disabled` | 是否禁用 | `boolean` | `false` |
