# BusinessGateway client API surface canonicalization 治理

本文是 BusinessGateway client 公开 API surface canonicalization 的当前唯一人工 Governance 入口。迁移前合同与形成过程冻结在 [`../../reports/audits/business-gateway-api-surface-canonicalization.md`](../../reports/audits/business-gateway-api-surface-canonicalization.md)；它们只用于历史核对，不再与本文构成并列权威。

固定 SDK/TFM/reference pack/Roslyn package、输入文件 SHA-256 和 ProjectReference lock fixture 集合由 [`../../reference/api/business-gateway-surface-restore.manifest.json`](../../reference/api/business-gateway-surface-restore.manifest.json) 提供机器事实。本文规定语义和 fail-closed 规则，不复制机器 manifest 中易漂移的版本/哈希清单。

## 范围与事实来源

受管对象是已归属的 `IBusiness<Capability>Client`、`HttpBusiness<Capability>Client` 以及其语义闭包中的 client 声明。wire DTO、普通 config、`Shared/` 基础类和 private implementation 不成为独立 API owner；受管 type 的 base/interface identity 仍属于公开 shape。

当前 source tree、编译器输出、运行时 reflection、既有 snapshot 或自动生成结果只能作为被测对象或诊断材料，不能反向定义 baseline。API/codegen 的一般公开契约背景见 [`contracts-and-codegen.md`](./contracts-and-codegen.md)；与本 canonicalization 合同冲突时，以本文更窄的规则为准。

## 语义输入与 evaluated source manifest

canonicalization 的权威 source set 必须由固定工具链对 `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj` 的 evaluated build graph 得到 repository `Compile` items，并加入同次 evaluation/generated-source targets 明确产生的 C# support items。不得使用工作目录扫描、手写 glob allowlist、旧 `obj`、运行时 reflection 或文件系统额外 `*.cs` 替代 evaluated input。

每个 source item 必须满足：

- repository item 使用仓库相对路径作为稳定 ID；generated item 使用 `generated://Nerv.IIP.BusinessGateway.Web/<evaluated-logical-name>`，不写机器绝对路径或 staging/`obj` 路径；
- manifest 按稳定 ID 的 UTF-8 bytes 排序，记录 item kind、logical ID、字节长度与 SHA-256；
- 内容必须是严格 UTF-8、无 BOM、LF 换行；重复 ID、缺失 item、读取失败、hash 不一致或空 source set 都必须失败；
- manifest item 集合与加入 `CSharpCompilation` 的 syntax tree 集合逐项相等，每个 item 恰好对应一棵 tree，反向也不能有额外 tree；
- partial/nested declaration 的归属先由既有目录 owner 规则裁决；同一 Roslyn symbol 的多个 partial 位置合并为一个 type identity，不能把 source path/line 变成 API identity。

seed 缺失、重复匹配、无法解析、语义闭包为空或出现无法归属的 client 声明都必须 fail closed。

## 固定 restore 与 references

restore/reference closure 必须由 Reference manifest 与其登记的 per-project lock fixture set 决定：

- 使用 manifest 固定的 SDK、TFM、reference pack、Roslyn package 与输入 hash；
- 使用仓库 `NuGet.config`、locked mode 和独立 package/cache staging；不得读取隐式其他 NuGet config、用户 cache 事实、runtime TPA 或机器任意 DLL；
- 所有 evaluated ProjectReference 的 lock 必须存在并与 manifest hash/目标图一致；不得在缺 fixture 或发生 drift 时现场刷新后继续；
- `project.assets.json` 是 locked restore 的派生结果，不是批准来源；reference graph 必须能回到 manifest、lock 与同次 evaluated project graph；
- package/reference/project graph 缺失、重复、版本/hash 不符、额外 source、restore diagnostic 或无法读取 reference 都必须失败。

精确 restore/msbuild 操作从 [`../../runbooks/api-codegen.md`](../../runbooks/api-codegen.md) 路由回本合同和实际脚本/CLI；本页不维护第二份机器版本表。

## Roslyn 解析与编译语义

实现必须显式固定而非依赖默认值：

