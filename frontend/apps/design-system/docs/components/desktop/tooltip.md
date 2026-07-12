---
title: NvTooltip 文字提示
---

<script setup>
import {
  NvTooltipProvider,
  NvTooltip,
  NvTooltipTrigger,
  NvTooltipContent,
  NvButton,
} from '@nerv-iip/ui'
import { ActivityIcon } from 'lucide-vue-next'
</script>

# NvTooltip 文字提示

悬停或聚焦时浮出的简短说明，用于解释图标、缩写或被截断的内容。高对比反色气泡，带箭头与指数淡入。

> 使用前需用 `NvTooltipProvider` 包裹（通常放在应用根部），可统一配置 `delay-duration`。

## 基础用法

<Demo>
  <NvTooltipProvider :delay-duration="200">
    <NvTooltip>
      <NvTooltipTrigger as-child>
        <NvButton variant="ghost" size="sm">
          <template #leading><ActivityIcon aria-hidden="true" /></template>
          悬停提示
        </NvButton>
      </NvTooltipTrigger>
      <NvTooltipContent>实时采集自 MES 网关</NvTooltipContent>
    </NvTooltip>
  </NvTooltipProvider>
</Demo>

```vue
<NvTooltipProvider :delay-duration="200">
  <NvTooltip>
    <NvTooltipTrigger as-child>
      <NvButton variant="ghost" size="sm">悬停提示</NvButton>
    </NvTooltipTrigger>
    <NvTooltipContent>实时采集自 MES 网关</NvTooltipContent>
  </NvTooltip>
</NvTooltipProvider>
```

## 方位

通过 `NvTooltipContent` 的 `side` 控制弹出方向。

<Demo>
  <NvTooltipProvider :delay-duration="200">
    <div class="flex gap-3">
      <NvTooltip>
        <NvTooltipTrigger as-child><NvButton variant="outline" size="sm">上方</NvButton></NvTooltipTrigger>
        <NvTooltipContent side="top">节拍 42s / 件</NvTooltipContent>
      </NvTooltip>
      <NvTooltip>
        <NvTooltipTrigger as-child><NvButton variant="outline" size="sm">右侧</NvButton></NvTooltipTrigger>
        <NvTooltipContent side="right">负责人：张伟</NvTooltipContent>
      </NvTooltip>
      <NvTooltip>
        <NvTooltipTrigger as-child><NvButton variant="outline" size="sm">下方</NvButton></NvTooltipTrigger>
        <NvTooltipContent side="bottom">交期 2026-06-22</NvTooltipContent>
      </NvTooltip>
    </div>
  </NvTooltipProvider>
</Demo>

```vue
<NvTooltipContent side="right">负责人：张伟</NvTooltipContent>
```

## 属性

| 属性             | 所属                | 说明                   | 类型                             | 默认    |
| ---------------- | ------------------- | ---------------------- | -------------------------------- | ------- |
| `delay-duration` | `NvTooltipProvider` | 悬停到弹出的延迟（ms） | `number`                         | `700`   |
| `side`           | `NvTooltipContent`  | 弹出方位               | `top \| right \| bottom \| left` | `top`   |
| `side-offset`    | `NvTooltipContent`  | 与触发器的间距         | `number`                         | `6`     |
| `as-child`       | `NvTooltipTrigger`  | 将触发合并到子元素     | `boolean`                        | `false` |
