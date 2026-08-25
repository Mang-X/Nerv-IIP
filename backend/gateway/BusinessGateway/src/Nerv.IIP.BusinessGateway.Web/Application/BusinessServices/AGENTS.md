# BusinessGateway capability client 边界

- 每个下游 capability 的 client interface、HTTP 实现和专属配置必须放在该 capability 的独立目录；禁止向跨 capability 的共享 client 文件追加类型。
- Shared/ 只容纳不拥有业务语义的传输基础类型；不得把领域 DTO、URL、查询参数或业务失败规则下沉到共享层。
- 新增 capability 时必须新建独立目录，不得恢复或新建 BusinessServiceClients.cs 一类跨域巨石。
- 仅做物理边界治理时，保持既有 namespace、公开签名、DI、HTTP URL、序列化、认证、幂等和异常语义不变。
- 修改 capability client 边界时，先更新 BusinessGatewayCapabilityBoundaryTests 并验证目标错误结构会失败，再迁移源码。
