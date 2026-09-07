namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// Temporary #3213 canary. Not merged: this file exists only on the experiment branch so a real CI
/// run produces red records whose retained failure text can be read back from the uploaded evidence
/// artifact.
/// </summary>
/// <remarks>
/// Round 2. The round-1 sentinels were all space-bearing, hyphen-bearing or `password=`-matching
/// shapes — none of them was a class the token alphabet admits, so the end-to-end evidence had no
/// power to falsify the admission rules at all. Every sentinel below is now drawn from a class the
/// alphabet either admits or must refuse by magnitude: bare digit strings, a GUID, an uppercase
/// dotted name, and a `key=digits` pair.
/// </remarks>
public sealed class Issue3213RetentionCanaryTests
{
    [Fact]
    public void Issue3213_alphabet_class_payload_must_not_reach_the_retained_evidence()
        => Assert.Equal(
            "13800000000 622202123456789012 110101199003078888",
            "3f2504e0-4f89-11d3-9a0c-0305e82c3301 Zhang.Wei salary=250000 20260907");

    [Fact]
    public void Issue3213_alphabet_class_payload_in_a_type_name_must_not_reach_the_retained_evidence()
        => throw new Customer110101199003078888Error();

    [Fact]
    public void Issue3213_diagnostic_canary_must_reach_the_retained_evidence()
        => throw new EventuallyTimeoutException(
            "nerv3213.canary.condition",
            7,
            TimeSpan.FromSeconds(30.0021),
            "assertion still failing: EqualException: Assert.Equal() Failure: Values differ "
                + "Expected: 1 Actual: 0 arrivals=0 cap.published=3");
}

#pragma warning disable CA1064, RCS1194
public sealed class Customer110101199003078888Error : Exception;
#pragma warning restore CA1064, RCS1194
