using System.Reflection;
using Nerv.IIP.FileStorage.Web.Application.Files;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageServiceContractTests
{
    [Fact]
    public void BusinessMethods_AreAsyncAndAcceptCancellationToken()
    {
        var businessMethods = typeof(IFileStorageService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(method => method.Name)
            .ToList();

        Assert.NotEmpty(businessMethods);
        foreach (var method in businessMethods)
        {
            Assert.True(
                method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>),
                $"{method.Name} should return Task<T>.");
            Assert.Equal(typeof(CancellationToken), method.GetParameters().LastOrDefault()?.ParameterType);
        }
    }
}
