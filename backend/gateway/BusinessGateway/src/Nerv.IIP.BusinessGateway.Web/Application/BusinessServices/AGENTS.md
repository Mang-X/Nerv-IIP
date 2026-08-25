# BusinessGateway capability client 边界

- #2159（#1222 子项①A）只冻结三个无业务语义的 Shared 基础类型：`BusinessServiceAuditContext`、`BusinessServiceProxyException` 和 `BusinessServiceHttpClient` 必须各自在 `Shared/` 下拥有唯一真实声明；`Shared/` 不得承载领域 DTO、URL、查询参数或业务失败规则。
- A 阶段不宣称既有 capability client 已完成独立目录归位。`BusinessServiceClients.cs` 中尚未迁移的既有 capability 类型属于过渡态；完整的 `Capabilities/<Capability>/` 目录归属、所有声明位置和语义闭包由后续 #2191（B）定义并执行。
- 新增或实质修改 capability client 的目录合同不得在 A 阶段通过向跨 capability 巨石追加类型来扩展；相关实现与治理必须遵循 #2191 的独立 PR 和验收合同。
- 仅做 A 的物理边界治理时，保持既有 namespace、公开签名、DI、HTTP URL、序列化、认证、幂等和异常语义不变。
- 修改 A 的 Shared 边界时，先更新 `BusinessGatewayCapabilityBoundaryTests` 并验证旧巨石、注释占位、重复真实声明和错误路径等目标错误结构会失败，再迁移源码。
