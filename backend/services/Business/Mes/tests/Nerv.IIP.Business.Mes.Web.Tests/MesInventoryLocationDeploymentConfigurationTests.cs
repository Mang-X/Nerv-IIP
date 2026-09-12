using System.Text.RegularExpressions;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 线边收料/完工入库的站点与库位部署面契约（#2008 / #3137）。服务读的 <c>Inventory:*</c> 配置键与
/// AppHost 下发的 <c>Inventory__*</c> 环境变量键过去只靠人眼对齐，改名的唯一表现是运行时
/// KnownException，没有任何测试转红。这里把 AppHost 下发的键约束为服务真的会读的键，并钉住
/// 环境门控与 profile 分叉：站点/库位只允许在 Development 回落；Development 内部再分两支——
/// leader-demo 回落 <c>WH-WB-*</c>（Inventory 世界观种子真的建出这些库位行），
/// 普通 Development 回落 <c>loc-*</c>（MasterData <c>inventory-location</c> 码表候选码，#2058 裁决）。
/// </summary>
public sealed class MesInventoryLocationDeploymentConfigurationTests
{
    private const string AppHostProgramPath = "infra/aspire/Nerv.IIP.AppHost/Program.cs";
    private const string InventoryWorldHistoryPhase2SpecPath =
        "backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/WorldHistoryPhase2Spec.cs";
    private const string MasterDataDictionaryRulesPath =
        "backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/MasterDataDictionaryRules.cs";
    /// <summary>AppHost 里区分两个 Development profile 的门控变量名。</summary>
    private const string LeaderDemoHistoryGate = "leaderDemoHistoryEnabled";
    private const string MesProgramPath =
        "backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs";

    /// <summary>门控覆盖的仓储位置键；<c>BaseUrl</c> 是服务端点，不在门控范围内。</summary>
    private static readonly string[] GatedLocationKeys =
    [
        "FinishedGoodsLocationCode",
        "LineSideLocationCode",
        "SiteCode",
        "SourceLocationCodes"
    ];

    [Fact]
    public void AppHost_only_injects_inventory_keys_the_service_actually_reads()
    {
        var section = InventoryConfigurationSection();
        var program = ReadRepositoryFile(MesProgramPath);

        // 服务侧可读键有两个来源：绑定到 options 类型的属性，以及 Program.cs 里直接读的配置路径。
        var boundKeys = typeof(MesMaterialSupplyLocationOptions)
            .GetProperties()
            .Select(property => property.Name);
        var directlyReadKeys = Regex
            .Matches(program, $@"""{Regex.Escape(section)}:(?<name>[A-Za-z0-9]+)""")
            .Select(match => match.Groups["name"].Value);
        var readableKeys = boundKeys
            .Concat(directlyReadKeys)
            .ToHashSet(StringComparer.Ordinal);

        var injectedKeys = InjectedKeyNames(section);

        Assert.NotEmpty(injectedKeys);
        Assert.Empty(injectedKeys.Except(readableKeys, StringComparer.Ordinal));
    }

    [Fact]
    public void AppHost_injects_every_gated_warehouse_location_key()
    {
        var injectedKeys = InjectedKeyNames(InventoryConfigurationSection());

        Assert.Empty(GatedLocationKeys.Except(injectedKeys, StringComparer.Ordinal));
    }

