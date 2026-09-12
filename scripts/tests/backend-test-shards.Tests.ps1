# The validator reports findings on stdout and exits 1 rather than throwing, which is what lets
# these assertions match whole sentences instead of the short fragments a thrown (and therefore
# width-wrapped) message forced — this file used to also scrape the command log to reassemble that
# text, and both workarounds are gone. Why the shape matters:
# docs/architecture/backend-ci-build-strategy.md ("走查收尾" 第 3 条).
#
# Whitespace is collapsed so that where the script chose to break lines is not part of the
# contract. The assertions are about content, not layout.
    # is a committed file, so any *new backend test project* — a change that touches no timing code
    # and breaks nothing — has no row in it and produced a `timing-assembly-missing` warning, which
    # this contract then turned into a red Backend Test Shard Governance job until a human
    # regenerated and re-committed the snapshot. That is the exact human refresh ceremony #1507
    # deleted, re-imposed by a test, over a warning whose own text says "This is report-only".
    # docs/architecture/test-evidence-governance.md states the same rule in prose: coverage gaps are
    # report-only warnings, and the committed snapshot is never required to be complete.
    #
    # The gap count is printed instead of asserted, so a human reading the job log can see the
    # coverage drift that is worth knowing about and worthless as a gate.
    # (`timing-assembly-missing`) means the source never observed that assembly at all — which is
    # exactly what adding a backend test project produces, and exactly what the balance report is
    # allowed to estimate over. Asserting zero gaps here is the deleted red gate wearing another
    # costume: it would turn "someone added a test project" into a red Backend Test Shard Governance
    # job until a human regenerated and re-committed the snapshot, which is the #1507 ceremony. The
    # same rule is stated in prose in docs/architecture/test-evidence-governance.md.
    #
    # So the gap count is printed for a human reading the job log, and only key stability is asserted.
    $keyResolutionByLayout = [ordered]@{}
    # (1) Structure: the key is reversibly sourceId|ruleId|identity, in that order, and nothing else.
    #     Test identities, rule ids and source ids are C#/ kebab identifiers and never contain `|`,
    #     which is what makes the split a faithful inverse. This is what catches a key that returns a
    #     constant, an empty string, drops a segment, or splices in an extra field such as
    #     `requiredLane` — the last of which is why the "carries no lane" claim in
    #     docs/architecture/test-evidence-governance.md is now enforced rather than merely written.
    $structuralKeyChecks = 0
    foreach ($shard in @($manifest.fastShards)) {
        foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
    # reads is the **logical** lane: it strips any `-shard-N` suffix before matching, so the shard
    # dimension is gone by the time any comparison happens. That is what keeps the third hard gate
    # ("a selected real-dependency lane executed nothing") meaningful while leaving a re-shard unable
    # to change a verdict. Asserted rather than described: every rule must decide identically for a
    # logical lane and for every shard spelling of it.
    # Narrative: docs/architecture/test-evidence-governance.md, "Timing data is a cache, not a
    # governed asset" (lane as applicability condition versus identity key).
    $logicalLanesUnderTest = @('backend', 'connector-host', 'postgres', 'full-chain', 'performance', 'redis-cap')
    $laneSuffixCases = 0
    foreach ($rule in @($evidencePolicy.rules)) {
