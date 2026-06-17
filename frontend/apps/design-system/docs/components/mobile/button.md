---
title: MobileButton 移动按钮
---

<script setup>
import { MobileButton } from '@nerv-iip/ui-mobile'
</script>

# MobileButton 移动按钮

紧凑、贴近原生（iOS / tdesign-mobile）的触控按钮，按压时整体变暗反馈。区别于工位大屏的超大 TouchButton。

## 变体

<Demo mobile>
  <div class="flex flex-wrap items-center gap-2">
    <MobileButton variant="primary">主操作</MobileButton>
    <MobileButton variant="default">次要</MobileButton>
    <MobileButton variant="outline">描边</MobileButton>
    <MobileButton variant="text">文字</MobileButton>
    <MobileButton variant="danger">删除</MobileButton>
  </div>
</Demo>

```vue
<MobileButton variant="primary">主操作</MobileButton>
<MobileButton variant="default">次要</MobileButton>
<MobileButton variant="outline">描边</MobileButton>
<MobileButton variant="text">文字</MobileButton>
<MobileButton variant="danger">删除</MobileButton>
```

## 尺寸

<Demo mobile>
  <div class="flex flex-wrap items-center gap-2">
    <MobileButton variant="primary" size="sm">小号</MobileButton>
    <MobileButton variant="primary" size="md">中号</MobileButton>
    <MobileButton variant="primary" size="lg">大号</MobileButton>
  </div>
</Demo>

```vue
<MobileButton variant="primary" size="sm">小号</MobileButton>
<MobileButton variant="primary" size="md">中号</MobileButton>
<MobileButton variant="primary" size="lg">大号</MobileButton>
```

## 整宽

<Demo mobile>
  <MobileButton variant="primary" size="lg" block>整宽主按钮</MobileButton>
</Demo>

```vue
<MobileButton variant="primary" size="lg" block>整宽主按钮</MobileButton>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `variant` | 视觉变体 | `primary \| default \| outline \| text \| danger` | `default` |
| `size` | 尺寸 | `sm \| md \| lg` | `md` |
| `block` | 是否整宽 | `boolean` | `false` |
