---
title: Cell 单元格
---

<script setup>
import { Cell, CellGroup, MobileSwitch } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const lockMaterial = ref(true)
</script>

# Cell 单元格

信息 / 表单行（tdesign-mobile 风格）：标题 + 可选备注 + 尾部值，可选箭头。`CellGroup` 把多个单元格组合成带细分割线的圆角卡片。

## 基础用法

<Demo mobile>
  <div class="px-3">
    <CellGroup>
      <Cell title="工单号" value="WO-2406-0413" />
      <Cell title="产品" value="前桥壳体 A2" />
      <Cell title="工艺路线" note="3 道工序" arrow />
      <Cell title="加急插单">
        <template #value><MobileSwitch v-model="lockMaterial" /></template>
      </Cell>
    </CellGroup>
  </div>
</Demo>

```vue
<CellGroup>
  <Cell title="工单号" value="WO-2406-0413" />
  <Cell title="产品" value="前桥壳体 A2" />
  <Cell title="工艺路线" note="3 道工序" arrow @click="openRoute" />
  <Cell title="加急插单">
    <template #value><MobileSwitch v-model="lockMaterial" /></template>
  </Cell>
</CellGroup>
```

## 分组标题

<Demo mobile>
  <div class="px-3">
    <CellGroup title="生产信息">
      <Cell title="目标产线" value="A 线 · 精密加工" arrow />
      <Cell title="计划日期" value="2026-06-18" arrow />
    </CellGroup>
  </div>
</Demo>

```vue
<CellGroup title="生产信息">
  <Cell title="目标产线" value="A 线 · 精密加工" arrow />
  <Cell title="计划日期" value="2026-06-18" arrow />
</CellGroup>
```

## 属性

### Cell

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `title` | 标题 | `string` | — |
| `note` | 标题下方备注 | `string` | — |
| `value` | 尾部值（也可用 `#value` 插槽） | `string \| number` | — |
| `arrow` | 显示箭头并启用点击 | `boolean` | `false` |

事件：`@click`（仅在 `arrow` 为真时触发）。插槽：`#icon` 前置图标、`#value` 自定义尾部内容。

### CellGroup

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `title` | 分组标题 | `string` | — |
