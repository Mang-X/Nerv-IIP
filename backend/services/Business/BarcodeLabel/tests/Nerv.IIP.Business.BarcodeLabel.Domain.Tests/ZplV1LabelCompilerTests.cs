using System.Text;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.BarcodeLabel.Domain.Tests;

public sealed class ZplV1LabelCompilerTests
{
    [Theory]
    [MemberData(nameof(GoldenBarcodeCases))]
    public void Compile_emits_the_approved_zpl_v1_barcode_fragments(
        string barcodeType,
        LabelCompilationItem item,
        string commandFragment,
        string dataFragment)
    {
        var zpl = CompileText(barcodeType, item);

        Assert.Contains(commandFragment, zpl, StringComparison.Ordinal);
        Assert.Contains(dataFragment, zpl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gs1-128", ">8", "^FD>;>8010950110153000310123456>821789012^FS")]
    [InlineData("gs1-datamatrix", "_1", "^FD_1010950110153000310123456_121789012^FS")]
    public void Compile_places_exactly_the_start_and_separator_fnc1_escapes(
        string barcodeType,
        string fnc1Escape,
        string expectedDataFragment)
    {
        var gs1 = new Gs1BarcodeValue("09501101530003", "123456", "789012", null);

        var zpl = CompileText(barcodeType, Gs1Item(gs1));

        Assert.Contains(expectedDataFragment, zpl, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(zpl, fnc1Escape));
        Assert.DoesNotContain("(01)", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("(10)", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain("(21)", zpl, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001D', zpl);
    }

    [Fact]
    public void Compile_keeps_plain_barcode_types_free_of_fnc1_escapes()
    {
        var code128 = CompileText("code128", PlainItem("MAT-0001"));
        var dataMatrix = CompileText("datamatrix", PlainItem("DM-SN0001"));

        Assert.DoesNotContain(">8", code128, StringComparison.Ordinal);
        Assert.DoesNotContain("_1", dataMatrix, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_is_byte_for_byte_deterministic()
    {
        var item = PlainItem("MAT-0001");

        var first = Compile("code128", item);
        var second = Compile("code128", item);

        Assert.Equal(first.Payload.ToArray(), second.Payload.ToArray());
    }

    [Fact]
    public async Task Compile_is_independent_of_the_process_culture()
    {
        await using var globalState = await GlobalTestStateScope.CaptureAsync();
        var item = Gs1Item(new Gs1BarcodeValue("09501101530003", "LOT-001", "SN-001", 1.5m));
        globalState.UseCulture("en-US");
        var english = Compile("gs1-128", item);
        globalState.UseCulture("fr-FR");
        var french = Compile("gs1-128", item);

        Assert.Equal(english.Payload.ToArray(), french.Payload.ToArray());
    }

    [Fact]
    public void Compile_emits_media_and_text_layout_from_the_template()
    {
        var zpl = CompileText("code128", PlainItem("MAT-0001"));

        Assert.StartsWith("^XA^PW812^LL406", zpl, StringComparison.Ordinal);
        Assert.Contains("^FO10,20^A0N,30,30^FDSKU-001^FS", zpl, StringComparison.Ordinal);
        Assert.EndsWith("^XZ", zpl, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidTemplateCases))]
    public void Template_parser_rejects_non_contract_json(string templateJson)
    {
        Assert.Throws<ArgumentException>(() => LabelTemplateDocument.Parse(templateJson));
    }

    [Theory]
    [InlineData(203)]
    [InlineData(300)]
    [InlineData(600)]
    public void Template_parser_accepts_the_frozen_dpi_values(int dpi)
    {
        Assert.Equal(dpi, LabelTemplateDocument.Parse(Template(dpi: dpi)).Media.Dpi);
    }

    [Theory]
    [InlineData(202)]
    [InlineData(204)]
    [InlineData(301)]
    [InlineData(599)]
    [InlineData(601)]
    public void Template_parser_rejects_unapproved_dpi(int dpi)
    {
        Assert.Throws<ArgumentException>(() => LabelTemplateDocument.Parse(Template(dpi: dpi)));
    }

    [Theory]
    [InlineData("\"widthDots\":812", "\"widthDots\":0")]
    [InlineData("\"heightDots\":406", "\"heightDots\":32768")]
    [InlineData("\"x\":10", "\"x\":-1")]
    [InlineData("\"x\":10", "\"x\":32768")]
    [InlineData("\"fontHeight\":30", "\"fontHeight\":0")]
    [InlineData("\"fontWidth\":30", "\"fontWidth\":32768")]
    [InlineData("\"moduleWidth\":2", "\"moduleWidth\":0")]
    [InlineData("\"height\":100", "\"height\":32768")]
    public void Template_parser_rejects_out_of_range_layout_values(string target, string replacement)
    {
        var template = Template().Replace(target, replacement, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => LabelTemplateDocument.Parse(template));
    }

    [Fact]
    public void Template_parser_accepts_exactly_64_fields_and_rejects_65()
    {
        var sixtyThreeTextFields = string.Join(',', Enumerable.Range(0, 63).Select(index =>
            $$"""{"kind":"text","x":{{index}},"y":0,"fontHeight":1,"fontWidth":1,"variable":"skuCode"}"""));
        var sixtyFourTextFields = string.Join(',', Enumerable.Range(0, 64).Select(index =>
            $$"""{"kind":"text","x":{{index}},"y":0,"fontHeight":1,"fontWidth":1,"variable":"skuCode"}"""));

        Assert.Equal(64, LabelTemplateDocument.Parse(TemplateWithFields($"{sixtyThreeTextFields},{BarcodeField}")).Fields.Length);
        Assert.Throws<ArgumentException>(() => LabelTemplateDocument.Parse(TemplateWithFields($"{sixtyFourTextFields},{BarcodeField}")));
    }

    [Theory]
    [MemberData(nameof(InvalidSchemaCases))]
    public void Variable_schema_parser_rejects_non_contract_json(string schemaJson)
    {
        Assert.Throws<ArgumentException>(() => LabelVariableSchema.Parse(schemaJson));
    }

    [Fact]
    public void Variable_schema_defaults_required_and_max_length()
    {
        var schema = LabelVariableSchema.Parse(Schema("""{"name":"skuCode","label":"物料编码","type":"string"}"""));

        var variable = Assert.Single(schema.Variables);
        Assert.True(variable.Required);
        Assert.Equal(200, variable.MaxLength);
    }

    [Fact]
    public void Binder_requires_every_business_template_variable_to_be_declared()
    {
        var template = LabelTemplateDocument.Parse(Template(textVariable: "undeclared"));
        var schema = LabelVariableSchema.Parse(Schema());

        Assert.Throws<ArgumentException>(() => LabelTemplateBinder.BindBatch(template, schema, [PlainItem("MAT-0001")]));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"skuCode\":null}")]
    [InlineData("{\"skuCode\":42}")]
    [InlineData("{\"skuCode\":{\"value\":\"SKU-001\"}}")]
    [InlineData("{\"skuCode\":[\"SKU-001\"]}")]
    [InlineData("{\"skuCode\":\"SKU-001\",\"extra\":\"value\"}")]
    [InlineData("{\"skuCode\":\"SKU-001\",\"label.value\":\"override\"}")]
    [InlineData("{\"skuCode\":\"SKU-001\",\"skuCode\":\"SKU-002\"}")]
    public void Binder_rejects_missing_invalid_extra_duplicate_or_reserved_business_values(string valuesJson)
    {
        var template = LabelTemplateDocument.Parse(Template());
        var schema = LabelVariableSchema.Parse(Schema());
        var item = PlainItem("MAT-0001") with { VariableValuesJson = valuesJson };

        Assert.Throws<ArgumentException>(() => LabelTemplateBinder.BindBatch(template, schema, [item]));
    }

    [Fact]
    public void Binder_enforces_max_length_and_allows_missing_optional_values()
    {
        var template = LabelTemplateDocument.Parse(Template());
        var requiredSchema = LabelVariableSchema.Parse(Schema("""{"name":"skuCode","type":"string","maxLength":3}"""));
        var optionalSchema = LabelVariableSchema.Parse(Schema("""{"name":"skuCode","type":"string","required":false,"maxLength":3}"""));

        Assert.Throws<ArgumentException>(() => LabelTemplateBinder.BindBatch(
            template,
            requiredSchema,
            [PlainItem("MAT-0001") with { VariableValuesJson = "{\"skuCode\":\"LONG\"}" }]));

        var bound = LabelTemplateBinder.BindBatch(
            template,
            optionalSchema,
            [PlainItem("MAT-0001") with { VariableValuesJson = "{}" }]);
        Assert.Equal(string.Empty, Assert.Single(bound).Fields[0].Value);
    }

    [Theory]
    [InlineData("{\"skuCode\":\"bad^XZ\"}", "MAT-0001")]
    [InlineData("{\"skuCode\":\"bad~JA\"}", "MAT-0001")]
    [InlineData("{\"skuCode\":\"bad\\u000Avalue\"}", "MAT-0001")]
    [InlineData("{\"skuCode\":\"SKU-001\"}", "^XZ~JA^XA")]
    [InlineData("{\"skuCode\":\"SKU-001\"}", "bad\u0001value")]
    public void Binder_rejects_text_or_barcode_instruction_injection(string valuesJson, string labelValue)
    {
        var template = LabelTemplateDocument.Parse(Template());
        var schema = LabelVariableSchema.Parse(Schema());
        var item = PlainItem(labelValue) with { VariableValuesJson = valuesJson };

        Assert.Throws<ArgumentException>(() => LabelTemplateBinder.BindBatch(template, schema, [item]));
    }

    [Theory]
    [InlineData("MAT>8INJECT")]
    [InlineData("MAT>;INJECT")]
    [InlineData("MAT>:INJECT")]
    public void Compiler_rejects_code128_control_sequences_in_barcode_data(string labelValue)
    {
        Assert.Throws<ArgumentException>(() => Compile("code128", PlainItem(labelValue)));
    }

    [Theory]
    [InlineData("LOT>8INJECT", "SN-001")]
    [InlineData("LOT-001", "SN>;INJECT")]
    [InlineData("LOT>:INJECT", "SN-001")]
    public void Compiler_rejects_gs1_128_control_sequences_in_application_identifier_data(string lotNo, string serialNumber)
    {
        var gs1 = new Gs1BarcodeValue("09501101530003", lotNo, serialNumber, null);

        Assert.Throws<ArgumentException>(() => Compile("gs1-128", Gs1Item(gs1)));
    }

    [Fact]
    public void Compiler_hex_escapes_literal_underscores_in_gs1_datamatrix_data()
    {
        var gs1 = new Gs1BarcodeValue("09501101530003", "LOT_A", "SN_001", null);

        var zpl = CompileText("gs1-datamatrix", Gs1Item(gs1));

        Assert.Contains("^FD_1010950110153000310LOT_5FA_121SN_5F001^FS", zpl, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(zpl, "_1"));
    }

    [Theory]
    [InlineData("123", null)]
    [InlineData("09501101530003", "123")]
    public void Compiler_rejects_invalid_fixed_length_gs1_data(string gtin, string? sscc)
    {
        var gs1 = new Gs1BarcodeValue(gtin, "LOT-A", "SN-001", null, Sscc: sscc);

        Assert.Throws<ArgumentException>(() => Compile("gs1-128", Gs1Item(gs1)));
    }

    [Fact]
    public void Compiler_rejects_an_unknown_barcode_type()
    {
        var template = LabelTemplateDocument.Parse(Template());
        var schema = LabelVariableSchema.Parse(Schema());

        Assert.Throws<ArgumentException>(() => ZplV1LabelCompiler.CompileBatch(
            template,
            schema,
            "pdf417",
            [PlainItem("MAT-0001")]));
    }

    [Fact]
    public void Compiler_rejects_a_document_larger_than_the_zpl_v1_payload_limit()
    {
        var template = LabelTemplateDocument.Parse(Template());
        var schema = LabelVariableSchema.Parse(Schema("""{"name":"skuCode","type":"string","maxLength":262144}"""));
        var item = PlainItem("MAT-0001") with
        {
            VariableValuesJson = $$"""{"skuCode":"{{new string('A', 262144)}}"}""",
        };

        Assert.Throws<ArgumentException>(() => ZplV1LabelCompiler.CompileBatch(template, schema, "code128", [item]));
    }

    public static TheoryData<string, LabelCompilationItem, string, string> GoldenBarcodeCases()
    {
        var gs1 = new Gs1BarcodeValue("09501101530003", "123456", "789012", null);
        return new TheoryData<string, LabelCompilationItem, string, string>
        {
            { "code128", PlainItem("MAT-0001"), "^BCN,100,Y,N,N,A", "^FD>:MAT-0001^FS" },
            { "gs1-128", Gs1Item(gs1), "^BCN,100,Y,N,N,A", "^FD>;>8010950110153000310123456>821789012^FS" },
            { "qr", PlainItem("https://example.invalid/items/SN0001"), "^BQ,2,6,Q,7", "^FDQA,https://example.invalid/items/SN0001^FS" },
            { "datamatrix", PlainItem("DM-SN0001"), "^BXN,6,200", "^FDDM-SN0001^FS" },
            { "gs1-datamatrix", Gs1Item(gs1), "^BXN,6,200", "^FD_1010950110153000310123456_121789012^FS" },
        };
    }

    public static TheoryData<string> InvalidTemplateCases() => new()
    {
        Template().Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal),
        Template().Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal),
        Template().Replace("\"format\":\"nerv-iip.label-template\"", "\"format\":\"other\"", StringComparison.Ordinal),
        Template().Replace("\"media\":", "\"rawZpl\":\"^XA^XZ\",\"media\":", StringComparison.Ordinal),
        Template().Replace("\"dpi\":203", "\"dpi\":203,\"unknown\":1", StringComparison.Ordinal),
        Template().Replace("\"kind\":\"text\"", "\"kind\":\"image\"", StringComparison.Ordinal),
        Template().Replace("\"variable\":\"skuCode\"", "\"variable\":\"skuCode\",\"variable\":\"skuCode\"", StringComparison.Ordinal),
        Template().Replace($",{BarcodeField}", string.Empty, StringComparison.Ordinal),
        Template().Replace("\"kind\":\"text\"", "\"kind\":\"barcode\"", StringComparison.Ordinal)
            .Replace("\"fontHeight\":30,\"fontWidth\":30", "\"moduleWidth\":2,\"height\":100", StringComparison.Ordinal),
    };

    public static TheoryData<string> InvalidSchemaCases() => new()
    {
        Schema().Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal),
        Schema().Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal),
        Schema().Replace("\"variables\":", "\"unknown\":true,\"variables\":", StringComparison.Ordinal),
        Schema("""{"name":"skuCode","type":"integer"}"""),
        Schema("""{"name":"skuCode","name":"skuCode","type":"string"}"""),
        Schema("""{"name":"skuCode","type":"string","unknown":true}"""),
        Schema("""{"name":"skuCode","type":"string"},{"name":"skuCode","type":"string"}"""),
        Schema("""{"name":"label.value","type":"string"}"""),
        Schema("""{"name":"skuCode","type":"string","maxLength":0}"""),
    };

    private static CompiledLabelDocument Compile(string barcodeType, LabelCompilationItem item)
    {
        var template = LabelTemplateDocument.Parse(Template());
        var schema = LabelVariableSchema.Parse(Schema());
        return Assert.Single(ZplV1LabelCompiler.CompileBatch(template, schema, barcodeType, [item]));
    }

    private static string CompileText(string barcodeType, LabelCompilationItem item) =>
        Encoding.UTF8.GetString(Compile(barcodeType, item).Payload.Span);

    private static LabelCompilationItem PlainItem(string labelValue) =>
        new("{\"skuCode\":\"SKU-001\"}", new LabelReservedVariables(labelValue, null, 1, "DOC-001"));

    private static LabelCompilationItem Gs1Item(Gs1BarcodeValue gs1) =>
        new("{\"skuCode\":\"SKU-001\"}", new LabelReservedVariables(gs1.ToAiString(), gs1, 1, "DOC-001"));

    private static string Schema(string? variables = null)
    {
        variables ??= """{"name":"skuCode","label":"物料编码","type":"string"}""";
        return $$"""{"version":1,"variables":[{{variables}}]}""";
    }

    private static string Template(int dpi = 203, string textVariable = "skuCode") =>
        TemplateWithFields($$"""{"kind":"text","x":10,"y":20,"fontHeight":30,"fontWidth":30,"variable":"{{textVariable}}"},{{BarcodeField}}""", dpi);

    private static string TemplateWithFields(string fields, int dpi = 203) => $$"""
        {"format":"nerv-iip.label-template","version":1,"media":{"dpi":{{dpi}},"widthDots":812,"heightDots":406},"fields":[{{fields}}]}
        """;

    private const string BarcodeField = """{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}""";

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;
}
