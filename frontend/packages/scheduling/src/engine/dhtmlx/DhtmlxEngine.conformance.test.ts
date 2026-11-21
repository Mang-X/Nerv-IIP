import { beforeAll, describe, expect } from 'vitest'
import { runEngineConformance } from '../conformance'
import { DhtmlxEngine } from './DhtmlxEngine'
import { isDhtmlxAvailable } from './loader'

describe('DhtmlxEngine conformance', () => {
  beforeAll(async () => {
    expect(await isDhtmlxAvailable(), 'The checked-in DHTMLX Evaluation engine must load').toBe(
      true,
    )
  })
  runEngineConformance(() => new DhtmlxEngine())
})