    [Fact]
    public void AppHost_reads_its_override_under_the_same_configuration_path_it_injects()
    {
        var section = InventoryConfigurationSection();
        var region = AppHostMesRegion();

        var overrideKeys = Regex
            .Matches(region, $@"""{Regex.Escape(section)}:(?<name>[A-Za-z0-9]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // 每个受门控的库位键都必须在同名配置路径下提供部署方覆盖入口。
        Assert.Empty(GatedLocationKeys.Except(overrideKeys, StringComparer.Ordinal));
    }

    [Fact]
    public void AppHost_confines_every_location_literal_to_the_gated_helpers()
    {
        // 围栏而不是形态匹配：只禁某一个位置码前缀的双字面量形态时，把受治理位置值经
        // const/局部变量转手就能绕过。这里要求**每一个**位置字面量都出现在同一条语句内的
        // DeploymentWarehouseLocation(s) 调用里，转手一次就落到调用之外，立刻转红。
        var appHost = ReadRepositoryFile(AppHostProgramPath);
        var literals = DemandLocationLiterals(appHost);

        Assert.NotEmpty(literals);
        foreach (var literal in literals)
        {
            Assert.True(
                literal.IsGated,
                $"受治理站点/库位字面量 {literal.Value} 未经 DeploymentWarehouseLocation(s) 门控下发。");
        }
    }

    /// <summary>
    /// 收窄（不是删除）原来的「AppHost 不得出现 <c>WH-WB-*</c> 字面量」禁令（#2058 → #3137）。
    ///
    /// 原禁令的真不变量是「世界观演示库位不得泄漏到普通 Development / 生产回落面」，这一条仍然成立；
    /// 失效的只是它的实现方式——它禁的是**整份文件**，连「只在 leader-demo 分支下发」也一并禁掉了，
    /// 而 leader-demo 恰恰是唯一会把这七个库位建成行的 profile（Inventory
    /// <c>WorldHistorySeedService</c> 只在 <c>LeaderDemo:Seed:Enabled</c> ∧
    /// <c>LeaderDemo:History:Enabled</c> 同时为真时运行）。所以这里把禁令收窄为：
    /// <c>WH-WB-*</c> 只允许出现在 <c>leaderDemoHistoryEnabled</c> 门控的那一支里。
    ///
    /// 门控变量的**推导式**一并钉住：只钉名字的话，把它改成 <c>true</c> 就恰好是本门禁要拦的错误
    /// （普通 Development / 生产也回落演示库位），却一条测试都不会红。
    /// </summary>
    [Fact]
    public void AppHost_confines_world_bible_location_literals_to_the_leader_demo_branch()
    {
        var appHost = ReadRepositoryFile(AppHostProgramPath);
        var fallbacks = DeploymentFallbackLiterals(appHost);

        // fail-closed：分叉整支被删掉、或 WH-WB-* 被挪出受门控实参，这里立刻抽空转红。
        Assert.Contains(fallbacks.LeaderDemo, code => code.StartsWith("WH-WB-", StringComparison.Ordinal));
        Assert.DoesNotContain(fallbacks.Plain, code => code.StartsWith("WH-WB-", StringComparison.Ordinal));

        Assert.Contains(
            "var leaderDemoHistoryEnabled = leaderDemoWorldEnabled && !string.Equals( " +
            "Environment.GetEnvironmentVariable(\"NERV_IIP_LEADER_DEMO_HISTORY\"), \"false\", " +
            "StringComparison.OrdinalIgnoreCase);",
            CollapseWhitespace(appHost),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// leader-demo 分支的回落库位码必须是 Inventory 种子**真的会建出行来**的库位（#3137）。
    ///
    /// 这一条是本票的核心：<c>loc-*</c> 满足「已在 MasterData 码表登记」却在库存里一个都不存在，
    /// 因为 MasterData 只写 <c>ReferenceDataCodes</c> 行，运行时没有任何路径按该码表创建
    /// Inventory 的 <c>StockLocation</c>（<c>StockLocation.CreateOrUpdate</c> 在 src 下只有两个调用点：
    /// <c>WorldHistorySeedService</c> 与 <c>CreateStockLocationCommand</c> 这个 HTTP 端点）。
    /// 因此 leader-demo 这一支要对着**种子集合**核，而不是对着码表核。
    ///
    /// 覆盖边界（声明多少就断言多少）：本条只覆盖**经 <c>DeploymentWarehouseLocation(s)</c> 下发的
    /// 回落实参字面量**。实参形状受 <see cref="DeploymentFallbackLiterals"/> fail-closed 约束，
    /// 因此「换一个未登记的新前缀」「把值经 const/变量转手」都会转红，而不是被前缀白名单静默放行。
    /// ⚠️ 不覆盖：部署方通过配置键显式覆盖的值（那是部署面事实，仓库里无从校验）。
    /// </summary>
    [Fact]
    public void AppHost_leader_demo_location_fallbacks_are_all_seeded_as_inventory_stock_locations()
    {
        var leaderDemoCodes = LocationCodesOnly(DeploymentFallbackLiterals(ReadRepositoryFile(AppHostProgramPath)).LeaderDemo);
        var seededCodes = InventorySeededStockLocationCodes();

        Assert.NotEmpty(leaderDemoCodes);
        Assert.NotEmpty(seededCodes);
        Assert.True(
            leaderDemoCodes.IsSubsetOf(seededCodes),
            "leader-demo 回落库位码在 Inventory 种子里不存在（线边收料/领料会恒定失效）：" +
            string.Join(", ", leaderDemoCodes.Except(seededCodes, StringComparer.Ordinal).Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// 普通 Development 分支仍然只回落 MasterData <c>inventory-location</c> 码表里登记过的候选码。
    ///
    /// 这条是 #2058 原断言的原样保留：它管的是「普通 Development 的回落码是受治理的登记码」，
    /// 该 profile 下 Inventory 不种任何库位行（<c>loc-*</c> 与 <c>WH-WB-*</c> 都不存在），在手量须经
    /// 真实流程建立——owner 在 #2058 已裁决走文档明示，本票不推翻它，只在它没管过的 leader-demo
    /// profile 上修 p1。
    /// </summary>
    [Fact]
    public void AppHost_product_location_fallbacks_are_present_in_master_data_dictionary()
    {
        var fallbackCodes = LocationCodesOnly(DeploymentFallbackLiterals(ReadRepositoryFile(AppHostProgramPath)).Plain);
        var dictionaryCodes = Regex
            .Matches(
                ReadRepositoryFile(MasterDataDictionaryRulesPath),
                @"new\(""inventory-location"",\s*""(?<code>[^""]+)""")
            .Select(match => match.Groups["code"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(fallbackCodes);
        Assert.NotEmpty(dictionaryCodes);
        Assert.True(
            fallbackCodes.IsSubsetOf(dictionaryCodes),
            $"AppHost fallback 库位码未在 MasterData inventory-location 字典中：{string.Join(", ", fallbackCodes.Except(dictionaryCodes, StringComparer.Ordinal).Order(StringComparer.Ordinal))}");
    }

    /// <summary>站点码不是库位码，库位行目录里不含它；两个目录比对都要先把它排除。</summary>
    private static HashSet<string> LocationCodesOnly(IEnumerable<string> codes) =>
        codes.Where(code => !code.StartsWith("SITE-", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Inventory 种子会写出 <c>StockLocation</c> 行的全部库位码。
    ///
    /// 提取**锚定在声明本身**而不是松散子串：早先用 <c>IndexOf("StockLocations =")</c> + 首个
    /// <c>"];"</c> 取范围，有两个失效方向——集合改名成 <c>SeededStockLocations</c> 时 <c>IndexOf</c>
    /// 命中子串照样放行；集合写法改成 <c>new List&lt;...&gt;{...}</c> 后右边界会漂到后续代码，把别处的
    /// 常量吸进来。这里改成整条声明的锚定匹配，两种改写都让 <c>match.Success</c> 为 false 而转红。
    /// 常量名抽空、常量名解析不出字面量，同样转红而不是放行。
    /// </summary>
    private static HashSet<string> InventorySeededStockLocationCodes()
    {
        var spec = ReadRepositoryFile(InventoryWorldHistoryPhase2SpecPath);
        var declaration = Regex.Match(
            spec,
            @"public static readonly IReadOnlyList<WorldHistoryStockLocation> StockLocations\s*=\s*\[(?<body>.*?)\];",
            RegexOptions.Singleline);
        Assert.True(declaration.Success, "Inventory 种子的 StockLocations 声明未按预期形状找到。");

        var constantNames = Regex
            .Matches(declaration.Groups["body"].Value, @"new\(\s*(?<name>[A-Za-z][A-Za-z0-9]*)\s*,")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(constantNames);

        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in constantNames)
        {
            var match = Regex.Match(spec, $@"const string {Regex.Escape(name)} = ""(?<value>[^""]+)"";");
            Assert.True(match.Success, $"Inventory 种子库位常量 {name} 未解析出字面量。");
            codes.Add(match.Groups["value"].Value);
        }

        return codes;
    }

    /// <summary>
    /// 按**调用点**（不是按前缀白名单）取出每个 <c>DeploymentWarehouseLocation(s)</c> 的 Development
    /// 回落实参字面量，并按 profile 分成两组。
    ///
    /// fail-closed 的落点是**实参形状**：回落实参必须是
    /// <c>leaderDemoHistoryEnabled ? &lt;demo&gt; : &lt;plain&gt;</c> 或单一的两 profile 同码表达式，
    /// 且两侧除字符串字面量、集合方括号、逗号、空白外不得含任何其它记号。于是
    /// 「换成白名单外的新前缀」（旧写法按 <c>(SITE-|WH-WB-|loc-)</c> 前缀匹配，新前缀根本不进集合而被
    /// 静默放行）与「把值经 const/局部变量/方法转手」都会在这里转红。
    /// </summary>
    internal static (HashSet<string> LeaderDemo, HashSet<string> Plain) DeploymentFallbackLiterals(string appHost)
    {
        var leaderDemo = new HashSet<string>(StringComparer.Ordinal);
        var plain = new HashSet<string>(StringComparer.Ordinal);
        var callSites = 0;

        foreach (Match call in Regex.Matches(appHost, @"(?<!\w)DeploymentWarehouseLocations?\("))
        {
            // 跳过两个局部函数的**声明**，只看调用点。
            var lineStart = appHost.LastIndexOf('\n', call.Index) + 1;
            var linePrefix = appHost[lineStart..call.Index];
            if (linePrefix.Contains("string?", StringComparison.Ordinal) ||
                linePrefix.Contains("IReadOnlyList<string>", StringComparison.Ordinal))
            {
                continue;
            }

            callSites++;
            var arguments = SplitTopLevelArguments(BalancedArgumentText(appHost, call.Index + call.Length - 1));
            Assert.Equal(2, arguments.Count);

            var fallback = arguments[1].Trim();
            var branch = Regex.Match(
                fallback,
                $@"^{Regex.Escape(LeaderDemoHistoryGate)}\s*\?(?<demo>.*?):(?<plain>.*)$",
                RegexOptions.Singleline);
            if (branch.Success)
            {
                leaderDemo.UnionWith(LiteralsOfFallbackArm(branch.Groups["demo"].Value));
                plain.UnionWith(LiteralsOfFallbackArm(branch.Groups["plain"].Value));
            }
            else
            {
                // 未分叉 = 两个 profile 同码（例如 SITE-001），两组都要算上。
                var shared = LiteralsOfFallbackArm(fallback);
                leaderDemo.UnionWith(shared);
                plain.UnionWith(shared);
            }
        }

        Assert.NotEqual(0, callSites);
        return (leaderDemo, plain);
    }

    /// <summary>
    /// 取回落实参一侧的全部字符串字面量，并要求这一侧**只由**字面量/集合方括号/逗号/空白构成。
    /// 这条形状约束就是 fail-closed 的落点：转手一次（<c>SomeConst</c>、<c>string.Concat(...)</c>）即红。
    /// </summary>
    private static IReadOnlyCollection<string> LiteralsOfFallbackArm(string arm)
    {
        var literals = Regex.Matches(arm, @"""(?<value>[^""]*)""")
            .Select(match => match.Groups["value"].Value)
            .ToArray();
        Assert.NotEmpty(literals);

        var residue = Regex.Replace(arm, @"""[^""]*""", string.Empty);
        Assert.True(
            Regex.IsMatch(residue, @"^[\s\[\],]*$"),
            $"受治理回落实参只允许字面量，不得转手：'{arm.Trim()}'。");
        return literals;
    }

    /// <summary>从左括号开始按括号/方括号配平取出实参文本（跳过字符串字面量内的括号）。</summary>
    private static string BalancedArgumentText(string text, int openParenIndex)
    {
        var depth = 0;
        var inString = false;
        for (var index = openParenIndex; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (current is '(' or '[')
            {
                depth++;
            }
            else if (current is ')' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return text[(openParenIndex + 1)..index];
                }
            }
        }

        Assert.Fail("受治理助手调用的括号未配平。");
        return string.Empty;
    }

    /// <summary>按顶层逗号切分实参（忽略字符串与嵌套括号内的逗号）。</summary>
    private static List<string> SplitTopLevelArguments(string argumentText)
    {
        var arguments = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
        for (var index = 0; index < argumentText.Length; index++)
        {
            var current = argumentText[index];
            if (current == '"')
            {
                inString = !inString;
            }
            else if (!inString && current is '(' or '[')
            {
                depth++;
            }
            else if (!inString && current is ')' or ']')
            {
                depth--;
            }
            else if (!inString && depth == 0 && current == ',')
            {
                arguments.Add(argumentText[start..index]);
                start = index + 1;
            }
        }

        arguments.Add(argumentText[start..]);
        return arguments;
    }

    private static string CollapseWhitespace(string text) =>
        Regex.Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), @"\s+", " ");

