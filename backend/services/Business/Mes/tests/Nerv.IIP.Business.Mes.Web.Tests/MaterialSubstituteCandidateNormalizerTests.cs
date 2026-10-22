using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MaterialSubstituteCandidateNormalizerTests
{
    // Contract: DomainInvariant + Regression. Authority: Issue #2222 acceptance 2; every producer and aggregate uses one canonical candidate rule.
    [Fact]
    public void Normalize_removes_blank_self_and_case_insensitive_duplicates_then_sorts_stably()
    {
        var normalized = MaterialSubstituteCandidateNormalizer.Normalize(
            "MAT-PRIMARY",
            [" MAT-ALT-B ", "mat-primary", "", "mat-alt-a", "MAT-ALT-B"]);

        Assert.Equal(["mat-alt-a", "MAT-ALT-B"], normalized);
    }
}
