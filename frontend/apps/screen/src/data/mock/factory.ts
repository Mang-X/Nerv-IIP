import type { FactoryOverview } from '@/data/contracts/factory'
import { jitter, spark } from './fixtures'

export function buildFactoryOverview(): FactoryOverview {
  const output = jitter(12840, 520)
  const rate = +(95 + Math.random() * 3.5).toFixed(1)
  const oee = +(80 + Math.random() * 5).toFixed(1)
  return {
    kpis: [
      { label: '今日产量', value: output, unit: '件', delta: '较昨日 +6.2%', spark: spark() },
      { label: '计划达成率', value: rate, unit: '%', delta: '较昨日 +1.4%', spark: spark() },
      { label: '综合 OEE', value: oee, unit: '%', delta: '较昨日 +2.1%', spark: spark() },
      { label: '未恢复告警', value: 3, unit: '条', delta: '较昨日 -2', spark: spark() },
    ],
    workshops: [
      { name: '冲压车间', state: '运行', label: '运行中', tone: 'run', plan: '4,200', actual: '4,032', rate: '96%', downtime: '12 min' },
      { name: '焊装车间', state: '运行', label: '运行中', tone: 'run', plan: '3,800', actual: '3,610', rate: '95%', downtime: '8 min' },
      { name: '涂装车间', state: '待机', label: '换型待机', tone: 'idle', plan: '2,600', actual: '2,106', rate: '81%', downtime: '34 min' },
      { name: '总装车间', state: '运行', label: '运行中', tone: 'run', plan: '3,200', actual: '2,976', rate: '93%', downtime: '15 min' },
      { name: '电池车间', state: '报警', label: '设备报警', tone: 'alarm', plan: '1,800', actual: '1,260', rate: '70%', downtime: '46 min' },
      { name: '注塑车间', state: '运行', label: '运行中', tone: 'run', plan: '2,400', actual: '2,160', rate: '90%', downtime: '10 min' },
    ],
    oee: [
      { label: '可用率', value: oee },
      { label: '性能率', value: jitter(92, 4) },
      { label: '良品率', value: +(97 + Math.random() * 2).toFixed(1) },
    ],
    alarms: [
      { id: 'AL-2041', level: 'critical', text: '电池车间 PACK-03 急停触发', time: '14:31' },
      { id: 'AL-2040', level: 'warning', text: '注塑车间 IMM-07 料温偏高', time: '14:27' },
      { id: 'AL-2039', level: 'warning', text: '涂装车间物料齐套不足', time: '14:22' },
      { id: 'AL-2038', level: 'warning', text: '焊装车间 WS-02 节拍低于目标', time: '14:18' },
      { id: 'AL-2037', level: 'critical', text: '总装车间 AGV-11 通讯中断', time: '14:09' },
      { id: 'AL-2036', level: 'warning', text: '冲压车间模具寿命预警', time: '14:01' },
    ],
  }
}
