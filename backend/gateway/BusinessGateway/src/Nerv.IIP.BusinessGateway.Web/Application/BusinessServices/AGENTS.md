# BusinessGateway capability client 边界

- 受管 client 的归属由登记的 interface/class 种子沿继承和接口关系双向确定。所有声明（包括 partial 和嵌套类型）必须位于对应的 `Capabilities/<Capability>/` 下。跨能力归属、重复声明和错误目录由 `BusinessGatewayCapabilityBoundaryTests` 验证。
- `BusinessServiceAuditContext`、`BusinessServiceProxyException` 和 `BusinessServiceHttpClient` 必须各自在 `Shared/` 下拥有唯一真实声明。`Shared/` 不承载领域 DTO、URL、查询参数或业务失败规则。
- 已退役的 `BusinessServiceClients.cs` 与 `BusinessConsoleWmsClient.cs` 不得恢复。新增 client 时，在对应能力目录实现并更新边界测试中的种子。
- 修改目录合同时，验证回迁、错误目录、partial、嵌套声明、双向继承和接口逃逸会失败，并验证合法的非 client 配置仍通过。
