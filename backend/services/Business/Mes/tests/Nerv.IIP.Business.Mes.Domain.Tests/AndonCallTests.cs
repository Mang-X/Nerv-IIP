using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

// DomainInvariant: #3650 / #1946 已确认口径。去掉所有权、首次时间或重放约束会破坏这些行为。
public sealed class AndonCallTests
{
    private static readonly DateTimeOffset RaisedAt = DateTimeOffset.Parse("2026-09-20T01:00:00Z");

    [Theory]
    [InlineData(AndonCallCategory.MaterialShortage)]
    [InlineData(AndonCallCategory.Equipment)]
    [InlineData(AndonCallCategory.Quality)]
    [InlineData(AndonCallCategory.Process)]
    public void Claim_and_close_preserve_first_response_for_each_category(AndonCallCategory category)
    {
        var call = Create(category);
        Assert.Null(call.ResponseDuration);
        Assert.Throws<KnownException>(() => call.Close("worker", "close-1", RaisedAt));
        call.Claim("worker", "claim-1", RaisedAt.AddMinutes(2));
        call.Close("worker", "close-1", RaisedAt.AddMinutes(5));
        call.Claim("worker", "claim-1", RaisedAt.AddMinutes(8));
        call.Close("worker", "close-1", RaisedAt.AddMinutes(9));

        Assert.Equal(AndonCallStatus.Closed, call.Status);
        Assert.Equal(TimeSpan.FromMinutes(2), call.ResponseDuration);
        Assert.Equal(RaisedAt.AddMinutes(2), call.FirstRespondedAtUtc);
        Assert.Equal(RaisedAt.AddMinutes(5), call.ClosedAtUtc);
        Assert.Equal(category, call.Category);
        Assert.Equal("worker", call.ResponderId);
    }

    [Fact]
    public void Another_claimant_or_another_intent_cannot_replace_the_owner_or_close_the_call()
    {
        var call = Create();
        call.Claim("worker", "claim-1", RaisedAt.AddMinutes(2));
        Assert.Throws<KnownException>(() => call.Claim("other", "claim-1", RaisedAt.AddMinutes(3)));
        Assert.Throws<KnownException>(() => call.Claim("worker", "claim-2", RaisedAt.AddMinutes(3)));
        Assert.Throws<KnownException>(() => call.Close("other", "close-1", RaisedAt.AddMinutes(4)));
        call.Close("worker", "close-1", RaisedAt.AddMinutes(5));
        Assert.Throws<KnownException>(() => call.Close("worker", "close-2", RaisedAt.AddMinutes(6)));
        Assert.Equal("worker", call.ResponderId);
        Assert.Equal(RaisedAt.AddMinutes(2), call.FirstRespondedAtUtc);
    }

    [Fact]
    public void Escalation_is_once_only_without_changing_lifecycle_or_source_and_survives_close()
    {
        var call = Create();
        Assert.False(call.TryEscalate(RaisedAt.AddMinutes(4), TimeSpan.FromMinutes(5), "supervisor"));
        Assert.True(call.TryEscalate(RaisedAt.AddMinutes(5), TimeSpan.FromMinutes(5), "supervisor"));
        Assert.False(call.TryEscalate(RaisedAt.AddMinutes(6), TimeSpan.FromMinutes(5), "other"));
        Assert.Equal(AndonCallStatus.Open, call.Status);
        Assert.Null(call.ResponseDuration);
        call.Claim("worker", "claim-1", RaisedAt.AddMinutes(7));
        call.Close("worker", "close-1", RaisedAt.AddMinutes(9));
        Assert.Equal(RaisedAt.AddMinutes(5), call.EscalatedAtUtc);
        Assert.Equal("supervisor", call.EscalationRecipientId);
        Assert.Equal("WO-1", call.WorkOrderId);
        Assert.Equal("OP-1", call.OperationTaskIdValue);
        Assert.Equal("WC-1", call.WorkCenterId);
        Assert.Equal(TimeSpan.FromMinutes(7), call.ResponseDuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_responded_call_does_not_escalate(bool close)
    {
        var call = Create();
        call.Claim("worker", "claim-1", RaisedAt.AddMinutes(1));
        if (close) call.Close("worker", "close-1", RaisedAt.AddMinutes(2));
        Assert.False(call.TryEscalate(RaisedAt.AddHours(1), TimeSpan.FromMinutes(5), "supervisor"));
        Assert.Null(call.EscalatedAtUtc);
    }

    [Fact]
    public void First_response_and_close_cannot_precede_their_previous_fact()
    {
        var call = Create();
        Assert.Throws<KnownException>(() => call.Claim("worker", "claim-1", RaisedAt.AddSeconds(-1)));
        call.Claim("worker", "claim-1", RaisedAt.AddMinutes(1));
        Assert.Throws<KnownException>(() => call.Close("worker", "close-1", RaisedAt));
        Assert.Equal(AndonCallStatus.Claimed, call.Status);
    }

    private static AndonCall Create(AndonCallCategory category = AndonCallCategory.MaterialShortage) =>
        AndonCall.Raise("org-1", "env-1", "raise-1", category, "WO-1", "OP-1", "WC-1", "caller", RaisedAt);
}
