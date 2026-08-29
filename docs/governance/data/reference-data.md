# ReferenceData / CodeSet 治理

本页规定 MasterData ReferenceData / CodeSet 的**稳定维护规则**。当前 CodeSet、码值与字段映射查询见 [`../../reference/master-data/dictionary.md`](../../reference/master-data/dictionary.md)；运行时事实以 MasterData seed、API、校验器和消费代码为准。

## CodeSet 与码值

1. **CodeSet 名平台保留。** 工厂不能自行创建或改名平台保留的 CodeSet；如需新增长期分组，先评估它是否应成为结构化独立目录。
2. **系统枚举受平台约束。** 带系统行为语义的标准 code 与语义不得由租户任意改写或扩张；租户只能在实现允许的边界内启用/停用。
3. **可维护集合不物理删除。** 平台预置+可维护或工厂自定义 CodeSet 可以按能力新增/停用码值，但已被历史事实引用的 code 不得通过物理删除破坏追溯。
4. **Code 唯一且稳定。** `(OrganizationId, EnvironmentId, CodeSet, Code)` 必须唯一；code 一旦被引用不得换码，展示 Name 可以在保留语义的前提下修改。
5. **停用优先于删除。** 新业务只能引用当前允许且启用的 code；历史事实继续保留对停用 code 的引用。停用前应按拥有该事实的服务检查仍在使用的启用主数据。
6. **写入时校验。** 受控字段在创建/更新时必须由拥有该写路径的后端校验其目录存在性、启用状态和必要的结构化约束；不得只依赖前端下拉。

## 独立目录与 legacy CodeSet

- 当值域需要层级、证书、严重度、默认处置或其它结构化属性时，优先使用独立目录实体/API，而不是继续扩张扁平 CodeSet。
- ProductCategory、Skill、QualityReason 等独立目录切换期间允许保留 legacy CodeSet 兼容读取，但新写路径的权威值域必须清晰且单一。
- legacy code 的正式退役必须有明确迁移与兼容方案；不得通过 seed 或前端常量静默重写历史值。

## Producer / consumer 对齐

1. MasterData seed/目录 API 是运行时目录事实的主要生产者；前端 `masterDataReference.ts` 只是离线兜底，不得反向定义服务端值域。
2. 已接线的前端离线兜底 code 集合必须与当前服务端兼容集合一致；中文 label 不应在实时目录与离线兜底之间表达不同业务语义。
3. 任何新增/删除/改语义的 CodeSet 或标准 code，都必须同时复核：后端 seed/校验器、公开目录 API、当前前端消费者以及 [`../../reference/master-data/dictionary.md`](../../reference/master-data/dictionary.md)。
4. 不要求没有消费关系的前端包复制全部 CodeSet；按实际消费者维护最小必要兜底。
5. `inventory-location` 等跨域部署约定不因此获得另一个领域的实体所有权；实际库存库位仍由 Inventory 的 `StockLocation` 拥有。

## 变更边界

- Reference 文档不能用阶段完成清单、旧 Issue 状态或历史 seed 数量证明当前实现；这些内容进入报告或 tracker。
- 不为 CodeSet 文档建立永久 JSON registry、自然语言同步器或独立 CI step。需要可执行一致性时，优先在真正 producer/consumer 的行为测试中验证。
- 发现文档与代码不一致时先确认运行时 producer，再修正文档或实现；不得为了“让文档一致”直接修改客户/租户数据。
