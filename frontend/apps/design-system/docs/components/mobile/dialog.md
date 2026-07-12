---
layout: page
title: NvMobileDialog 对话框
---

<script setup>
import { NvMobileButton, NvMobileDialog } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const confirmOpen = ref(false)
const dangerOpen = ref(false)
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">居中确认</p>
    <NvMobileButton variant="default" size="md" block @click="confirmOpen = true">
      居中确认
    </NvMobileButton>
    <NvMobileDialog
      v-model:open="confirmOpen"
      title="下发到产线？"
      description="工单 WO-2406-0413 将下发至 A 线排队，物料即时锁定。"
      confirm-text="下发"
    />
  </section>
  <section>
    <p class="nv-mdoc-label">危险确认</p>
    <NvMobileButton variant="danger" size="md" block @click="dangerOpen = true">
      危险确认
    </NvMobileButton>
    <NvMobileDialog
      v-model:open="dangerOpen"
      title="确认作废该工单？"
      description="作废后不可恢复，已领用物料需手动退库。"
      confirm-text="作废"
      confirm-tone="danger"
    />
  </section>
</template>

# NvMobileDialog 对话框

iOS 风格的居中确认弹窗，紧凑卡片承载标题与说明，发线分隔的按钮行收敛确认/取消操作。

## 居中确认

由触发按钮控制 `open`，`confirm` 事件回调确认动作。默认模态：点击遮罩不关闭。

```vue
<script setup>
import { NvMobileButton, NvMobileDialog } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const confirmOpen = ref(false)
</script>

<template>
  <NvMobileButton variant="default" size="md" block @click="confirmOpen = true">
    居中确认
  </NvMobileButton>
  <NvMobileDialog
    v-model:open="confirmOpen"
    title="下发到产线？"
    description="工单 WO-2406-0413 将下发至 A 线排队，物料即时锁定。"
    confirm-text="下发"
    @confirm="() => console.log('已下发')"
  />
</template>
```

## 危险确认

`confirm-tone="danger"` 将确认按钮渲染为危险色，用于不可逆操作。

```vue
<NvMobileDialog
  v-model:open="dangerOpen"
  title="确认作废该工单？"
  description="作废后不可恢复，已领用物料需手动退库。"
  confirm-text="作废"
  confirm-tone="danger"
/>
```

## 属性

| 属性             | 说明                       | 类型              | 默认    |
| ---------------- | -------------------------- | ----------------- | ------- |
| `open`           | 是否打开（`v-model:open`） | `boolean`         | `false` |
| `title`          | 标题                       | `string`          | —       |
| `description`    | 说明文字                   | `string`          | —       |
| `confirmText`    | 确认按钮文案               | `string`          | `确定`  |
| `cancelText`     | 取消按钮文案               | `string`          | `取消`  |
| `showCancel`     | 是否显示取消按钮           | `boolean`         | `true`  |
| `confirmTone`    | 确认按钮色调               | `brand \| danger` | `brand` |
| `closeOnOverlay` | 点击遮罩是否关闭           | `boolean`         | `false` |

## 事件

| 事件          | 说明         | 回调参数           |
| ------------- | ------------ | ------------------ |
| `confirm`     | 点击确认     | —                  |
| `cancel`      | 点击取消     | —                  |
| `update:open` | 开关状态变化 | `(value: boolean)` |

</MobileDoc>
