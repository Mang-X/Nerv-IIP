using System.Reflection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nerv.IIP.Business.MasterData.Web.Extensions;

public static class SwaggerGenOptionsExtensions
{
    public static SwaggerGenOptions AddEntityIdSchemaMap(this SwaggerGenOptions swaggerGenOptions)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(p => p.FullName != null && p.FullName.Contains("Nerv.IIP.Business.MasterData")))
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsClass && Array.Exists(type.GetInterfaces(), p => p == typeof(IEntityId)))
                {
                    swaggerGenOptions.MapType(type,
                        () => new OpenApiSchema { Type = typeof(string).Name.ToLower() });
                }
            }
        }

        return swaggerGenOptions;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
