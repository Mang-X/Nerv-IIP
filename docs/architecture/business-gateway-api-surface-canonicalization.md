# BusinessGateway client API surface canonicalization 合同

本文是 #1222 子项 C（#2192）的第一阶段合同。它冻结“公开 API surface 如何被规范化为可审阅文本 snapshot”，不把当前实现输出、一次运行结果或生成文件升级为语义来源。本阶段只提交合同，不改变生产 client、OpenAPI、生成客户端、数据库、UI 或真实 provider/full-chain 行为。

本文使用“必须/不得”表达实现约束；“应”表示后续 baseline 与治理实现的默认要求。任何不满足本文输入完整性、版本、语法或语义约束的运行都必须失败且不得写出 snapshot。

## 范围与独立来源

受管对象是 #2191 B 已归属的 BusinessGateway client 类型：`IBusiness<Capability>Client`、`HttpBusiness<Capability>Client` 以及其语义闭包中的 client 声明。wire DTO、普通 config 类型、`Shared/` 基础类和 private implementation 不成为 C 的独立 API owner；受管类型的 base/interface 身份仍记录在公开形状中。

GitHub Issue [#2192](https://github.com/Mang-X/Nerv-IIP/issues/2192) 的受控 spec 区块与本文是同等权威的 normative sources，不存在一方优先于另一方的解释顺序。两者必须对来源、字段、排序、编码、编译输入、版本和排除规则保持语义一致；任一差异、缺失或无法逐项对应都是阻断，实施者和审核者不得自行择一解释。`docs/architecture/api-contract-and-codegen.md` 只提供不冲突的公开契约背景约束，不能覆盖或补写本合同。

当前 source tree、编译器输出、运行时 reflection、既有 snapshot 或自动生成结果只能作为被测对象或诊断材料，不能反向定义 baseline。

## 语义输入与可复现编译

### 固定输入与 evaluated source manifest

compilation 的权威 source set 不是单一 glob，而是由固定工具链对 `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj` 做一次 evaluated build graph 后得到的 repository `Compile` item 集合，加上该 graph 明确产生的 generated C# support item 集合。评估参数固定为 `Configuration=Release`、`TargetFramework=net10.0`、`DesignTimeBuild=false`、`Deterministic=true`；执行 manifest 前必须在新建的空 intermediate staging 目录运行项目声明的 generated-source targets（包括 `GenerateGlobalUsings`），再读取该次 evaluation 的 repository `Compile` items、`GeneratedGlobalUsingsFile` 和其他明确的 generated C# outputs。不得读取调用目录、当前工作目录、任意旧 `obj` 文件或文件系统额外的 `*.cs`。

manifest 必须包含且只包含以下 evaluated `Compile` items：

- project 的全部 repository source，包括 `Application/BusinessServices/**/*.cs`、`Application/BusinessServices/Shared/**/*.cs` 和 `Application/Auth/**/*.cs`；因此 `BusinessGatewayIdempotencyKey`、`BusinessGatewayAuthorization` 等编译依赖不能被遗漏；
- 该次同工具链评估生成的全部 `.g.cs`/generated C# support items，至少包括 logical name 为 `Nerv.IIP.BusinessGateway.Web.GlobalUsings.g.cs` 的 `GeneratedGlobalUsingsFile` tree；禁止把仓库中残留的旧 generated file 当作输入；
- 其他由该 `.csproj` evaluated `Compile` item 或 generated-source target 明确列出的 project source/support tree；不以 `BusinessServices` glob 之外的手工 allowlist 替代 MSBuild 结果。

每个 repository item 的稳定 ID 是仓库相对路径（`/` 分隔）；每个 generated item 的稳定 ID 是 `generated://Nerv.IIP.BusinessGateway.Web/<evaluated-logical-name>`，不能带机器绝对路径、staging 路径或 `obj` 版本路径。manifest 按稳定 ID 的 UTF-8 bytes 排序，并记录 item kind、逻辑 ID、字节长度和 SHA-256；同一 ID 重复、evaluated item 缺失、generated support tree 未由本次 pinned evaluation 产生或 manifest 为空都必须失败。每个 item 必须是严格有效 UTF-8、无 BOM、无 CR、LF 换行；非法编码、读取失败或内容 hash 不一致均为 fatal error。

manifest 的 item 集合必须与加入 `CSharpCompilation` 的 syntax tree 集合逐项相等（每个 item 恰好一棵 tree，反之亦然）；B 已登记 client seed 只在这棵完整 compilation 上做语义过滤，不能把 source glob 当作 API closure。partial 与 nested declaration 的每个声明位置都先由 B 的目录合同裁决；同一个 Roslyn symbol 的多个 partial 位置合并为一条 type 记录，所有位置的显式成员合并后去重，C 不复制目录归属规则，也不把路径/行号写进公开 API identity。seed 缺失、重复匹配、无法解析、闭包为空或出现无法归属的 client 声明都必须失败。

### 固定工具链、解析选项和 references

后续实现必须在执行前检查并记录以下精确值；任一值不匹配时只报告环境不满足，不生成 snapshot：

| 项目 | 固定值 |
| --- | --- |
| TFM | `net10.0`（由 `backend/Directory.Build.props` 提供） |
| .NET SDK | `10.0.302` |
| `Microsoft.NETCore.App.Ref` | `10.0.10` 的 `net10.0` reference pack |
| `Microsoft.CodeAnalysis.CSharp` / `Microsoft.CodeAnalysis` | NuGet `5.0.0`，版本由 `backend/Directory.Packages.props` 锁定 |
| 输出 | `CSharpCompilation`，`OutputKind.DynamicallyLinkedLibrary` |

### 可重放 restore inputs、lock 与 producer artifact

Roslyn/reference closure 的 authority 还包括提交到仓库、可复核的 restore inputs；运行时自报本次下载得到的 hash 不能替代批准 artifact。C 本 PR 提交 manifest 和 evaluated ProjectReference 全图的 per-project lock fixture；它们是合同的一部分而不是 reviewer 在隔离副本中临时生成的文件：

- `docs/architecture/business-gateway-api-surface-restore.manifest.json` 固定 SDK、TFM、reference pack、Roslyn package 和输入文件的 SHA-256；输入包括根 `NuGet.config`（清空默认 sources，只允许 `https://api.nuget.org/v3/index.json` 及其 `nuget.org/*` mapping）、`backend/Directory.Packages.props`、`backend/Directory.Build.props`、`Nerv.IIP.BusinessGateway.Web.csproj` 以及完整 ProjectReference closure 的 lock files；manifest 自己列出每个 lock 的路径与 hash，禁止遗漏或重复。
- 该 manifest 的根 `toolchain.sdk` 是受管 CI .NET SDK 版本的唯一 owner；`.github/workflows/ci.yml` 只是消费者，不另立版本常量。`scripts/verify-ci-dotnet-sdk-authority.ps1` 从 manifest 单向读取精确三段版本，结构化核对全部受管 `actions/setup-dotnet` step，并把当前进程真实 `dotnet --version` 与同一值比较；manifest 或 workflow 缺失、重复、浮动、任一侧漂移及真实进程版本不一致均失败关闭。此门禁只证明受管 CI 配置和执行该门禁的进程工具链一致，hosted job 的最终运行身份仍须由各 job 日志与 MAN-661 evidence authority 独立证明。
- 每个 evaluated ProjectReference 项目的 `packages.lock.json` 都已按 SDK `10.0.302` 实际生成并提交，所有 lock schema 均为 `version=2`，target 仅为 `net10.0`。Direct package entry 必须有 `requested`、`resolved`、`contentHash`；CentralTransitive/Transitive package entry 必须有 `resolved`、`contentHash`；Project entry 必须绑定 evaluated project reference。禁止 floating version、额外 source、无 content hash 的 package entry 或未登记 transitive dependency。每个 lock 的 SHA-256 必须与 manifest 一致。

固定 restore 流程必须在仓库根目录使用空的 isolated package/cache staging（staging 路径不进入 identity），并把 lock path 明确指向已提交 artifact：

```text
dotnet restore <repo-root>/backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj \
  --configfile <repo-root>/NuGet.config --locked-mode --packages <isolated-package-cache> \
  -p:RestorePackagesWithLockFile=true -p:RestoreLockedMode=true \
  -p:RestoreForceEvaluate=false -p:RestoreIgnoreFailedSources=false
```

禁止隐式搜索其他 `NuGet.config`、用户 cache、runtime TPA 或未锁定的 restore；禁止在缺 artifact 时现场生成 lock。任一 evaluated ProjectReference 的 lock 缺失、schema/target 漂移、manifest 输入 hash 漂移、NuGet source/config/props/project hash 漂移、任一 lock 与生成的 `project.assets.json` target graph 不一致、package contentHash/reference pack hash 漂移、额外 package/source 或 restore diagnostic 非空，都必须 fail closed。SDK、TFM、reference pack 和 Roslyn 版本仍按 manifest 固定，任何升级必须先修订本文、Issue 受控区块和 lock fixture set。

`project.assets.json` 不是批准来源，而是上述命令的派生结果。后续 baseline/mutation PR 在写 snapshot 前必须执行同一 locked restore，并运行：

```text
dotnet msbuild <repo-root>/backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj \
  -getProperty:ProjectAssetsFile -getItem:ReferencePath -getItem:ProjectReference -nologo
```

producer 必须从该次 exact-head 的 `ProjectAssetsFile`、evaluated `ReferencePath`/`ProjectReference` 和固定 reference pack 生成带 exact head、每个 lock SHA、package identity/version/contentHash、project-reference、compile-asset、reference-file SHA-256 的 canonical `restore-graph` artifact，再逐项与 manifest 和 lock fixture set 比较。任何诊断、目标图、引用或 hash 不一致都不得写 snapshot。该 producer artifact 及其 required semantic restore lane 是后续实现 PR 的 Ready 前置条件，不能由当前 C 文档 PR 自行伪造。

C 当前只证明 manifest 与 lock fixture set 的静态可审阅性和文档一致性；本 PR 的 docs-only CI 没有执行上述 semantic restore，也不对它作绿灯宣称，更不外推为 canonicalizer、provider、FullChain 或真实基础设施证据。后续实现 PR 必须把 semantic restore lane、producer artifact 和 exact-head 结果作为 required evidence；若 lane 未执行或 artifact 不可复核，门禁保持阻断。

实现必须使用显式而非默认的 Roslyn 选项：

- `CSharpParseOptions(LanguageVersion.CSharp14, DocumentationMode.Parse, SourceCodeKind.Regular, preprocessorSymbols: empty)`；不得使用 `Preview`、机器环境变量、`DEBUG`、`TRACE` 或调用方临时 symbols；
- `CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: Enable, deterministic: true, concurrentBuild: false)`；nullable annotation 在 v1 的 type display 中按下文规则省略，但编译仍必须以 `Enable` 检查；
- 每个 syntax tree 只能由该 compilation 创建一个 `SemanticModel`；symbol、base/interface 和成员关系必须来自这个 model/compilation，不得回退到文本或反射；
- references 只来自固定 `net10.0` reference pack，以及 `Nerv.IIP.BusinessGateway.Web.csproj` 在该 SDK、TFM 和 restore graph 下的 evaluated compile-time closure：`project.assets.json` 的 `net10.0/compile` assets、`ReferencePath`/显式 `ProjectReference` 的编译输出和 central-version package assets；不得加入 runtime-only assets。不得使用当前进程的 `TRUSTED_PLATFORM_ASSEMBLIES`、运行时加载程序集、机器上任意版本的 DLL 或 provider 的运行时 reflection 结果；
- reference 清单必须按稳定的 project/package identity、规范化路径和 SHA-256 排序，并在运行记录 TFM、assembly identity、包版本、文件名和 SHA-256。缺少、重复、版本不符、restore graph 与 evaluated project 不一致或无法读取 reference 都是 fatal error；
- source bytes、parse options、compilation options、restore lock/asset graph、reference 清单和 Roslyn assembly version 必须作为运行诊断打印；这些诊断不属于 snapshot 内容，且诊断 hash 只能用于核对已批准 lock，不能自行成为批准来源。

### 诊断与覆盖的 fail-closed 规则

编译完成后必须收集全部 syntax、declaration、compilation diagnostics，按 `path, span-start, diagnostic-id, severity, message` 的 UTF-8 字节序排序。任何 `Error` 或 `Warning`（包括缺 reference、预处理分支、nullable、未解析 symbol 和 hidden/ informational 之外的编译问题）都使本次运行失败；`Info`/`Hidden` 只能记录，不能改变输出。诊断非空时不得降级为“尽力生成”或写部分文件。

在收集 symbol 前必须证明：

- evaluated source manifest 非空，manifest item 集合与加入 compilation 的 syntax tree 集合完全相等，数量相等且无重复；
- 每个 syntax tree 都成功使用固定 parse options 解析；所有活动的 type/member declaration 都能得到非空 declared symbol，partial declaration 共享同一 symbol 是允许且必须计数的闭包关系；
- B seed 集合和语义闭包中的每个 symbol 都能由 compilation 唯一解析，且同一 canonical identity 不得对应多个不等价 symbol；
- manifest item、generated support item、声明、partial 位置和输出记录数量被计数并校验；任何 source/support 遗漏、重复加入、不可达声明、无法绑定的 base/interface 或空的闭包都失败；
- 只有全部校验通过后，snapshot 才能以临时文件写入并原子替换；失败路径不得留下看似成功的空文件或旧文件副本作为本次结果。

## 文本 snapshot 格式

snapshot 使用 UTF-8、无 BOM、LF 换行。第一行固定为：

```text
api-surface-version=1
```

其余每行是 TAB 分隔记录。记录类型和字段键的顺序固定，不能增加、删除、重排或重复字段：

```text
type<TAB>identity=<value><TAB>kind=<value><TAB>arity=<value><TAB>access=<value><TAB>modifiers=<value><TAB>base=<value><TAB>interfaces=<value>
member<TAB>owner=<value><TAB>kind=<value><TAB>name=<value><TAB>arity=<value><TAB>access=<value><TAB>modifiers=<value><TAB>return=<value><TAB>parameters=<value><TAB>accessors=<value>
```

记录中的 `TAB`、LF、CR、`=`、`%` 以及所有字段值字符均不得作为未定义的转义语法使用。每条 record 必须带齐 schema 规定的字段；字段 key 缺失不是空值而是格式错误。记录解析器必须在第一枚 TAB 处分隔记录类型，在每个字段的第一枚 `=` 处分隔键和值；未知记录类型、未知/重复/缺失字段、额外 TAB、非法 percent escape、重复 record identity 或重复完整记录都必须拒绝。

### Percent-encoding

字段值保留 Roslyn 返回的 Unicode scalar sequence，不做 NFC/NFKC、locale 或大小写转换，再按 UTF-8 bytes 编码。唯一允许原样保留的字节集合是 ASCII `A-Z`、`a-z`、`0-9`、`.`、`_`、`~`；特别地，`-` 不在安全集合中，以便空值哨兵不产生歧义。其他每个 byte 必须写成 `%HH`，`H` 为大写十六进制。因而空格为 `%20`、TAB 为 `%09`、LF 为 `%0A`、`%` 为 `%25`、`:` 为 `%3A`、`;` 为 `%3B`、`<` 为 `%3C`、`>` 为 `%3E`、反引号为 `%60`，Unicode 非 ASCII bytes 也逐 byte percent-encode。

字段值的判定顺序固定为：先确认字段 key 存在，再检查 raw value 是否恰好是未编码的 `-`，最后才对非哨兵值执行 percent-decode。raw `-` 是字段级 `EmptyOrNotApplicable` 哨兵，不表示字段缺失；schema 允许它表示空 modifiers、无 base、空 interfaces/parameters、无 return 或不适用的 accessors。真实字段值 `-` 必须编码为 `%2D`，因此 `%2D` decode 后才是 literal hyphen，不能当哨兵。结构化字段若非空，先在未编码内部值中用 `-` 表示缺失槽位，再对整个字段统一 percent-encode，所以 accessor tuple 内的槽位输出为 `%2D`；空的整个结构化字段则直接输出 raw `-`。集合先按未编码 canonical item 的 UTF-8 bytes 排序，以 `;` 连接，再对整个连接字符串执行上述 percent-encoding；因此记录中不会出现裸 `;`，也不能依赖 split 后的机器行为。解析必须严格 decode 一次并拒绝畸形 `%`、非大写 hex、raw `-` 以外的空值或 decode 后再次编码不相同的值。

identity 内部的 global-namespace component `-` 属于非空 identity 字符串，经过整字段编码后写成 `%2D`；只有 raw field value 恰好等于单独的 `-` 才是 sentinel。实现不得在 percent-decode 后再做 sentinel 判断。

### 确定性分组与排序

输出顺序完全固定：首行 header；其后先输出全部 `type` records，再输出全部 `member` records；两组之间不得交错。每条 record 完成字段规范化和 percent-encoding 后，以“不含结尾 LF 的完整行”的 UTF-8 bytes 做无 locale、无大小写折叠的字节升序排序；排序相等即为重复记录并失败。该规则同时固定了 canonical type 总排序、type/member 分组顺序和 member 顺序，不依赖 source file、partial 片段、声明访问顺序或并发调度。

示例仅说明编码和分组，不是生产 baseline：

```text
api-surface-version=1
type	identity=global%3A%3ANerv.IIP.BusinessGateway.Web.Application.BusinessServices%3A%3AIBusinessInventoryClient%600	kind=interface	arity=0	access=public	modifiers=-	base=-	interfaces=-
member	owner=global%3A%3ANerv.IIP.BusinessGateway.Web.Application.BusinessServices%3A%3AIBusinessInventoryClient%600	kind=method	name=ListAsync	arity=0	access=public	modifiers=-	return=global%3A%3ASystem.Threading.Tasks.Task%3Cglobal%3A%3ANerv.IIP.BusinessGateway.Web.Application.BusinessServices.InventoryPage%3E	parameters=global%3A%3ASystem.String%3AinternalBearerToken%3Avalue%3Bglobal%3A%3ASystem.Threading.CancellationToken%3AcancellationToken%3Avalue	accessors=-
```

示例中的 `::`、`:`、`;`、`<`、`>`、空格（若存在）和反引号均已编码；实现不得以示例中便于阅读的裸分隔符为例外。

## Identity、type display 与公开词汇

### Type identity 与 type display

Type identity 不含 kind，因此 `class` 与 `record-class` 的变化表现为同一 identity 下的 `kind` 差异。identity 的规范语法是：

```text
global::<namespace-or->::<segment>[+<segment>...]
```

namespace segment 用 `.` 连接；nested type segment 用 `+` 连接；每个 segment 是 metadata name 加反引号和该 declaration 自己的 generic arity，例如 `Outer\`1+Inner\`2`，不使用累计 arity。global namespace 使用 `-`。namespace、containing type、名称、arity 均取 symbol，不取 source alias、using 或简单名称。

所有类型引用使用一个固定的 fully-qualified Roslyn display 规则：始终带 `global::`，使用 namespace 和 containing types，关闭 C# keyword/special-type alias，保留数组 rank、pointer、function-pointer、tuple 的结构；tuple element names 丢弃并规范化为对应 `System.ValueTuple` 类型；nested separator 使用 `.`。named type 的 type arguments 按 symbol 顺序递归显示，type parameter 不使用源名称而使用声明深度和序号（type-level 为 `!0`、`!1`，method-level 为 `!!0`、`!!1`）。实现使用等价于以下固定 `SymbolDisplayFormat` 的设置，未列 flags 均关闭：

| Roslyn display option | v1 值 |
| --- | --- |
| `globalNamespaceStyle` | `IncludeGlobalNamespace` |
| `typeQualificationStyle` | `NameAndContainingTypesAndNamespaces` |
| `genericsOptions` | `IncludeTypeParameters` |
| `miscellaneousOptions` | 不启用 `UseSpecialTypes`、`IncludeNullableReferenceTypeModifier`、`IncludeTupleElementNames`；其余 display flags 为 `None` |

v1 明确关闭 nullable annotation display：`string` 与 `string?` 的引用类型 display 均为同一非 nullable-annotated form；nullable/default-value 是否加入未来字段必须在后续 baseline PR 单独修订本合同，不能由当前实现暗中加入。数组、pointer、ref-kind 和 `dynamic` 的语义仍分别规范化为 `[]`/rank、`*`、参数 ref-kind 和 `global::System.Object`，不能依赖 source spelling。

### Type kind、accessibility 与 modifiers

`kind` 只有以下值：`class`、`record-class`、`struct`、`record-struct`、`interface`。受管 symbol 若为 enum、delegate 或其他未列 kind，必须报告 unsupported shape 并失败，而不是猜测为 class。

`access` 词汇固定为：`public`、`protected`、`protected-internal`、`private-protected`、`internal`、`private`、`not-applicable`。`protected internal` 与 `private protected` 是两个不同 token，不能折叠为 `protected`；type 和 member/accessor 均按 symbol 的真实声明 accessibility 取值。缺少 access 的记录只能使用 `not-applicable`，不得用空字符串替代。

Type `modifiers` 是固定顺序的去重 token 列表（以 `;` 连接后编码）：

```text
new;static;abstract;sealed;readonly;ref-like;unsafe
```

`record` 形状只由 `kind` 表达；interface 的隐含 abstract 不作为 `abstract` token；class 的隐含 `object` base 也不按 source 是否写出而变化。source 的修饰词顺序、`partial`、`async` 或仅影响实现的 `extern` 不进入 v1。

Member `modifiers` 使用固定顺序的同一列表子集：

```text
new;static;abstract;virtual;override;sealed;readonly;const;required;ref-return;ref-readonly-return;unsafe
```

只有实际影响公开声明形状的 semantic flags 才可输出；列表顺序不随 source 变化。`async`、method/property body、局部实现细节不输出。没有 token 写 `-`。

## Type 与 member 字段语义

### Type 记录

每个受管 type 只生成一条记录，字段含义固定如下：

- `identity`：完整 namespace、containing type、名称和每段 declaration arity，按上文 identity 语法；
- `kind`/`arity`：kind 取固定词汇，arity 是该 type declaration 自身 generic arity；
- `access`/`modifiers`：按上文词汇和固定顺序；
- `base`：effective base identity。class 无显式 base 时固定为 `global::System.Object`，struct 固定为 `global::System.ValueType`，interface 固定为 `-`；显式 base 解析后的 identity 替代 source spelling。显式写 `object` 与省略 base 因而得到相同结果；
- `interfaces`：只列 source 直接列出的 interface identities，不展开继承闭包，不使用 `AllInterfaces`；class 的直接 base interface 与 interface 的直接 base interface 均按 canonical identity 排序后编码。缺 reference 或 direct relationship 无法解析即失败。

generic parameter 在 identity、type display、base、interface、return 和 parameter 中都按 declaration position 规范化；修改源文件中的 `T` 名称但不改 arity/位置不得制造差异。

### Member 记录

只记录受管 type **显式声明**的 `public`、`protected`、`protected-internal`、`private-protected` 成员，以及上文唯一例外的 source-origin primary constructor；`internal`、`private` 成员不生成 member record，但其 body 变化仍不能使受管 type 的公开记录缺失。继承成员通过 type 的 base/interface identity 组合，不重复展开。

`kind` 词汇固定为 `constructor`、`method`、`property`、`indexer`、`event`、`field`、`operator`、`conversion`。constructor 的 `name` 为 `.ctor`；indexer 的 name 为 metadata name `Item`；operator/conversion 使用 Roslyn metadata name（例如 `op_Addition`、`op_Implicit`），普通 method/property/event/field 使用 symbol name。static constructor、compiler-generated member 和无法归入上述 kind 的受管成员必须被排除或报告 unsupported，而不能悄悄改名。

### Constructor 的唯一收录规则

constructor 是唯一需要区分 source declaration 与 compiler synthesis 的 member kind，规则固定如下：

- 普通显式 constructor 只收录其对应的 `IMethodSymbol`（`MethodKind.Constructor` 且 `DeclaringSyntaxReferences` 指向 `ConstructorDeclarationSyntax`）一次；不再从 syntax tree 另造第二条记录；
- C# primary constructor 的参数列表是 source declaration。对带 `TypeDeclarationSyntax.ParameterList` 的受管 type，收录 Roslyn 生成的、与该 parameter list 关联的唯一 `IMethodSymbol`（即使 `IsImplicitlyDeclared=true`），把参数列表按下文 `parameters` 规则写成一条 `.ctor` record；同一 primary constructor 的 syntax 与 generated symbol 不得各自产生记录；
- 普通隐式无参 constructor、record copy constructor/clone、static constructor、primary parameter capture 的 compiler-generated field/property 以及其他 `IsImplicitlyDeclared` member 均不收录。只有上一个条款明确标记为 source-origin primary constructor 的 generated `.ctor` 是例外；
- primary constructor 的 accessibility、modifiers、parameter type/ref-kind/name 取其唯一关联 constructor symbol；若 Roslyn 对 generated symbol 返回 `NotApplicable` 或与 type declaration 的有效 accessibility 不一致，canonicalizer 必须失败而不是猜测；
- 去重键是 `(owner identity, constructor, .ctor, method arity, access, modifiers, return, parameters, accessors)`。同一 source-origin 产生两个不等价 candidate，或 source explicit constructor 与 generated candidate 无法一一对应，均为 fatal error；
- primary constructor 改为具有相同 canonical parameters 的普通显式 constructor 时，constructor record 应保持相同；参数名称、type、ref-kind、accessibility 或公开 modifier 变化必须改变 record。primary constructor body、capture implementation 和其他 private body 仍在排除范围内。

字段规则如下：

- `owner` 是 type identity；`arity` 是 method generic arity，非 generic method 写 `0`；
- constructor 的 `return` 为 `-`，其他 callable 的 `return` 为 canonical type display；property/indexer/event/field 的 `return` 为其 canonical member type；
- `parameters` 按声明顺序保留。每个参数的 canonical tuple 是 `ref-kind:type-display:name:params-flag`，其中 `ref-kind` 固定为 `value`、`ref`、`out`、`in`、`ref-readonly`、`scoped-ref`、`scoped-in`、`scoped-ref-readonly`，`params-flag` 为 `params` 或 `-`；tuple 以 `;` 连接后整体编码。默认值不进入 v1；参数名称保留，因为 named argument 是公开调用形状；
- `accessors` 固定为 `get:<access>;set:<access>;init:<access>`，顺序不可变；不存在的 accessor 写 `-`，private/internal accessor 仍按真实 token 记录，以免 setter/getter 的可见性变化逃逸；不适用的成员写整字段 `-`；
- `access` 与 `modifiers` 记录声明本身的 semantic shape；`abstract`、default interface member、override、ref return 等变化必须体现在字段中。

member record 由 owner、kind、name、arity、access、modifiers、return、parameters、accessors 的规范化组合唯一确定。同一 type 的 partial source 顺序、文件名、行号和 private implementation body 变化不得改变记录。

## 失败边界、排除项与后续证据

以下内容不进入 v1 snapshot：

- method/property/constructor body、局部变量、局部函数、控制流和 private implementation body；
- private/internal member 的独立记录、注释、XML 文档、attribute 文本、source path、line number 和编译器生成的隐式成员；
- 当前运行时 reflection 结果、provider 行为、数据库内容、OpenAPI 生成文件和前端生成文件；
- nullable/default-value 字段，除非后续 baseline PR 以本合同修订和字段表单独批准；
- 不属于 #2192 批准字段的实现细节。

合同实现必须 fail closed：不能以声明计数、文件名、简单名、注释、正则或“有些 symbol 成功”代替完整语义输入；不能在 diagnostic、reference、source coverage 或 unsupported shape 失败后继续写 snapshot。pure Roslyn fixture 只能证明语义 canonicalization 和治理规则；未运行 PostgreSQL、Redis/CAP、FullChain、浏览器或生产环境，不得把合同测试外推为这些 provider/lane 的证明。

合同审阅通过后，后续 baseline PR 才能生成 snapshot。baseline 必须从本合同与固定 evaluated source manifest 逐项推导，人工审阅每一处差异；不能运行当前实现后批量刷新并把全量输出当作预期。后续治理至少要保留这些可编译 mutation，并对每个 mutation 先证明旧规则 Red、再证明修复规则 Green：

- `class` ↔ `record-class`、`sealed` ↔ `unsealed`；
- interface default public method 的新增或删除；
- base/interface 的新增、删除或改变方向；
- namespace、name 或 generic arity 的变化；
- public/protected/protected-internal/private-protected member 的新增、删除、可见性或公开 modifier 变化；
- source primary constructor 与 canonical parameters 相同的显式 constructor 互换必须保持同一 record；primary parameter name/type/ref-kind/access 变化必须先 Red、修复后 Green，普通隐式/default/copy constructor 仍不得生成记录且不得重复归档；
- private implementation body 变化必须保持允许。

每个 mutation 需要断言 exact identity、差异维度和允许/拒绝原因。baseline/mutation PR 另行交付；本合同 PR 不落 baseline、mutation、生产 client、OpenAPI、生成客户端、数据库、UI、provider 或 FullChain 行为。
