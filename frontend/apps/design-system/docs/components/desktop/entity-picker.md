---
title: NvEntityPicker / NvCascadePicker 实体选择与级联选择
---

<script setup>
import { NvEntityPicker, NvCascadePicker, NvField, NvFieldLabel } from '@nerv-iip/ui'
import { computed, ref } from 'vue'

const material = ref('')
const materialOptions = [
  { value: 'SKU-FG-100', label: '前减振器总成', hint: '成品' },
  { value: 'SKU-FG-200', label: '后减振器总成', hint: '成品' },
  { value: 'RM-200', label: '活塞杆毛坯', hint: '原材料' },
  { value: 'RM-300', label: '弹簧钢丝', hint: '原材料' },
]

const scope = ref({ workshop: '', line: '', device: '' })
const lineCatalog = [
  { value: 'LINE-01', label: '冲压一线', workshop: 'WS-01' },
  { value: 'LINE-02', label: '焊装一线', workshop: 'WS-02' },
]
const deviceCatalog = [
  { value: 'DEV-PRESS-01', label: '冲压机 01', line: 'LINE-01' },
  { value: 'DEV-WELD-01', label: '焊接机器人 01', line: 'LINE-02' },
]
const scopeLevels = computed(() => [
  { key: 'workshop', label: '车间', options: [
    { value: 'WS-01', label: '冲压车间' },
    { value: 'WS-02', label: '焊装车间' },
  ] },
  { key: 'line', label: '产线', options: lineCatalog
    .filter((l) => !scope.value.workshop || l.workshop === scope.value.workshop)
    .map(({ value, label }) => ({ value, label })) },
  { key: 'device', label: '设备', options: deviceCatalog
    .filter((d) => !scope.value.line || d.line === scope.value.line)
    .map(({ value, label }) => ({ value, label })) },
])
</script>

# NvEntityPicker / NvCascadePicker 实体选择与级联选择

两个 `blocks/` 层的**选择类区块件**，都是"仅选不填"：把手输编码换成从主数据目录里挑。

| 组件                            | 语义                               | 典型场景                                  |
| ------------------------------- | ---------------------------------- | ----------------------------------------- |
| **NvEntityPicker** 实体选择弹窗 | 按钮触发可搜索的实体选择**对话框** | 物料 / SKU / 质量特性等上百条的主数据目录 |
| **NvCascadePicker** 级联选择器  | 一行多级依赖选择，上级变化清空下游 | 车间 → 产线 → 设备 的范围下钻             |

## NvEntityPicker 实体选择弹窗

相比 [`NvSearchSelect`](/components/desktop/combobox) 的弹出列表，对话框给出更大的展示
空间：每行展示**名称 + 编码 + 辅助信息**。`clearable` 时触发按钮出现清除叉。

下拉里只放用户做选择要用的东西：不写数据来自哪个模块，也不报「共 N 条」。目录大到要服务端
搜索时（`server-search` + `total-count`），结果多于当前列出的条目才在底部提示「输入关键字继续
筛选」。

<Demo>
  <div style="max-width: 360px">
    <NvField>
      <NvFieldLabel for="ep-material">物料</NvFieldLabel>
      <NvEntityPicker
        id="ep-material"
        v-model="material"
        :options="materialOptions"
        title="选择物料"
        placeholder="请选择物料"
        aria-label="物料"
        clearable
      />
    </NvField>
    <p style="margin-top: 8px; font-size: 13px; color: var(--vp-c-text-2)">当前值：{{ material || '（未选）' }}</p>
  </div>
</Demo>

```vue
<NvEntityPicker
  v-model="material"
  :options="[{ value: 'SKU-FG-100', label: '前减振器总成', hint: '成品' }]"
  title="选择物料"
  placeholder="请选择物料"
  :invalid="submitted && !material"
  clearable
/>
```

表单提交后校验失败时传入 `invalid`。组件会给根节点设置 `data-invalid`、给触发按钮设置
`aria-invalid`，并使用错误态边框；打开表单时不要提前传入错误态。

字段下方有错误提示时，把提示的 id 传给 `aria-describedby`，组件会把它转发到触发按钮上，读屏
在这个字段上能念到错误。它必须作为组件属性传入：组件的根节点不是 DOM 元素，写在外层包装上的同名
属性不会落到按钮上。

### 就地新增

表单里要选的实体可能还没建（比如登记设备时车间还不存在），传 `create-text` 后候选列表下方
出现一个新增入口。点击后选择器收起并发出 `create`，由调用方打开新增表单；建好后调用方把
新建项写回 `v-model`，并确保 `options` 里有这一项，触发器才显示名称而不是编码。

- 有没有新增权限由调用方判断：没有权限就不传 `create-text`，入口不出现。
- 只在表单字段上用；筛选条上的选择器不带新增入口。

```vue
<NvEntityPicker
  v-model="workshop"
  :options="workshopOptions"
  title="选择车间"
  :create-text="canCreateWorkshop ? '新增车间' : undefined"
  @create="workshopDialogOpen = true"
/>
```

## NvCascadePicker 级联选择器

每级第一项固定为「全部」（值 = 空串），代表不在该层收窄；选中上级会自动把下游层级
清回「全部」。**层级间的选项过滤由调用方组装**（组件不感知业务字段），如按已选车间
过滤产线目录。

<Demo>
  <div style="max-width: 640px">
    <NvCascadePicker v-model="scope" :levels="scopeLevels" />
    <p style="margin-top: 8px; font-size: 13px; color: var(--vp-c-text-2)">当前值：{{ scope }}</p>
  </div>
</Demo>

```vue
<NvCascadePicker
  v-model="scope"
  :levels="[
    { key: 'workshop', label: '车间', options: workshopOptions },
    { key: 'line', label: '产线', options: lineOptionsFilteredByWorkshop },
    { key: 'device', label: '设备', options: deviceOptionsFilteredByLine },
  ]"
/>
```

## 选型

- 单实体、目录大（≥ 50 条）或需要展示名称 + 编码 + 辅助信息 → **NvEntityPicker**。
- 多级父子范围（车间▸产线▸设备）→ **NvCascadePicker**。
- 单实体、集合中等且一行放得下 → [`NvSearchSelect`](/components/desktop/combobox)。
- 值可能不在集合内（允许自由录入）→ [`NvCombobox`](/components/desktop/combobox)。