    [Fact]
    public void AppHost_gates_the_development_seed_fallback_on_a_pinned_environment_name()
    {
        // 门控判据的**值**必须一并钉住：只钉 `LocalDevelopmentEnvironment` 这个名字的话，把常量
        // 改成 "Production" 就恰好是本门禁要拦的错误（生产回落主线位置），却一条测试都不会红。
        var appHost = ReadRepositoryFile(AppHostProgramPath);

        Assert.Contains(
            "const string LocalDevelopmentEnvironment = \"Development\";",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "var localDevelopmentAppHost = string.Equals( builder.Environment.EnvironmentName, " +
            "LocalDevelopmentEnvironment, StringComparison.OrdinalIgnoreCase);",
            Regex.Replace(appHost.Replace("\r\n", "\n", StringComparison.Ordinal), @"\s+", " "),
            StringComparison.Ordinal);
        Assert.Contains("DeploymentWarehouseLocations(", AppHostMesRegion(), StringComparison.Ordinal);
    }

    [Fact]
    public void AppHost_gating_helpers_have_no_return_path_that_bypasses_the_environment_check()
    {
        // 钉 return 语句的**完整集合**而不是若干子串：子串断言对加性变异无感——在受门控 return
        // 之前插一句无条件早退，原来的子串全都还在，测试照绿。集合断言里多出一条 return 即红。
        // 语句内空白已归一，等价重排版不会假红。
        var appHost = ReadRepositoryFile(AppHostProgramPath);

        Assert.Equal(
            ["return configured.Trim();", "return localDevelopmentAppHost ? developmentSeedValue : null;"],
            ReturnStatements(appHost, "string? DeploymentWarehouseLocation(", "IReadOnlyList<string> DeploymentWarehouseLocations("));
        Assert.Equal(
            ["return indexed;", "return values;", "return localDevelopmentAppHost ? developmentSeedValues : [];"],
            ReturnStatements(appHost, "IReadOnlyList<string> DeploymentWarehouseLocations(", "> WithDeploymentEnvironment("));
        Assert.Equal(
            ["return string.IsNullOrWhiteSpace(value) ? project : project.WithEnvironment(name, value);"],
            ReturnStatements(appHost, "> WithDeploymentEnvironment(", "> WithRedisMessagingTransport("));
    }

