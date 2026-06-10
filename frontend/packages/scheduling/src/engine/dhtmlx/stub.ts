// 占位模块。当 DHTMLX 试用包未安装时,consuming app / 测试把 `@dhx/trial-gantt`
// 别名到此文件,使 import 可解析且 isDhtmlxAvailable() 返回 false → 回落 NativeEngine。
// 试用版评估许可禁止分发,真实库文件永不进 git。
export const gantt: unknown = undefined
export const Gantt: unknown = undefined
