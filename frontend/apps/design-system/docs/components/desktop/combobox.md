---
title: NvCombobox / NvSearchSelect 联想与搜索选择
---

<script setup>
import { NvCombobox, NvSearchSelect, NvField, NvFieldLabel } from '@nerv-iip/ui'
import { ref } from 'vue'

const device = ref('')
const deviceSuggestions = [
  { value: 'DEV-SMT-01', label: '贴片机 01' },
  { value: 'DEV-SMT-02', label: '贴片机 02' },
  { value: 'DEV-PRESS-01', label: '冲压机 01' },
]

const technician = ref('')
const technicianOptions = [
  { value: 'u1', label: '张工', hint: 'E001' },
  { value: 'u2', label: '李工', hint: 'E002' },
  { value: 'u3', label: '王工', hint: 'E003' },
]
</script>

# NvCombobox / NvSearchSelect 联想与搜索选择

两个用于**减少手动输入**的输入控件，都基于 Popover（portal 逃逸 Sheet/对话框的
`overflow` 裁剪），自带过滤与键盘导航（↑ ↓ 移动、↵ 选择、esc 关闭）。

| 控件                          | 语义                              | 是否允许自由录入 | 典型场景                                     |
| ----------------------------- | --------------------------------- | ---------------- | -------------------------------------------- |
| **NvCombobox** 输入联想框     | 文本输入即过滤建议，值 = 输入文本 | ✅ 允许          | 设备编号、测量特性（有历史候选、也可能新填） |
| **NvSearchSelect** 弹出选择框 | 按钮触发可搜索的弹出单选          | ❌ 仅选不填      | 技师目录、停机原因、维护结果（固定集合）     |

## NvCombobox 输入联想框

输入即过滤 `suggestions`；命中建议可一键填入，也可继续输入未在建议中的自定义值。
仅在存在匹配建议时展开弹层，避免自由录入时反复弹出空态。

<Demo>
  <div style="max-width: 320px">
    <NvField>
      <NvFieldLabel for="cb-device">设备编号</NvFieldLabel>
      <NvCombobox
        id="cb-device"
        v-model="device"
        :suggestions="deviceSuggestions"
        placeholder="搜索设备台账或直接输入，如 DEV-SMT-01"
      />
    </NvField>
    <p style="margin-top: 8px; font-size: 13px; color: var(--vp-c-text-2)">当前值：{{ device || '（空）' }}</p>
  </div>
</Demo>

```vue
<NvCombobox
  v-model="device"
  :suggestions="[{ value: 'DEV-SMT-01', label: '贴片机 01' }]"
  placeholder="搜索设备台账或直接输入"
/>
```

## NvSearchSelect 弹出选择框

从固定集合里选一个；触发按钮展示所选标签，弹层顶部带搜索框，比长下拉更好定位。
`options` 支持 `hint`（如工号）辅助识别。

<Demo>
  <div style="max-width: 320px">
    <NvField>
      <NvFieldLabel for="ss-tech">指派技师</NvFieldLabel>
      <NvSearchSelect
        id="ss-tech"
        v-model="technician"
        :options="technicianOptions"
        placeholder="未指派"
        search-placeholder="搜索技师姓名 / 工号…"
      />
    </NvField>
    <p style="margin-top: 8px; font-size: 13px; color: var(--vp-c-text-2)">当前值：{{ technician || '（未选）' }}</p>
  </div>
</Demo>

```vue
<NvSearchSelect
  v-model="technician"
  :options="[{ value: 'u1', label: '张工', hint: 'E001' }]"
  placeholder="未指派"
  search-placeholder="搜索技师姓名 / 工号…"
/>
```

## 选型

- 值可能不在集合内（会新登记设备、特性因产线而异）→ **NvCombobox**。
- 值必须来自固定集合（人员目录、编码字典）→ **NvSearchSelect**。
- 集合极小（≤5 且稳定）且无需搜索 → 用 [`NvSelect`](/components/desktop/select)。