    /// <summary>
    /// 取局部函数体内的全部 <c>return</c> 语句，语句内空白归一，保持源码顺序。
    /// </summary>
    private static string[] ReturnStatements(string appHost, string startMarker, string endMarker) =>
        Regex.Matches(TextBetween(appHost, startMarker, endMarker), @"return\s[^;]*;")
            .Select(match => Regex.Replace(match.Value, @"\s+", " "))
            .ToArray();

    [Fact]
    public void Legacy_compose_overlay_declares_the_line_side_supply_chain_unsupported()
    {
        var section = InventoryConfigurationSection();
        var mes = ComposeServiceBlock(
            ReadRepositoryFile("infra/compose/nerv-iip.platform.yml"),
            "business-mes");

        // 扫**整个 overlay 文件**而不是单个服务块：键塞进共享锚点 `&dotnet-env` 同样会到达
        // business-mes，只看服务块会漏。整个 legacy overlay 都不支持该链路，全文件扫描才是对的强度。
        // 只看真正的环境变量赋值行；注释里点名这些键正是「明确声明不支持」的载体。
        var overlay = ReadRepositoryFile("infra/compose/nerv-iip.platform.yml");
        foreach (var key in GatedLocationKeys)
        {
            Assert.DoesNotMatch($@"(?m)^\s*{Regex.Escape(section)}__{Regex.Escape(key)}(__[0-9]+)?:", overlay);
        }

        Assert.Contains("不支持线边收料", mes, StringComparison.Ordinal);
    }

