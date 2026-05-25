import { describe, expect, it } from 'vitest'

import * as scheduling from './index'

describe('@nerv-iip/scheduling-visualization public exports', () => {
  it('exports the stable component and fixture APIs', () => {
    expect(Object.keys(scheduling).sort()).toEqual([
      'GanttChart',
      'ScheduleChart',
      'SchedulingDetailSheet',
      'SchedulingToolbar',
      'SchedulingWorkspace',
      'calculateVisibleRowRange',
      'createMockGanttFixture',
      'createMockScheduleFixture',
      'createSchedulingCommandStack',
      'createTimeScale',
      'flattenGanttTasks',
      'groupScheduleRows',
      'useSchedulingSelection',
    ])
  })
})