- `CSharpParseOptions(LanguageVersion.CSharp14, DocumentationMode.Parse, SourceCodeKind.Regular, preprocessorSymbols: empty)`；不得引入 `Preview`、机器环境变量或调用方临时 symbols；
- `CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: Enable, deterministic: true, concurrentBuild: false)`；
- 每个 syntax tree 由该 compilation 创建语义模型；type/member/base/interface 必须来自 Roslyn semantic model，不回退到正则、文本或 reflection；
- compile-time references 只来自固定 reference pack 与 evaluated restore graph 的 compile closure，不加入 runtime-only assets；
- source bytes、parse/compilation options、restore/lock graph、reference 清单和 Roslyn assembly version 必须进入运行诊断，但这些诊断本身不成为批准来源。

## 诊断与覆盖 fail-closed

收集 syntax、declaration 与 compilation diagnostics，并使用稳定顺序输出。任何 `Error` 或 `Warning` 都阻止 snapshot 写入；`Info`/`Hidden` 只能记录，不能改变输出。

写 snapshot 前至少必须证明：

1. evaluated source manifest 非空且与 syntax trees 完全相等；
2. 每棵 tree 都按固定 parse options 成功解析；
3. 活动 type/member declaration 都能得到 declared symbol；partial declaration 的共享 symbol 关系可解释；
4. seed 与语义闭包中的 symbol 能唯一解析，不出现同一 canonical identity 对应多个不等价 symbol；
5. source/support/declaration/partial/output 计数闭合，没有遗漏、重复、不可达或无法绑定关系。

只有全部检查通过后才能通过临时文件原子替换 snapshot。失败路径不得留下新的空/半成品文件，也不得把旧 snapshot 冒充本次成功结果。

## Snapshot 文本协议

snapshot 使用 UTF-8、无 BOM、LF。第一行固定：

```text
api-surface-version=1
```

其余记录是 TAB 分隔，并只允许两种 schema：

```text
type<TAB>identity=<value><TAB>kind=<value><TAB>arity=<value><TAB>access=<value><TAB>modifiers=<value><TAB>base=<value><TAB>interfaces=<value>
member<TAB>owner=<value><TAB>kind=<value><TAB>name=<value><TAB>arity=<value><TAB>access=<value><TAB>modifiers=<value><TAB>return=<value><TAB>parameters=<value><TAB>accessors=<value>
```

字段 key 缺失不是空值。解析器在第一枚 TAB 分隔 record type，在每个字段的第一枚 `=` 分隔 key/value；未知/重复/缺失字段、额外 TAB、非法 escape、重复 record identity 或重复完整记录必须拒绝。

### Percent-encoding

字段值保留 Unicode scalar sequence，不做 NFC/NFKC、locale 或大小写转换，再按 UTF-8 bytes 编码。唯一可原样保留的是 ASCII `A-Z`、`a-z`、`0-9`、`.`、`_`、`~`；其它 byte 写成大写 `%HH`。

raw `-` 是字段级 `EmptyOrNotApplicable` sentinel，只在 **decode 前** 判断。真实字符 `-` 必须编码为 `%2D`。结构化字段的内部缺失槽位先用 `-` 表示，再对整个结构统一 percent-encode；非空集合先按未编码 canonical item 的 UTF-8 bytes 排序、以 `;` 连接，再整体编码。

parser 必须严格 decode 一次并拒绝 malformed `%`、非大写 hex、raw `-` 之外的非法空值，以及 decode 后重新 canonical encode 不相同的值。

### 分组与排序

输出顺序固定为 header → 全部 `type` records → 全部 `member` records。每组以完成规范化和 percent-encoding 后的完整行（不含结尾 LF）的 UTF-8 bytes 升序排序，不使用 locale 或大小写折叠；排序相等意味着重复记录并失败。

## Type identity 与 display

Type identity 不含 kind，语法固定为：

```text
global::<namespace-or->::<segment>[+<segment>...]
```

namespace segment 用 `.`，nested type segment 用 `+`；每个 segment 使用 metadata name 加该 declaration 自身 generic arity。global namespace component 为 `-`，进入非空 identity 后按普通字符编码。

类型引用使用 fully-qualified Roslyn semantic display：始终包含 `global::`、namespace 和 containing types；不使用 C# special-type alias；保留数组 rank、pointer/function-pointer 等结构；tuple element names 不进入 identity；nested display separator 使用 `.`；generic parameter 以 declaration position 规范化，type-level 为 `!n`、method-level 为 `!!n`，不依赖源参数名。

v1 不输出 nullable reference annotation；`dynamic` 规范化为 `global::System.Object`。无法按固定 display 规则表达的 shape 必须失败，不能猜测字符串。

