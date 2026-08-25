# BusinessGateway client API surface canonicalization 合同

本文是 #1222 子项 C（#2192）的第一阶段合同。它冻结“公开 API surface 如何被规范化为可审阅文本 snapshot”，不把当前实现输出、一次运行结果或生成文件升级为语义来源。本阶段只提交合同，不改变生产 client、OpenAPI、生成客户端、数据库、UI 或真实 provider/full-chain 行为。

本文使用“必须/不得”表达实现约束；“应”表示后续 baseline 与治理实现的默认要求。任何不满足本文输入完整性、版本、语法或语义约束的运行都必须失败且不得写出 snapshot。

## 范围与独立来源

受管对象是 #2191 B 已归属的 BusinessGateway client 类型：`IBusiness<Capability>Client`、`HttpBusiness<Capability>Client` 以及其语义闭包中的 client 声明。wire DTO、普通 config 类型、`Shared/` 基础类和 private implementation 不成为 C 的独立 API owner；受管类型的 base/interface 身份仍记录在公开形状中。

合同来源按以下优先级解释：

1. GitHub Issue [#2192](https://github.com/Mang-X/Nerv-IIP/issues/2192) 的 Scope Gate 与验收合同；
2. 本文冻结的字段、排序、编码、编译输入和排除规则；
3. `docs/architecture/api-contract-and-codegen.md` 中关于公开契约、兼容性和 Gateway 边界的规则。

当前 source tree、编译器输出、运行时 reflection、既有 snapshot 或自动生成结果只能作为被测对象或诊断材料，不能反向定义 baseline。

## 语义输入与可复现编译

### 固定输入

canonicalizer 必须读取以下完整 source tree，不得由调用目录、当前工作目录或文件系统枚举顺序改变输入：

```text
backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/**/*.cs
```

文件路径统一为仓库相对路径、使用 `/`、按 UTF-8 字节序排序后加入 compilation。每个文件必须是严格有效的 UTF-8、不得带 BOM、不得含 CR、必须使用 LF；非法编码、BOM、CR 或读取失败均为 fatal error。不得用 `File.ReadAllText` 的系统默认编码猜测替代带显式 `Encoding.UTF8` 的读取。当前 CI 的 `setup-dotnet 10.0.x` 必须解析为上述 SDK；若解析到其他 patch，必须先更新本合同和证据，不得静默接受。

只纳入 B 已登记 client seed 的语义闭包。partial 与 nested declaration 的每个声明位置都先由 B 的目录合同裁决；同一个 Roslyn symbol 的多个 partial 位置合并为一条 type 记录，所有位置的显式成员合并后去重，C 不复制目录归属规则，也不把路径/行号写进公开 API identity。seed 缺失、重复匹配、无法解析、闭包为空或出现无法归属的 client 声明都必须失败。

### 固定工具链、解析选项和 references

后续实现必须在执行前检查并记录以下精确值；任一值不匹配时只报告环境不满足，不生成 snapshot：

| 项目 | 固定值 |
| --- | --- |
| TFM | `net10.0`（由 `backend/Directory.Build.props` 提供） |
| .NET SDK | `10.0.302` |
| `Microsoft.NETCore.App.Ref` | `10.0.10` 的 `net10.0` reference pack |
| `Microsoft.CodeAnalysis.CSharp` / `Microsoft.CodeAnalysis` | NuGet `5.0.0`，版本由 `backend/Directory.Packages.props` 锁定 |
| 输出 | `CSharpCompilation`，`OutputKind.DynamicallyLinkedLibrary` |

实现必须使用显式而非默认的 Roslyn 选项：

- `CSharpParseOptions(LanguageVersion.CSharp14, DocumentationMode.Parse, SourceCodeKind.Regular, preprocessorSymbols: empty)`；不得使用 `Preview`、机器环境变量、`DEBUG`、`TRACE` 或调用方临时 symbols；
- `CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: Enable, deterministic: true, concurrentBuild: false)`；nullable annotation 在 v1 的 type display 中按下文规则省略，但编译仍必须以 `Enable` 检查；
- 每个 syntax tree 只能由该 compilation 创建一个 `SemanticModel`；symbol、base/interface 和成员关系必须来自这个 model/compilation，不得回退到文本或反射；
- references 只来自固定 `net10.0` reference pack，以及 `Nerv.IIP.BusinessGateway.Web.csproj` 在该 SDK、TFM 和 restore graph 下的 compile-time closure：`project.assets.json` 的 `net10.0/compile` assets、显式 `ProjectReference` 的编译输出和 central-version package assets；不得加入 runtime-only assets。不得使用当前进程的 `TRUSTED_PLATFORM_ASSEMBLIES`、运行时加载程序集、机器上任意版本的 DLL 或 provider 的运行时 reflection 结果；
- reference 清单必须按规范化绝对来源身份排序，并在运行记录包版本、文件名和 SHA-256。缺少、重复、版本不符或无法读取 reference 都是 fatal error；
- source bytes、parse options、compilation options、reference 清单和 Roslyn assembly version 必须作为运行诊断打印；这些诊断不属于 snapshot 内容。

### 诊断与覆盖的 fail-closed 规则

编译完成后必须收集全部 syntax、declaration、compilation diagnostics，按 `path, span-start, diagnostic-id, severity, message` 的 UTF-8 字节序排序。任何 `Error` 或 `Warning`（包括缺 reference、预处理分支、nullable、未解析 symbol 和 hidden/ informational 之外的编译问题）都使本次运行失败；`Info`/`Hidden` 只能记录，不能改变输出。诊断非空时不得降级为“尽力生成”或写部分文件。

在收集 symbol 前必须证明：

- 递归枚举结果非空，文件路径集合与加入 compilation 的 syntax tree 路径集合完全相等，数量相等且无重复；
- 每个 syntax tree 都成功使用固定 parse options 解析；所有活动的 type/member declaration 都能得到非空 declared symbol，partial declaration 共享同一 symbol 是允许且必须计数的闭包关系；
- B seed 集合和语义闭包中的每个 symbol 都能由 compilation 唯一解析，且同一 canonical identity 不得对应多个不等价 symbol；
- 受管 source file、声明、partial 位置和输出记录数量被计数并校验；任何文件遗漏、重复加入、不可达声明、无法绑定的 base/interface 或空的闭包都失败；
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

记录中的 `TAB`、LF、CR、`=`、`%` 以及所有字段值字符均不得作为未定义的转义语法使用。记录解析器必须在第一枚 TAB 处分隔记录类型，在每个字段的第一枚 `=` 处分隔键和值；未知记录类型、未知/重复/缺失字段、额外 TAB、非法 percent escape、重复 record identity 或重复完整记录都必须拒绝。

### Percent-encoding

字段值保留 Roslyn 返回的 Unicode scalar sequence，不做 NFC/NFKC、locale 或大小写转换，再按 UTF-8 bytes 编码。唯一允许原样保留的字节集合是 ASCII `A-Z`、`a-z`、`0-9`、`.`、`_`、`~`；特别地，`-` 不在安全集合中，以便空值哨兵不产生歧义。其他每个 byte 必须写成 `%HH`，`H` 为大写十六进制。因而空格为 `%20`、TAB 为 `%09`、LF 为 `%0A`、`%` 为 `%25`、`:` 为 `%3A`、`;` 为 `%3B`、`<` 为 `%3C`、`>` 为 `%3E`、反引号为 `%60`，Unicode 非 ASCII bytes 也逐 byte percent-encode。

在字段的未编码规范化值中，标量不存在、空集合和空 accessor 槽位使用 `-`；整个字段随后统一 percent-encode，所以结构化槽位的哨兵输出为 `%2D`，只有整字段不存在才输出裸 `-`。真实值 `-` 同样编码为 `%2D`。集合先按未编码 canonical item 的 UTF-8 bytes 排序，以 `;` 连接，再对整个连接字符串执行上述 percent-encoding；因此记录中不会出现裸 `;`，也不能依赖 split 后的机器行为。解析必须严格 decode 一次并拒绝畸形 `%`、非大写 hex 或 decode 后再次编码不相同的值。

### 确定性分组与排序

输出顺序完全固定：首行 header；其后先输出全部 `type` records，再输出全部 `member` records；两组之间不得交错。每条 record 完成字段规范化和 percent-encoding 后，以“不含结尾 LF 的完整行”的 UTF-8 bytes 做无 locale、无大小写折叠的字节升序排序；排序相等即为重复记录并失败。该规则同时固定了 canonical type 总排序、type/member 分组顺序和 member 顺序，不依赖 source file、partial 片段、声明访问顺序或并发调度。

示例仅说明编码和分组，不是生产 baseline：

```text
api-surface-version=1
type	identity=global%3A%3ANerv.IIP.BusinessGateway.Web.Application.BusinessServices%3A%3AIBusinessInventoryClient%600	kind=interface	arity=0	access=public	modifiers=-	base=-	interfaces=-
member	owner=global%3A%3ANerv.IIP.BusinessGateway.Web.Application.BusinessServices%3A%3AIBusinessInventoryClient%600	kind=method	name=ListAsync	arity=0	access=public	modifiers=-	return=global%3A%3ASystem.Threading.Tasks.Task%3Cglobal%3A%3ANerv.IIP.BusinessGateway.Web.Application.BusinessServices.InventoryPage%3E	parameters=global%3A%3ASystem.String%3AinternalBearerToken%3Avalue%3Bglobal%3A%3ASystem.Threading.CancellationToken%3AcancellationToken%3Avalue	accessors=get%3A%2D%3Bset%3A%2D%3Binit%3A%2D
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

只记录受管 type **显式声明**的 `public`、`protected`、`protected-internal`、`private-protected` 成员；`internal`、`private` 成员不生成 member record，但其 body 变化仍不能使受管 type 的公开记录缺失。继承成员通过 type 的 base/interface identity 组合，不重复展开。

`kind` 词汇固定为 `constructor`、`method`、`property`、`indexer`、`event`、`field`、`operator`、`conversion`。constructor 的 `name` 为 `.ctor`；indexer 的 name 为 metadata name `Item`；operator/conversion 使用 Roslyn metadata name（例如 `op_Addition`、`op_Implicit`），普通 method/property/event/field 使用 symbol name。static constructor、compiler-generated member 和无法归入上述 kind 的受管成员必须被排除或报告 unsupported，而不能悄悄改名。

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

合同审阅通过后，后续 baseline PR 才能生成 snapshot。baseline 必须从本合同与固定 source tree 逐项推导，人工审阅每一处差异；不能运行当前实现后批量刷新并把全量输出当作预期。后续治理至少要保留这些可编译 mutation，并对每个 mutation 先证明旧规则 Red、再证明修复规则 Green：

- `class` ↔ `record-class`、`sealed` ↔ `unsealed`；
- interface default public method 的新增或删除；
- base/interface 的新增、删除或改变方向；
- namespace、name 或 generic arity 的变化；
- public/protected/protected-internal/private-protected member 的新增、删除、可见性或公开 modifier 变化；
- private implementation body 变化必须保持允许。

每个 mutation 需要断言 exact identity、差异维度和允许/拒绝原因。baseline/mutation PR 另行交付；本合同 PR 不落 baseline、mutation、生产 client、OpenAPI、生成客户端、数据库、UI、provider 或 FullChain 行为。
