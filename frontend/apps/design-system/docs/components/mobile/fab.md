---
layout: page
title: NvFab 悬浮按钮
---

<script setup>
import { NvFab } from '@nerv-iip/ui-mobile'
import { ClipboardListIcon, ScanLineIcon, WrenchIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const fabActions = [
  { key: 'scan', icon: ScanLineIcon, text: '扫码入库' },
  { key: 'wo', icon: ClipboardListIcon, text: '新建工单' },
  { key: 'repair', icon: WrenchIcon, text: '设备报修' },
]
const last = ref('')
function onFabSelect(action) {
  last.value = action.text
}
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">速拨菜单</p>
    <div class="relative h-64 w-full overflow-hidden rounded-xl border border-border bg-background">
      <div class="space-y-2 p-3">
        <div v-for="n in 4" :key="n" class="h-12 rounded-lg bg-card" />
      </div>
      <NvFab :actions="fabActions" @select="onFabSelect" />
    </div>
  </section>
</template>

# NvFab 悬浮按钮

锚定容器角落的悬浮操作按钮。提供 `actions` 时变为速拨菜单：主按钮旋转展开、出现遮罩、带标签的子动作依次升起。需放入相对定位（relative）容器。

## 速拨菜单

`actions` 提供子动作，点击主按钮展开；`select` 回传所选动作与下标。

```vue
<script setup>
import { NvFab } from '@nerv-iip/ui-mobile'
import { ClipboardListIcon, ScanLineIcon, WrenchIcon } from 'lucide-vue-next'

const fabActions = [
  { key: 'scan', icon: ScanLineIcon, text: '扫码入库' },
  { key: 'wo', icon: ClipboardListIcon, text: '新建工单' },
  { key: 'repair', icon: WrenchIcon, text: '设备报修' },
]
function onFabSelect(action) {
  console.log('已触发：', action.text)
}
</script>

<template>
  <!-- 放入相对定位容器，例如 NvAppShellMobile -->
  <div class="relative h-64 overflow-hidden">
    <NvFab :actions="fabActions" @select="onFabSelect" />
  </div>
</template>
```

## 属性

| 属性       | 说明                         | 类型                                           | 默认           |
| ---------- | ---------------------------- | ---------------------------------------------- | -------------- |
| `icon`     | 主按钮图标（单动作模式）     | `Component`                                    | `PlusIcon`     |
| `text`     | 扩展态药丸文案（单动作模式） | `string`                                       | —              |
| `actions`  | 速拨子动作；提供时点击展开   | `FabAction[]`                                  | —              |
| `position` | 锚定位置                     | `bottom-right \| bottom-left \| bottom-center` | `bottom-right` |
| `tone`     | 主按钮色调                   | `brand \| default`                             | `brand`        |

`FabAction`：`{ key?: string; icon?: Component; text?: string }`

## 事件

| 事件     | 说明                 | 回调参数                             |
| -------- | -------------------- | ------------------------------------ |
| `click`  | 单动作模式点击主按钮 | —                                    |
| `select` | 选择速拨子动作       | `(action: FabAction, index: number)` |

</MobileDoc>
