<script setup lang="ts">
import ScreenPanel from './ScreenPanel.vue'

/**
 * Screen — alarm list. A minimal table (no vertical rules, hairline rows) keyed
 * by a severity dot: red for severe (严重), amber for general (一般). Work-order
 * numbers render mono, and a recovered (已恢复) status turns green. Pure data —
 * `rows` drives everything.
 */
withDefaults(
  defineProps<{
    /** Panel heading (2026-07 生产走查：各屏语境不同，开放定制). */
    title?: string
    /** Right-side hint text; pass '' to hide (大屏无点击语境). */
    more?: string
    rows?: {
      time: string
      line: string
      /** 'sev' = 严重 (red dot), 'gen' = 一般 (amber dot). */
      level: 'sev' | 'gen'
      name: string
      /** Work-order number, e.g. WO-2406-0421 — shown mono. */
      wo: string
      status: string
    }[]
  }>(),
  {
    title: '告警列表',
    more: '查看全部 ›',
    rows: () => [
      {
        time: '10:23:14',
        line: 'CNC 线 C',
        level: 'sev',
        name: '主轴电机过载',
        wo: 'WO-2406-0421',
        status: '未确认',
      },
      {
        time: '10:18:07',
        line: '焊接线 A',
        level: 'gen',
        name: '焊枪温度异常',
        wo: 'WO-2406-0418',
        status: '处理中',
      },
      {
        time: '10:12:55',
        line: '装配线 B',
        level: 'gen',
        name: '物料短缺',
        wo: 'WO-2406-0415',
        status: '处理中',
      },
      {
        time: '10:05:31',
        line: 'CNC 线 C',
        level: 'sev',
        name: '刀具寿命到期',
        wo: 'WO-2406-0409',
        status: '待确认',
      },
      {
        time: '09:58:22',
        line: '焊接线 A',
        level: 'gen',
        name: '气压低于阈值',
        wo: 'WO-2406-0406',
        status: '已恢复',
      },
    ],
  },
)
</script>

<template>
  <ScreenPanel :title="title" class="nv-scr-at">
    <template v-if="more" #extra
      ><span class="nv-scr-at-more">{{ more }}</span></template
    >
    <div class="nv-scr-at-body nv-scr-scroll">
      <table class="nv-scr-at-tbl">
        <thead>
          <tr>
            <th scope="col">告警时间</th>
            <th scope="col">产线</th>
            <th scope="col">告警级别</th>
            <th scope="col">告警内容</th>
            <th scope="col">工单号</th>
            <th scope="col">状态</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="a in rows" :key="a.wo">
            <td>{{ a.time }}</td>
            <td>{{ a.line }}</td>
            <td>
              <span class="nv-scr-at-lvl" :class="a.level"
                ><i />{{ a.level === 'sev' ? '严重' : '一般' }}</span
              >
            </td>
            <td>{{ a.name }}</td>
            <td class="nv-scr-at-mono">{{ a.wo }}</td>
            <td :class="{ 'nv-scr-at-ok': a.status === '已恢复' }">{{ a.status }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </ScreenPanel>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-at-more {
    font-size: 13px;
    color: var(--nv-scr-muted);
  }
  /* 行数多时面板内滚动（2026-07 生产走查：真实报警量 10+ 行），表头吸顶 */
  .nv-scr-at {
    display: flex;
    flex-direction: column;
    min-height: 0;
  }
  .nv-scr-at-body {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
  }
  .nv-scr-at-tbl {
    width: 100%;
    border-collapse: collapse;
    font-size: 14px;
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-at-tbl th {
    color: var(--nv-scr-text-2);
    font-weight: 600;
    font-size: 13px;
    text-align: left;
    padding: 11px 10px;
    border-bottom: 1px solid var(--nv-scr-line-2);
    position: sticky;
    top: 0;
    z-index: 1;
    background: rgba(13, 21, 39, 0.96);
  }
  .nv-scr-at-tbl td {
    padding: 12px 10px;
    border-bottom: 1px solid var(--nv-scr-line);
    color: var(--nv-scr-text-2);
  }
  .nv-scr-at-tbl tbody tr:nth-child(even) td {
    background: rgba(255, 255, 255, 0.018);
  }
  .nv-scr-at-tbl tbody tr {
    transition: background 0.15s var(--nv-scr-ease);
  }
  .nv-scr-at-tbl tbody tr:hover td {
    background: rgba(67, 180, 228, 0.08);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-at-tbl tbody tr {
      transition: none;
    }
  }
  .nv-scr-at-mono {
    font-family: ui-monospace, monospace;
    color: var(--nv-scr-muted);
    font-size: 13px;
  }
  .nv-scr-at-ok {
    color: var(--nv-scr-green);
  }
  .nv-scr-at-lvl {
    display: inline-flex;
    align-items: center;
    gap: 6px;
  }
  .nv-scr-at-lvl i {
    width: 7px;
    height: 7px;
    border-radius: 50%;
  }
  .nv-scr-at-lvl.sev {
    color: var(--nv-scr-red);
  }
  .nv-scr-at-lvl.sev i {
    background: var(--nv-scr-red);
    box-shadow: 0 0 7px var(--nv-scr-red);
  }
  .nv-scr-at-lvl.gen {
    color: var(--nv-scr-amber);
  }
  .nv-scr-at-lvl.gen i {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 7px var(--nv-scr-amber);
  }
}
</style>
