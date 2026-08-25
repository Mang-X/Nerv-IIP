# BusinessGateway capability client 边界

- 自本治理文件落地后，新建、实质修改或按 #1222 迁移的下游 capability client interface、HTTP 实现和专属配置必须归入该 capability 的独立目录；禁止向跨 capability 的共享 client 文件追加或回迁类型。
- `BusinessServiceClients.cs` 仅作为 #1222 尚未轮到的既有 capability 类型的受管迁移例外；每层迁移后必须删除对应旧声明，全部子项完成后删除该文件。
- Shared/ 只容纳不拥有业务语义的传输基础类型；不得把领域 DTO、URL、查询参数或业务失败规则下沉到共享层。
- 新增 capability 时必须新建独立目录，不得恢复或新建 BusinessServiceClients.cs 一类跨域巨石。
- 仅做物理边界治理时，保持既有 namespace、公开签名、DI、HTTP URL、序列化、认证、幂等和异常语义不变。
- 修改 capability client 边界时，先更新 BusinessGatewayCapabilityBoundaryTests 并验证目标错误结构会失败，再迁移源码。
