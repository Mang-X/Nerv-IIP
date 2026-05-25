import { describe, expect, it } from 'vitest'

import * as scheduling from './index'

describe('@nerv-iip/scheduling-visualization public exports', () => {
  it('exports the stable component and fixture APIs', () => {
    expect(Object.keys(scheduling).sort()).toEqual([
      'GanttChart',
      'ScheduleChart',
      'SchedulingDetailSheet',
      'SchedulingLegend',
      'SchedulingToolbar',
      'SchedulingWorkspace',
      'buildDependencyRoute',
      'buildGanttBarPositions',
      'buildGanttScene',
      'buildScheduleCalendarHighlightPositions',
      'buildScheduleOperationPositions',
      'buildScheduleScene',
      'buildTimelineTicks',
      'calculateTimelineContentWidth',
      'calculateVisibleRowRange',
      'createLargeMockScheduleFixture',
      'createMockGanttFixture',
      'createMockScheduleFixture',
      'createSchedulingCommandStack',
      'createTimeScale',
      'filterGanttFixture',
      'filterScheduleFixture',
      'flattenGanttTasks',
      'groupScheduleRows',
      'renderSceneToLeafer',
      'shiftWindowByPixels',
      'useSchedulingSelection',
    ])
  })
})
