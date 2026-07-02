import { describe } from 'vitest'
import { runEngineConformance } from '../conformance'
import { DhtmlxEngine } from './DhtmlxEngine'
import { isDhtmlxAvailable } from './loader'

// 真实 DHTMLX 试用包存在时,跑与内联 FakeEngine 同一套引擎契约,锁定「可替换性」。
// CI / 无许可环境:@dhx/trial-gantt 别名到 stub → isDhtmlxAvailable() 为 false → 整组 skip。
const available = await isDhtmlxAvailable()

describe.skipIf(!available)('DhtmlxEngine conformance (requires @dhx/trial-gantt)', () => {
  runEngineConformance(() => new DhtmlxEngine())
})
