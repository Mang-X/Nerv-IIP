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

    /// <summary>
    /// #2779 首件确认读契约的取值是 MES 报工门禁要判读的线上字面量，改一个字符门禁就选错分支；
    /// 本用例把字面量钉在契约上，并保证新增取值必须显式登记。
    /// </summary>
    [Fact]
    public void First_article_confirmation_statuses_pin_the_2779_wire_values()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NotRequired"] = "not-required",
            ["NotOpened"] = "not-opened",
            ["Pending"] = "pending",
            ["Decided"] = "decided",
            ["NotSynchronized"] = "not-synchronized",
        };

        Assert.Equal(
            expected,
            PublicStringConstantsOf(typeof(QualityFirstArticleConfirmationStatuses)));
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
