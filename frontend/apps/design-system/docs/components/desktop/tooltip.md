---
title: Tooltip 文字提示
---

<script setup>
import {
  TooltipProProvider,
  TooltipPro,
  TooltipProTrigger,
  TooltipProContent,
  ButtonPro,
} from '@nerv-iip/ui'
import { ActivityIcon } from 'lucide-vue-next'
</script>

# Tooltip 文字提示

悬停或聚焦时浮出的简短说明，用于解释图标、缩写或被截断的内容。高对比反色气泡，带箭头与指数淡入。

> 使用前需用 `TooltipProProvider` 包裹（通常放在应用根部），可统一配置 `delay-duration`。

## 基础用法

<Demo>
  <TooltipProProvider :delay-duration="200">
    <TooltipPro>
      <TooltipProTrigger as-child>
        <ButtonPro variant="ghost" size="sm">
          <template #leading><ActivityIcon aria-hidden="true" /></template>
          悬停提示
        </ButtonPro>
      </TooltipProTrigger>
      <TooltipProContent>实时采集自 MES 网关</TooltipProContent>
    </TooltipPro>
  </TooltipProProvider>
</Demo>

```vue
<TooltipProProvider :delay-duration="200">
  <TooltipPro>
    <TooltipProTrigger as-child>
      <ButtonPro variant="ghost" size="sm">悬停提示</ButtonPro>
    </TooltipProTrigger>
    <TooltipProContent>实时采集自 MES 网关</TooltipProContent>
  </TooltipPro>
</TooltipProProvider>
```

## 方位

通过 `TooltipProContent` 的 `side` 控制弹出方向。

<Demo>
  <TooltipProProvider :delay-duration="200">
    <div class="flex gap-3">
      <TooltipPro>
        <TooltipProTrigger as-child><ButtonPro variant="outline" size="sm">上方</ButtonPro></TooltipProTrigger>
        <TooltipProContent side="top">节拍 42s / 件</TooltipProContent>
      </TooltipPro>
      <TooltipPro>
        <TooltipProTrigger as-child><ButtonPro variant="outline" size="sm">右侧</ButtonPro></TooltipProTrigger>
        <TooltipProContent side="right">负责人：张伟</TooltipProContent>
      </TooltipPro>
      <TooltipPro>
        <TooltipProTrigger as-child><ButtonPro variant="outline" size="sm">下方</ButtonPro></TooltipProTrigger>
        <TooltipProContent side="bottom">交期 2026-06-22</TooltipProContent>
      </TooltipPro>
    </div>
  </TooltipProProvider>
</Demo>

```vue
<TooltipProContent side="right">负责人：张伟</TooltipProContent>
```

## 属性

| 属性 | 所属 | 说明 | 类型 | 默认 |
|---|---|---|---|---|
| `delay-duration` | `TooltipProProvider` | 悬停到弹出的延迟（ms） | `number` | `700` |
| `side` | `TooltipProContent` | 弹出方位 | `top \| right \| bottom \| left` | `top` |
| `side-offset` | `TooltipProContent` | 与触发器的间距 | `number` | `6` |
| `as-child` | `TooltipProTrigger` | 将触发合并到子元素 | `boolean` | `false` |
