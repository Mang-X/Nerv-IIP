# BusinessGateway client API surface canonicalization 合同

本文是 #1222 子项 C（#2192）的第一阶段合同。它冻结“公开 API surface 如何被规范化为可审阅文本 snapshot”，不把当前实现输出、一次运行结果或生成文件升级为语义来源。本阶段只提交合同，不改变生产 client、OpenAPI、生成客户端、数据库、UI 或真实 provider/full-chain 行为。

## 范围与来源

受管对象是 #2191 B 已归属的 BusinessGateway client 类型：`IBusiness<Capability>Client`、`HttpBusiness<Capability>Client` 以及其语义闭包中的 client 声明。wire DTO、普通 config 类型、`Shared/` 基础类和 private implementation 不成为 C 的独立 API owner；受管类型的 base/interface 身份仍记录在公开形状中。

合同来源按以下优先级解释：

1. GitHub Issue [#2192](https://github.com/Mang-X/Nerv-IIP/issues/2192) 的 Scope Gate 与验收合同；
2. 本文冻结的字段、排序、编码和排除规则；
3. `docs/architecture/api-contract-and-codegen.md` 中关于公开契约、兼容性和 Gateway 边界的规则。

当前 source tree、编译器输出、运行时 reflection、既有 snapshot 或自动生成结果只能作为被测对象或诊断材料，不能反向定义 baseline。

## 语义输入

canonicalizer 固定读取 `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/**/*.cs`，用 Roslyn `CSharpCompilation` 和 `SemanticModel` 解析 symbol。它必须按完整 namespace 和 symbol identity 识别声明，不能使用简单名称、注释、正则、文件名启发式或运行时反射替代语义解析。

只纳入 B 已登记 client seed 的语义闭包。partial 与 nested declaration 的每个声明位置都先由 B 的目录合同裁决；C 不复制目录归属规则，也不把路径/行号写进公开 API identity。

## 文本 snapshot 格式

snapshot 使用 UTF-8、无 BOM、LF 换行。第一行固定为：

```text
api-surface-version=1
```

其余每行是 TAB 分隔记录，记录类型和字段顺序固定：

```text
type<TAB>identity=<value><TAB>kind=<value><TAB>arity=<value><TAB>access=<value><TAB>modifiers=<value><TAB>base=<value><TAB>interfaces=<value>
member<TAB>owner=<value><TAB>kind=<value><TAB>name=<value><TAB>arity=<value><TAB>access=<value><TAB>modifiers=<value><TAB>return=<value><TAB>parameters=<value><TAB>accessors=<value>
```

字段值使用 UTF-8 percent-encoding，编码结果采用大写十六进制；空集合和不存在的值统一写 `-`。字段键不得重排，不能依赖机器区域性、当前目录、行号或文件遍历顺序。示例只是格式说明，不是生产 baseline：

```text
api-surface-version=1
type	identity=Nerv.IIP.BusinessGateway.Web.Application.BusinessServices::interface:IBusinessInventoryClient`0	kind=interface	arity=0	access=public	modifiers=-	base=-	interfaces=-
member	owner=Nerv.IIP.BusinessGateway.Web.Application.BusinessServices::interface:IBusinessInventoryClient`0	kind=method	name=ListAsync	arity=0	access=public	modifiers=-	return=System.Threading.Tasks.Task%3CInventoryPage%3E	parameters=string%20internalBearerToken%3BSystem.Threading.CancellationToken%20cancellationToken	accessors=-
```

## 记录字段

### Type 记录

每个受管 type 记录：

- 完整 namespace、containing-type identity、名称和 generic arity；identity 的 nested type 使用稳定的 containing-type 连接形式，不能只保留简单名称；
- `class`、`interface`、`struct`、`record`、`record struct` 等公开形状；`class` 与 `record` 必须可区分；
- 声明 accessibility；
- 影响公开形状的 modifiers，至少覆盖 `abstract`、`sealed`、`static`、`readonly`、`ref` 和 record 形状；
- base identity；
- 直接 interface identities。

base 和 interface 集合按 canonical identity 使用 `StringComparer.Ordinal` 排序。generic parameter 在类型位置按声明序号规范化，不能把源文件中的局部命名差异当作 type identity。

### Member 记录

只记录受管 type **显式声明**的 public/protected 成员：constructor、method、property/indexer、event、field、operator 和 conversion。继承成员通过 type 的 base/interface identity 组合，不重复展开。

每个 member 记录：

- owner identity、member kind、名称和 generic arity；
- accessibility 与影响公开形状的 modifiers；
- 返回类型或成员类型；
- 参数顺序、参数类型、参数名称和 `ref`/`out`/`in`/`params` 等 ref-kind；
- property/indexer 的 accessor 形状（`get`/`set`/`init` 及各自可见性）；
- 对没有 accessor 的成员写 `-`。

member key 使用 owner、kind、name、arity、类型、参数和 modifier 的规范化组合，最终以 `StringComparer.Ordinal` 排序。同一声明的 source 文件顺序、partial 片段顺序和私有成员顺序不得改变 snapshot。

## 明确排除

以下内容不进入 v1 snapshot：

- method/property/constructor body、局部变量、局部函数、控制流和 private implementation body；
- private 成员、注释、XML 文档、source path、line number 和编译器生成的隐式成员；
- 当前运行时 reflection 结果、provider 行为、数据库内容、OpenAPI 生成文件和前端生成文件；
- 不属于 #2192 批准字段的实现细节；nullable/default-value 只有在后续 baseline PR 按字段表单独裁决后才可加入，不能由当前输出静默决定。

record/class、sealed/unsealed、public/protected member 的可观察变化不能借“body 被排除”掩盖：它们必须由 type/member 记录或形状字段捕获。

## Baseline 与 mutation 证据

合同审阅通过后，后续 baseline PR 才能生成 snapshot。baseline 必须从本合同与固定 source tree 逐项推导，人工审阅每一处差异；不能运行当前实现后批量刷新并把全量输出当作预期。

后续治理至少要保留这些可编译 mutation，并对每个 mutation 先证明旧规则 Red、再证明修复规则 Green：

- `class` ↔ `record`、`sealed` ↔ `unsealed`；
- interface default public method 的新增或删除；
- base/interface 的新增、删除或改变方向；
- namespace、name 或 generic arity 的变化；
- public/protected member 的新增、删除、可见性或公开 modifier 变化；
- private implementation body 变化必须保持允许。

每个 mutation 需要断言 exact identity、差异维度和允许/拒绝原因。pure Roslyn fixture 只能证明语义 canonicalization 和治理规则；未运行 PostgreSQL、Redis/CAP、FullChain、浏览器或生产环境，不得把合同测试外推为这些 provider/lane 的证明。
