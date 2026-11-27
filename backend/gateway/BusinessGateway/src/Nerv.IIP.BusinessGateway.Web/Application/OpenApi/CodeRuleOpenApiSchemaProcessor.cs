using Nerv.IIP.Contracts.Coding;
using NJsonSchema.Generation;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

internal sealed class CodeRuleOpenApiSchemaProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        if (context.ContextualType.Type == typeof(CodeRuleSegment))
        {
            // 保留 v1 客户端的可选 type。CLR required 继续约束运行时 DTO。
            context.Schema.RequiredProperties.Remove("type");
        }
        else if (context.ContextualType.Type == typeof(ScopeDimension))
        {
            // v1 以单个字符串传输范围，组合值也属于该字符串枚举。
            context.Schema.IsFlagEnumerable = false;
            context.Schema.EnumerationNames.Clear();
            context.Schema.Enumeration.Add("organization, environment");
            context.Schema.Enumeration.Add("organization, site");
            context.Schema.Enumeration.Add("environment, site");
            context.Schema.Enumeration.Add("organization, environment, site");
        }
    }
}
