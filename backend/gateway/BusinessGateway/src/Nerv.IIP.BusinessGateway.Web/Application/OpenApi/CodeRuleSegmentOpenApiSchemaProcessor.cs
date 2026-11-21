using Nerv.IIP.Contracts.Coding;
using NJsonSchema.Generation;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

internal sealed class CodeRuleSegmentOpenApiSchemaProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        if (context.ContextualType.Type == typeof(CodeRuleSegment))
        {
            // 保留 v1 客户端的可选 type。CLR required 继续约束运行时 DTO。
            context.Schema.RequiredProperties.Remove("type");
        }
    }
}
