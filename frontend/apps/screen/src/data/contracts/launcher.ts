// 大屏选择页（指挥中心门厅）数据契约：全厂脉搏 KPI + 每块屏的领域一瞥。
// 🟠 动态指标当前无真实聚合端点（见 #570），mock 先按此形状产出；
// 接入后仅换 fetchers/launcher.ts，本契约与页面不动。
import type { ScreenKey } from '@/data/mock/scope'

export interface HallKpis {
  /** 今日产量（件） 🟠 待 #570 */
  output: number
  /** 计划达成率 0–100 🟠 待 #570 */
  achievement: number
  runningDevices: number
  totalDevices: number
  /** 未恢复报警数 🟠 待 #570 */
  openAlarms: number
  /** 全厂健康度 0–100 🟠 待 #570 */
  health: number
}

export interface GlanceStat {
  label: string
  value: string
  /** 数字语气：缺省中性白；ok 达成绿 / warn 关注黄 / bad 报警红 */
  tone?: 'ok' | 'warn' | 'bad'
}

/** 领域成员一枚（车间 / 产线 / 异常设备）：门厅卡的导航层。 */
export interface GlanceChip {
  label: string
  tone: 'run' | 'idle' | 'alarm' | 'off'
}

/** 一块屏的领域一瞥：入口卡上回答「先进哪块屏、进去看什么」。 */
export interface ScreenGlance {
  key: ScreenKey
  state: 'run' | 'idle' | 'alarm'
  stateLabel: string
  stats: GlanceStat[]
  /** 成员区标题，如 车间状态 / 异常设备 / 产线状态 */
  chipsLabel: string
  chips: GlanceChip[]
}

export interface LauncherSummary {
  factoryId: string
  kpis: HallKpis
  glances: ScreenGlance[]
}
