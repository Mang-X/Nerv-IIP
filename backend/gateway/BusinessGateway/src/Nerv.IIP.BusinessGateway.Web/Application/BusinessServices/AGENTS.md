# BusinessGateway capability client 边界

- #2159（#1222 子项①A）已完成三个无业务语义的 Shared 基础类型物理迁移：`BusinessServiceAuditContext`、`BusinessServiceProxyException` 和 `BusinessServiceHttpClient` 必须各自在 `Shared/` 下拥有唯一真实声明；`Shared/` 不得承载领域 DTO、URL、查询参数或业务失败规则。
- #2191（#1222 子项 B）只负责能力边界治理合同，不在一个 PR 中物理迁移全部 capability client。受管 client 的能力归属由语义关系闭包确定：从已登记的 capability interface/class 种子沿 base/interface 关系双向遍历；同一符号若落入多个能力，或其任一声明（包括 partial、嵌套声明）不在 `Capabilities/<Capability>/` 下，必须失败。三个 Shared 基础类型仅作为明确登记的共享基础设施排除，不得借此放过受管 client。
- B 同时冻结迁移过渡态的 legacy 声明清单：每个尚未物理迁移的受管公开 interface/class/config 声明必须保留完整相对路径和重复计数；新增、回迁、重复或跨目录声明必须失败。每完成一个物理迁移子项，就在同一合同中把该符号从 legacy 清单更新为独立能力目录合同。
- 当前 legacy 清单仍覆盖 `BusinessServiceClients.cs` 与 `BusinessConsoleWmsClient.cs` 中的既有受管声明。具体 capability client 的物理迁移由独立子项（#2160–#2174）各自交付，不得将这些文件迁移混入 B；公开 API 形状冻结属于后续 C 子项。
- 新增或实质修改 capability client 的目录合同不得通过向跨 capability 巨石追加类型来扩展；相关实现与治理必须遵循 #2191 及其物理迁移子项的独立 PR 和验收合同。
- 仅做 A 的物理边界治理时，保持既有 namespace、公开签名、DI、HTTP URL、序列化、认证、幂等和异常语义不变。
- 修改边界合同时，先更新 `BusinessGatewayCapabilityBoundaryTests` 并验证旧巨石新增、错误目录、partial/嵌套声明、双向继承/接口逃逸、legacy 回迁/重复以及合法非 client 配置等 mutation 分别得到 Red，再验证生产 Fact 与完整矩阵 Green。Shared 边界仍须保留注释占位、重复真实声明和错误路径的回归证据。
