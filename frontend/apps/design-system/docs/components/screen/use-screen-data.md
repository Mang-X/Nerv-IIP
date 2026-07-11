---
title: useScreenData 大屏取数
---

<script setup>
import { useScreenData } from '@nerv-iip/ui'

let n = 0
const { data, lastUpdated, isStale, loading } = useScreenData(
  async () => {
    n += 1
    // 演示：每第 4 次失败一次，观察 isStale 保活
    if (n % 4 === 0) throw new Error('demo error')
    return `第 ${n} 次取数 · ${new Date().toLocaleTimeString()}`
  },
  { intervalMs: 3000 },
)
</script>

# useScreenData 大屏取数

大屏挂墙**长时间运行**的取数组合式:轮询 + 页面隐藏暂停 + 失败保活。三条针对控制室场景的设计决策:

1. **失败保活**——取数失败不抛错、不清空旧数据,仅标记 `error` / `isStale`,墙上画面永不闪空;
2. **隐藏暂停**——页面不可见(切标签/熄屏)时跳过取数,恢复可见立即补一拍;
3. **自动清理**——组件卸载(scope dispose)自动停止定时器与监听,长期运行不泄漏。

## 实时演示

3s 轮询,每第 4 次故意失败——注意失败时数据保留、仅 `isStale` 亮起:

<ScreenDemo>
  <div style="display: flex; flex-direction: column; gap: 8px; font-size: 14px; font-variant-numeric: tabular-nums">
    <div>data：<b style="color: var(--nv-scr-text)">{{ data ?? '（取数中…）' }}</b></div>
    <div style="color: var(--nv-scr-muted)">
      lastUpdated：{{ lastUpdated ? new Date(lastUpdated).toLocaleTimeString() : '—' }}
      · loading：{{ loading }}
      · isStale：<b :style="{ color: isStale ? 'var(--nv-scr-amber)' : 'var(--nv-scr-green)' }">{{ isStale }}</b>
    </div>
  </div>
</ScreenDemo>

## 基础用法

```ts
import { useScreenData } from '@nerv-iip/ui'

const { data, isStale, refresh } = useScreenData(
  () => fetchFactoryOverview(scope.currentFactoryId),
  { intervalMs: 5000 },
)
```

多频分层(主数据 5s / 高频参数 2–3s)各起一个实例;scope 切换时手动 `refresh()`:

```ts
const { data: board, refresh } = useScreenData(() => fetchBoard(id.value), { intervalMs: 5000 })
const { data: tick } = useScreenData(() => fetchParamsTick(visibleIds.value), { intervalMs: 2000 })
watch(
  () => id.value,
  () => void refresh(),
)
```

## API

**`useScreenData<T>(fetcher, options?) => UseScreenDataReturn<T>`**

| Option        | 类型      | 默认    | 说明         |
| ------------- | --------- | ------- | ------------ |
| `intervalMs`  | `number`  | `15000` | 轮询间隔     |
| `immediate`   | `boolean` | `true`  | 挂载即取一次 |
| `initialData` | `T`       | —       | 初始占位数据 |

| 返回                 | 类型                       | 说明                                                    |
| -------------------- | -------------------------- | ------------------------------------------------------- |
| `data`               | `Ref<T \| undefined>`      | 最近一次成功数据(失败不清空)                            |
| `error`              | `Ref<unknown>`             | 最近一次错误                                            |
| `loading`            | `Ref<boolean>`             | 取数中                                                  |
| `lastUpdated`        | `Ref<number \| undefined>` | 最近成功时间戳(ms)                                      |
| `isStale`            | `Ref<boolean>`             | 失败但保留旧数据时为 `true`(页面可据此亮「数据滞留」灯) |
| `refresh()`          | `() => Promise<void>`      | 手动取一次(在途时跳过)                                  |
| `start()` / `stop()` | `() => void`               | 手动启停(如弹窗打开时暂停墙面轮询)                      |
