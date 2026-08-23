using System.Reflection;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Domain.Tests;

public sealed class QualityVocabularyContractTests
{
    [Fact]
    public void Quality_vocabulary_matches_the_1892_reference_data_and_public_contract()
    {
        var expectedInspectionDispositionStatuses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Passed"] = "passed",
            ["ConditionalRelease"] = "conditional-release",
            ["Rejected"] = "rejected",
        };

        Assert.Equal(
            expectedInspectionDispositionStatuses,
            PublicStringConstantsOf(typeof(QualityInspectionDispositionStatuses)));
    }

    private static IReadOnlyDictionary<string, string> PublicStringConstantsOf(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);
    }
}