    /// <summary>
    /// AppHost 源码里的全部受治理站点/库位字符串字面量，以及每个字面量是否落在同一条语句内的
    /// <c>DeploymentWarehouseLocation(s)</c> 调用中。注释里的 <c>SITE-001</c>/<c>WH-WB-*</c>/
    /// <c>loc-*</c> 不带引号，不会被计入。
    /// </summary>
    private static IReadOnlyList<(string Value, bool IsGated)> DemandLocationLiterals(string appHost) =>
        Regex.Matches(appHost, @"""(SITE-|WH-WB-|loc-)[^""]*""")
            .Select(match =>
            {
                var precedingText = appHost[..match.Index];
                var lastCall = precedingText.LastIndexOf("DeploymentWarehouseLocation", StringComparison.Ordinal);
                var lastStatementEnd = precedingText.LastIndexOfAny([';', '{', '}']);
                return (match.Value, lastCall > lastStatementEnd);
            })
            .ToArray();

    private static HashSet<string> InjectedKeyNames(string section)
    {
        // 索引形态（Inventory__SourceLocationCodes__0）与标量形态都归一到键名本身。
        return Regex
            .Matches(AppHostMesRegion(), $@"""{Regex.Escape(section)}__(?<name>[A-Za-z0-9]+)")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// 配置节名从 MES 的绑定语句读出来，而不是写死：改了 <c>GetSection</c> 的字符串这里立刻转红。
    /// </summary>
    private static string InventoryConfigurationSection()
    {
        var program = ReadRepositoryFile(MesProgramPath);
        var match = Regex.Match(
            program,
            $@"Configure<{nameof(MesMaterialSupplyLocationOptions)}>\(builder\.Configuration\.GetSection\(""(?<section>[A-Za-z0-9:]+)""\)\)");

        Assert.True(match.Success, $"{nameof(MesMaterialSupplyLocationOptions)} 的配置节绑定语句未找到。");
        return match.Groups["section"].Value;
    }

    private static string AppHostMesRegion() =>
        TextBetween(ReadRepositoryFile(AppHostProgramPath), "var businessMes =", "var businessDemandPlanning =");

    private static string ComposeServiceBlock(string yaml, string serviceName)
    {
        var match = Regex.Match(
            yaml.Replace("\r\n", "\n", StringComparison.Ordinal),
            $@"(?ms)^  {Regex.Escape(serviceName)}:\s*\n(?<body>.*?)(?=^  [a-z0-9][a-z0-9-]*:\s*$|\z)");

        Assert.True(match.Success, $"Compose service '{serviceName}' was not found.");
        return match.Value;
    }

    private static string TextBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Text range '{startMarker}' to '{endMarker}' was not found.");
        return text[start..end];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
