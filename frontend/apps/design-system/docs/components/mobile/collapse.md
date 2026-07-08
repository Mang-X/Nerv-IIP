---
layout: page
title: NvMobileCollapse 折叠面板
---

<script setup>
import { NvMobileCollapse } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div class="mx-3 divide-y divide-border overflow-hidden rounded-xl border border-border">
      <NvMobileCollapse title="工艺参数" :open="true">
        主轴转速 2400 rpm · 进给 180 mm/min · 冷却液开
      </NvMobileCollapse>
      <NvMobileCollapse title="物料清单">
        铝棒 6061-T6 ×1 · 密封圈 ×2 · 标准件若干
      </NvMobileCollapse>
      <NvMobileCollapse title="质检记录">
        首检合格（08:12 张伟）· 巡检 2 次无异常
      </NvMobileCollapse>
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">受控展开</p>
    <div class="mx-3 overflow-hidden rounded-xl border border-border">
      <NvMobileCollapse title="异常处理记录">
        WC-ASM-04 报警 → 已派单维修 → 12:40 恢复
      </NvMobileCollapse>
    </div>
  </section>
</template>

# NvMobileCollapse 折叠面板

可折叠面板（Vant / tdesign-mobile 风格）：高度平滑展开，箭头旋转。`v-model:open` 可选，亦可非受控使用。

## 基础用法

非受控使用，`:open` 仅设置初始展开状态。

```vue
<NvMobileCollapse title="工艺参数" :open="true">
  主轴转速 2400 rpm · 进给 180 mm/min · 冷却液开
</NvMobileCollapse>
<NvMobileCollapse title="物料清单">
  铝棒 6061-T6 ×1 · 密封圈 ×2 · 标准件若干
</NvMobileCollapse>
```

## 受控展开

通过 `v-model:open` 双向绑定展开状态。

```vue
<script setup lang="ts">
import { ref } from 'vue'
const open = ref(false)
</script>

<template>
  <NvMobileCollapse v-model:open="open" title="异常处理记录">
    WC-ASM-04 报警 → 已派单维修 → 12:40 恢复
  </NvMobileCollapse>
</template>
```

## 属性

| 属性    | 说明                             | 类型      | 默认    |
| ------- | -------------------------------- | --------- | ------- |
| `title` | 面板标题（也可用 `#title` 插槽） | `string`  | —       |
| `open`  | 是否展开（`v-model:open`）       | `boolean` | `false` |

插槽：默认（折叠内容）、`#title` 自定义标题。

</MobileDoc>
