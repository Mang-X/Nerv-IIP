import { describe, expect, it } from 'vitest'

import { createMockScheduleFixture } from '../model/fixtures'
import { groupScheduleRows } from '../model/schedule'

describe('groupScheduleRows', () => {
  it('attaches operations to resource rows', () => {
    const fixture = createMockScheduleFixture()
    const rows = groupScheduleRows(fixture.resources, fixture.operations)

    expect(rows[0]).toMatchObject({
      id: 'wc-pack-01',
      operationIds: ['op-packing-1001', 'op-packing-1002'],
    })
  })
})