## Type 记录

`kind` 只允许 `class`、`record-class`、`struct`、`record-struct`、`interface`；其他受管 kind 必须作为 unsupported shape 失败。

`access` 只允许 `public`、`protected`、`protected-internal`、`private-protected`、`internal`、`private`、`not-applicable`。

Type `modifiers` 使用固定顺序：

```text
new;static;abstract;sealed;readonly;ref-like;unsafe
```

每个受管 type 只生成一条记录：

- `identity`：完整 namespace/containing type/name/arity；
- `kind` / `arity` / `access` / `modifiers`：按上述固定词汇；
- `base`：effective base identity；无显式 class base 时为 `global::System.Object`，struct 为 `global::System.ValueType`，interface 为 raw `-`；
- `interfaces`：只列 source 直接声明的 interface identities，不展开 `AllInterfaces`，按 canonical identity 排序。

source modifier 顺序、`partial`、`async`、文件名和行号不进入 type identity。

## Member 记录

只记录受管 type 显式声明的 `public`、`protected`、`protected-internal`、`private-protected` 成员，以及下述 source-origin primary constructor 例外；继承成员通过 base/interface identity 表达，不重复展开。

`kind` 只允许 `constructor`、`method`、`property`、`indexer`、`event`、`field`、`operator`、`conversion`。constructor name 为 `.ctor`，indexer 使用 metadata name `Item`，operator/conversion 使用 Roslyn metadata name。

Member `modifiers` 使用固定顺序：

```text
new;static;abstract;virtual;override;sealed;readonly;const;required;ref-return;ref-readonly-return;unsafe
```

字段语义：

- `owner` 为 type identity；method `arity` 为其 generic arity；
- constructor `return` 为 raw `-`，其它成员使用 canonical type display；
- `parameters` 保持声明顺序，每项为 `ref-kind:type-display:name:params-flag`，ref-kind 只允许 `value`、`ref`、`out`、`in`、`ref-readonly`、`scoped-ref`、`scoped-in`、`scoped-ref-readonly`；默认值不进入 v1；
- `accessors` 固定为 `get:<access>;set:<access>;init:<access>`，不存在的 slot 使用内部 `-`；不适用成员的整个字段使用 raw `-`；
- private/internal accessor 的真实 access 仍记录，以捕获公开 shape 变化。

## Constructor 唯一收录规则

- 普通显式 constructor 只从其唯一 `IMethodSymbol` 收录一次。
- 带 source primary-constructor parameter list 的受管 type，只收录与该 source 参数列表对应的唯一 generated constructor symbol；syntax 与 generated symbol 不得各生成一条。
- 普通隐式无参 constructor、record copy/clone、static constructor、primary parameter capture 产生的 generated field/property，以及其它 compiler-generated member 不收录。
- primary constructor 的 access/modifiers/parameter type/ref-kind/name 来自关联 symbol；无法唯一对应或 Roslyn 返回无法解释的 accessibility 时失败。
- primary constructor 与 canonical parameters 相同的普通显式 constructor 互换应保持同一 canonical record；参数名/type/ref-kind/access 或公开 modifier 变化必须产生差异。

## 排除项与变更证据

v1 不记录：method/property/constructor body、局部变量/局部函数/控制流、private implementation body、private/internal member 的独立记录、注释/XML 文档、attribute 文本、source path/line、runtime reflection、provider/数据库行为、OpenAPI/generated frontend 文件，以及未被本 schema 批准的 nullable/default-value 字段。

任何 canonicalizer/baseline 变更都必须从本规则与固定机器输入推导，不能运行当前实现后批量刷新输出并把它当作预期。至少要有可编译 mutation 证明这些边界：

- type kind、sealed/unsealed、base/interface、namespace/name/generic arity 的公开变化会产生差异；
- default interface/public/protected 系列成员的新增、删除、access 或公开 modifier 变化会产生差异；
- primary constructor 与等价显式 constructor 的 canonical equivalence 不产生伪差异，参数 shape 变化会产生差异；
- private implementation body 变化保持允许。

每个 mutation 必须断言 exact identity、差异维度和允许/拒绝原因。pure Roslyn/canonicalization 证据只证明该规则本身；未运行的 PostgreSQL、Redis/CAP、FullChain、浏览器或真实 provider 行为不得被外推为已验证。
