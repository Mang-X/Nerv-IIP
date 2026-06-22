using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public class FiniteCapacitySchedulerTests
{
    private static readonly DateTimeOffset GeneratedAtUtc = new(2026, 6, 1, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Schedule_returns_identical_plan_for_repeated_shock_absorber_input()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var scheduler = new FiniteCapacityScheduler();

        var first = scheduler.Schedule(problem, "plan-preview-001", GeneratedAtUtc);
        var second = scheduler.Schedule(problem, "plan-preview-001", GeneratedAtUtc);

        Assert.Equal(
            JsonSerializer.Serialize(first, SchedulingJson.Options),
            JsonSerializer.Serialize(second, SchedulingJson.Options));
        Assert.Equal(
            first.Assignments.Select(x => x.AssignmentId),
            first.Assignments.OrderBy(x => x.StartUtc).ThenBy(x => x.ResourceId).ThenBy(x => x.OperationId).Select(x => x.AssignmentId));
    }

    [Fact]
    public void Schedule_preserves_operation_precedence()
    {
        var plan = ScheduleShockAbsorber();
        var byOperation = plan.Assignments.ToDictionary(x => x.OperationId);
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();

        foreach (var order in problem.Orders)
        {
            foreach (var operation in order.Operations)
            {
                var assignment = byOperation[operation.OperationId];
                foreach (var predecessorId in operation.PredecessorOperationIds)
                {
                    Assert.True(
                        assignment.StartUtc >= byOperation[predecessorId].EndUtc,
                        $"{operation.OperationId} starts before {predecessorId} ends.");
                }
            }
        }
    }

    [Fact]
    public void Schedule_avoids_maintenance_window()
    {
        var plan = ScheduleShockAbsorber();
        var maintenanceStart = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var maintenanceEnd = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var oilAssignments = plan.Assignments.Where(x => x.ResourceId == "DEV-OIL-01");

        Assert.All(oilAssignments, assignment =>
            Assert.False(assignment.StartUtc < maintenanceEnd && assignment.EndUtc > maintenanceStart));
        AssertAssignment(plan, "WO-RUSH-REAR-001-OIL", "DEV-OIL-01",
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 13, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Schedule_places_rush_order_before_normal_order_on_shared_bottleneck()
    {
        var plan = ScheduleShockAbsorber();
        var rushOil = Assignment(plan, "WO-RUSH-REAR-001-OIL");
        var normalOil = Assignment(plan, "WO-FRONT-001-OIL");

        Assert.True(rushOil.EndUtc <= normalOil.StartUtc);
        AssertAssignment(plan, "WO-FRONT-001-OIL", "DEV-OIL-01",
            new DateTimeOffset(2026, 6, 1, 13, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Schedule_reports_due_date_conflict_when_assignment_finishes_late()
    {
        var plan = ScheduleShockAbsorber();

        Assert.Contains(plan.Conflicts, x =>
            x.ReasonCode == ScheduleConflictReasonCodeContract.DueDate
            && x.OrderId == "WO-FRONT-001"
            && x.OperationId == "WO-FRONT-001-TEST"
            && x.Severity == ScheduleConflictSeverityContract.Warning);
        Assert.Contains(plan.ChangeSummary, x =>
            x.OrderId == "WO-FRONT-001"
            && x.OperationId == "WO-FRONT-001-TEST"
            && x.ChangeType == ScheduleChangeTypeContract.Delayed);
    }

    [Fact]
    public void Schedule_returns_plan_level_metrics_for_aps_review()
    {
        var baseProblem = CreateSingleOperationProblem();
        var problem = baseProblem with
        {
            Orders =
            [
                baseProblem.Orders.Single() with
                {
                    DueUtc = new DateTimeOffset(2026, 6, 1, 8, 30, 0, TimeSpan.Zero),
                    Operations =
                    [
                        baseProblem.Orders.Single().Operations.Single() with
                        {
                            DueUtc = new DateTimeOffset(2026, 6, 1, 8, 30, 0, TimeSpan.Zero)
                        }
                    ]
                }
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-metrics-001", GeneratedAtUtc);

        Assert.Equal(1, plan.Metrics.ScheduledOperationCount);
        Assert.Equal(0, plan.Metrics.UnscheduledOperationCount);
        Assert.Equal(60, plan.Metrics.AssignedMinutes);
        Assert.Equal(60, plan.Metrics.MakespanMinutes);
        Assert.Equal(30, plan.Metrics.TotalTardinessMinutes);
        Assert.Equal(1, plan.Metrics.LateOperationCount);
        Assert.Equal(0m, plan.Metrics.OnTimeRate);
        Assert.Equal(0.125m, plan.Metrics.AverageResourceUtilization);
    }

    [Fact]
    public void Schedule_preserves_locked_assignment_and_reserves_capacity()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem() with
        {
            LockedAssignments =
            [
                new SchedulingLockedAssignmentContract(
                    AssignmentId: "lock-oil-001",
                    OrderId: "WO-LOCKED-001",
                    OperationId: "WO-LOCKED-001-OIL",
                    OperationSequence: 30,
                    ResourceId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero),
                    LockReasonCode: "user-lock")
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-preview-lock-001", GeneratedAtUtc);

        var locked = Assignment(plan, "WO-LOCKED-001-OIL");
        Assert.True(locked.IsLocked);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), locked.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero), locked.EndUtc);
        Assert.True(Assignment(plan, "WO-RUSH-REAR-001-OIL").StartUtc >= locked.EndUtc);
    }

    [Fact]
    public void Schedule_reports_invalid_locked_assignment_when_locked_capacity_is_overbooked()
    {
        var problem = CreateSingleOperationProblem() with
        {
            LockedAssignments =
            [
                new SchedulingLockedAssignmentContract(
                    AssignmentId: "lock-overbooked-001",
                    OrderId: "WO-LOCKED-A",
                    OperationId: "LOCK-A",
                    OperationSequence: 10,
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    LockReasonCode: "existing-load"),
                new SchedulingLockedAssignmentContract(
                    AssignmentId: "lock-overbooked-002",
                    OrderId: "WO-LOCKED-B",
                    OperationId: "LOCK-B",
                    OperationSequence: 10,
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero),
                    LockReasonCode: "existing-load")
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-overbooked-locks-001", GeneratedAtUtc);

        Assert.Contains(plan.Conflicts, x =>
            x.ReasonCode == ScheduleConflictReasonCodeContract.InvalidLockedAssignment
            && x.OperationId == "LOCK-A"
            && x.ResourceId == "DEV-SNAPSHOT-01"
            && x.Severity == ScheduleConflictSeverityContract.Error);
        Assert.Contains(plan.Conflicts, x =>
            x.ReasonCode == ScheduleConflictReasonCodeContract.InvalidLockedAssignment
            && x.OperationId == "LOCK-B"
            && x.ResourceId == "DEV-SNAPSHOT-01"
            && x.Severity == ScheduleConflictSeverityContract.Error);
    }

    [Fact]
    public void Schedule_returns_unscheduled_reason_when_no_resource_can_run_operation()
    {
        var baseProblem = ShockAbsorberSchedulingFixture.CreateProblem();
        var blockedOperation = new SchedulingOperationContract(
            OperationId: "WO-NO-CAP-001-PAINT",
            OperationSequence: 10,
            PredecessorOperationIds: [],
            DurationMinutes: 30,
            RequiredCapabilityCode: "CAP-PAINT",
            EligibleResourceIds: ["DEV-PAINT-404"],
            PrimaryResourceId: "DEV-PAINT-404",
            EarliestStartUtc: baseProblem.HorizonStartUtc,
            DueUtc: baseProblem.HorizonEndUtc,
            Priority: 1,
            IsRush: false,
            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
            MaterialReadyUtc: baseProblem.HorizonStartUtc,
            QualityBlockReason: null,
            SourceReference: "MES:WO-NO-CAP-001");
        var problem = baseProblem with
        {
            Orders =
            [
                ..baseProblem.Orders,
                new SchedulingOrderContract(
                    OrderId: "WO-NO-CAP-001",
                    SkuCode: "FG-CUSTOM",
                    Quantity: 1,
                    DueUtc: baseProblem.HorizonEndUtc,
                    Priority: 1,
                    IsRush: false,
                    Operations: [blockedOperation])
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-preview-unscheduled-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-NO-CAP-001"
            && x.OperationId == "WO-NO-CAP-001-PAINT"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.NoEligibleResource);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Schedule_rejects_non_positive_operation_duration(int durationMinutes)
    {
        var problem = ReplaceSingleOperation(CreateSingleOperationProblem(), operation => operation with
        {
            DurationMinutes = durationMinutes
        });
        var scheduler = new FiniteCapacityScheduler();

        var exception = Assert.Throws<ArgumentException>(() =>
            scheduler.Schedule(problem, "plan-invalid-duration-001", GeneratedAtUtc));

        Assert.Contains("DurationMinutes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schedule_rejects_duplicate_resource_or_calendar_ids()
    {
        var problem = CreateSingleOperationProblem();
        var duplicateResourceProblem = problem with
        {
            Resources = [..problem.Resources, problem.Resources.Single()]
        };
        var duplicateCalendarProblem = problem with
        {
            Calendars = [..problem.Calendars, problem.Calendars.Single()]
        };
        var scheduler = new FiniteCapacityScheduler();

        var resourceException = Assert.Throws<ArgumentException>(() =>
            scheduler.Schedule(duplicateResourceProblem, "plan-duplicate-resource-001", GeneratedAtUtc));
        var calendarException = Assert.Throws<ArgumentException>(() =>
            scheduler.Schedule(duplicateCalendarProblem, "plan-duplicate-calendar-001", GeneratedAtUtc));

        Assert.Contains("Duplicate resourceId", resourceException.Message, StringComparison.Ordinal);
        Assert.Contains("Duplicate calendarId", calendarException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schedule_reports_required_message_for_single_blank_resource_id()
    {
        var problem = CreateSingleOperationProblem();
        var blankResourceProblem = problem with
        {
            Resources =
            [
                problem.Resources.Single() with
                {
                    ResourceId = " "
                }
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var exception = Assert.Throws<ArgumentException>(() =>
            scheduler.Schedule(blankResourceProblem, "plan-blank-resource-001", GeneratedAtUtc));

        Assert.Contains("resourceId is required.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Duplicate resourceId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schedule_uses_canonical_fingerprint_for_reordered_equivalent_problem()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var reordered = problem with
        {
            Orders = problem.Orders.Reverse().ToArray(),
            Resources = problem.Resources.Reverse().ToArray(),
            Calendars = problem.Calendars.Reverse().ToArray(),
            UnavailabilityWindows = problem.UnavailabilityWindows.Reverse().ToArray(),
            MaterialReadiness = problem.MaterialReadiness.Reverse().ToArray(),
            QualityBlocks = problem.QualityBlocks.Reverse().ToArray(),
            LockedAssignments = problem.LockedAssignments.Reverse().ToArray()
        };
        var scheduler = new FiniteCapacityScheduler();

        var first = scheduler.Schedule(problem, "plan-fingerprint-001", GeneratedAtUtc);
        var second = scheduler.Schedule(reordered, "plan-fingerprint-002", GeneratedAtUtc);

        Assert.Equal(first.ProblemFingerprint, second.ProblemFingerprint);
    }

    [Fact]
    public void Schedule_reports_capacity_reason_when_resource_is_saturated()
    {
        var problem = CreateSingleOperationProblem() with
        {
            HorizonEndUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            Calendars =
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-SNAPSHOT",
                    ShiftWindows:
                    [
                        new SchedulingTimeWindowContract(
                            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                            "day-shift")
                    ])
            ],
            LockedAssignments =
            [
                new SchedulingLockedAssignmentContract(
                    AssignmentId: "lock-saturated-001",
                    OrderId: "WO-LOCKED-001",
                    OperationId: "LOCKED-OP10",
                    OperationSequence: 10,
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    LockReasonCode: "existing-load")
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-capacity-saturated-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Capacity);
    }

    [Fact]
    public void Schedule_reports_calendar_reason_when_no_shift_can_fit_operation()
    {
        var shiftStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var problem = CreateSingleOperationProblem() with
        {
            Calendars =
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-SNAPSHOT",
                    ShiftWindows:
                    [
                        new SchedulingTimeWindowContract(shiftStart, shiftStart.AddMinutes(30), "short-shift")
                    ])
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-calendar-no-fit-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Calendar);
    }

    [Fact]
    public void Schedule_reports_equipment_reason_when_all_eligible_resources_are_unavailable()
    {
        var problem = CreateSingleOperationProblem() with
        {
            HorizonEndUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            Calendars =
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-SNAPSHOT",
                    ShiftWindows:
                    [
                        new SchedulingTimeWindowContract(
                            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                            "day-shift")
                    ])
            ],
            UnavailabilityWindows =
            [
                new SchedulingUnavailabilityWindowContract(
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    ReasonCode: "maintenance")
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-equipment-unavailable-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Equipment);
    }

    [Fact]
    public void Schedule_inserts_setup_time_before_next_operation_on_same_resource()
    {
        var problem = CreateSingleOperationProblem();
        var firstOperation = problem.Orders.Single().Operations.Single();
        var secondOperation = firstOperation with
        {
            OperationId = "WO-SNAPSHOT-001-OP20",
            OperationSequence = 20,
            PredecessorOperationIds = ["WO-SNAPSHOT-001-OP10"],
            SetupMinutes = 15
        };
        problem = problem with
        {
            Orders =
            [
                problem.Orders.Single() with
                {
                    Operations = [firstOperation, secondOperation]
                }
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-setup-gap-001", GeneratedAtUtc);

        var first = Assignment(plan, "WO-SNAPSHOT-001-OP10");
        var second = Assignment(plan, "WO-SNAPSHOT-001-OP20");
        Assert.Equal(first.EndUtc.AddMinutes(15), second.StartUtc);
        Assert.Equal(second.StartUtc.AddMinutes(60), second.EndUtc);
    }

    [Fact]
    public void Schedule_counts_setup_time_as_resource_load_and_plan_assigned_minutes()
    {
        var problem = CreateSingleOperationProblem();
        var firstOperation = problem.Orders.Single().Operations.Single();
        var secondOperation = firstOperation with
        {
            OperationId = "WO-SNAPSHOT-001-OP20",
            OperationSequence = 20,
            PredecessorOperationIds = ["WO-SNAPSHOT-001-OP10"],
            SetupMinutes = 15
        };
        problem = problem with
        {
            Orders =
            [
                problem.Orders.Single() with
                {
                    Operations = [firstOperation, secondOperation]
                }
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-setup-load-001", GeneratedAtUtc);

        var load = Assert.Single(plan.ResourceLoads, x => x.ResourceId == "DEV-SNAPSHOT-01");
        Assert.Equal(135, plan.Metrics.AssignedMinutes);
        Assert.Equal(135, load.AssignedMinutes);
        Assert.Equal(0.2812m, plan.Metrics.AverageResourceUtilization);
        Assert.Equal(0.2812m, load.Utilization);
    }

    [Fact]
    public void Schedule_accumulates_multiple_setup_windows_in_resource_load()
    {
        var problem = CreateSingleOperationProblem();
        var firstOperation = problem.Orders.Single().Operations.Single();
        var secondOperation = firstOperation with
        {
            OperationId = "WO-SNAPSHOT-001-OP20",
            OperationSequence = 20,
            PredecessorOperationIds = ["WO-SNAPSHOT-001-OP10"],
            SetupMinutes = 15
        };
        var thirdOperation = firstOperation with
        {
            OperationId = "WO-SNAPSHOT-001-OP30",
            OperationSequence = 30,
            PredecessorOperationIds = ["WO-SNAPSHOT-001-OP20"],
            SetupMinutes = 20
        };
        problem = problem with
        {
            Orders =
            [
                problem.Orders.Single() with
                {
                    Operations = [firstOperation, secondOperation, thirdOperation]
                }
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-setup-load-multiple-001", GeneratedAtUtc);

        var load = Assert.Single(plan.ResourceLoads, x => x.ResourceId == "DEV-SNAPSHOT-01");
        Assert.Equal(215, plan.Metrics.AssignedMinutes);
        Assert.Equal(215, load.AssignedMinutes);
        Assert.Equal(0.4479m, plan.Metrics.AverageResourceUtilization);
        Assert.Equal(0.4479m, load.Utilization);
    }

    [Fact]
    public async Task Schedule_advances_setup_after_pre_processing_resource_block()
    {
        var problem = CreateSingleOperationProblem();
        var firstOperation = problem.Orders.Single().Operations.Single();
        var secondOperation = firstOperation with
        {
            OperationId = "WO-SNAPSHOT-001-OP20",
            OperationSequence = 20,
            PredecessorOperationIds = ["WO-SNAPSHOT-001-OP10"],
            SetupMinutes = 15
        };
        problem = problem with
        {
            Orders =
            [
                problem.Orders.Single() with
                {
                    Operations = [firstOperation, secondOperation]
                }
            ],
            UnavailabilityWindows =
            [
                new SchedulingUnavailabilityWindowContract(
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 9, 5, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 9, 15, 0, TimeSpan.Zero),
                    ReasonCode: "maintenance")
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = await Task
            .Run(() => scheduler.Schedule(problem, "plan-setup-block-001", GeneratedAtUtc))
            .WaitAsync(TimeSpan.FromSeconds(5));

        var second = Assignment(plan, "WO-SNAPSHOT-001-OP20");
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero), second.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 10, 30, 0, TimeSpan.Zero), second.EndUtc);
        var load = Assert.Single(plan.ResourceLoads, x => x.ResourceId == "DEV-SNAPSHOT-01");
        Assert.Equal(135, load.AssignedMinutes);
    }

    [Fact]
    public void Schedule_requires_declared_skill_and_tooling_codes_on_selected_resource()
    {
        var problem = CreateSingleOperationProblem();
        var operation = problem.Orders.Single().Operations.Single() with
        {
            RequiredSkillCodes = ["skill.welder"],
            RequiredToolingIds = ["fixture.a"]
        };
        problem = problem with
        {
            Orders =
            [
                problem.Orders.Single() with
                {
                    Operations = [operation]
                }
            ],
            Resources =
            [
                problem.Resources.Single() with
                {
                    CapabilityCodes = ["CAP-SNAPSHOT", "skill.welder"]
                }
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-skill-tooling-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.NoEligibleResource);
    }

    [Fact]
    public void Schedule_reports_outside_horizon_when_shift_can_fit_but_horizon_cannot()
    {
        var shiftStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var problem = CreateSingleOperationProblem() with
        {
            HorizonEndUtc = shiftStart.AddHours(2),
            Orders =
            [
                CreateSingleOperationProblem().Orders.Single() with
                {
                    DueUtc = shiftStart.AddHours(2),
                    Operations =
                    [
                        CreateSingleOperationProblem().Orders.Single().Operations.Single() with
                        {
                            DurationMinutes = 180,
                            DueUtc = shiftStart.AddHours(2)
                        }
                    ]
                }
            ],
            Calendars =
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-SNAPSHOT",
                    ShiftWindows:
                    [
                        new SchedulingTimeWindowContract(shiftStart, shiftStart.AddHours(8), "day-shift")
                    ])
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-outside-horizon-no-fit-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.OutsideHorizon);
    }

    [Fact]
    public void Schedule_rejects_null_required_collections()
    {
        var problem = CreateSingleOperationProblem() with
        {
            Orders = null!
        };
        var scheduler = new FiniteCapacityScheduler();

        var exception = Assert.Throws<ArgumentException>(() =>
            scheduler.Schedule(problem, "plan-null-collections-001", GeneratedAtUtc));

        Assert.Contains("Orders", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schedule_marks_dependent_operation_unscheduled_when_predecessor_fails()
    {
        var problem = CreateFailedPredecessorProblem();
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-failed-predecessor-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-DEPENDENT-001"
            && x.OperationId == "OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.NoEligibleResource);
        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-DEPENDENT-001"
            && x.OperationId == "OP20"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.PredecessorUnscheduled);
    }

    [Fact]
    public void Schedule_marks_stalled_dependent_unscheduled_when_predecessor_fails_afterward()
    {
        var problem = CreateFailedPredecessorProblemWithRushDependent();
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-stalled-dependent-failed-predecessor-001", GeneratedAtUtc);

        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-DEPENDENT-001"
            && x.OperationId == "OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.NoEligibleResource);
        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-DEPENDENT-001"
            && x.OperationId == "OP20"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.PredecessorUnscheduled);
    }

    [Fact]
    public void Schedule_allows_parallel_capacity_when_overlaps_are_not_concurrent()
    {
        var problem = CreateParallelCapacityProblem() with
        {
            LockedAssignments =
            [
                new SchedulingLockedAssignmentContract(
                    AssignmentId: "lock-capacity-001",
                    OrderId: "WO-LOCKED-A",
                    OperationId: "LOCK-A",
                    OperationSequence: 10,
                    ResourceId: "DEV-PARALLEL-01",
                    WorkCenterId: "WC-PARALLEL",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    LockReasonCode: "existing-load"),
                new SchedulingLockedAssignmentContract(
                    AssignmentId: "lock-capacity-002",
                    OrderId: "WO-LOCKED-B",
                    OperationId: "LOCK-B",
                    OperationSequence: 10,
                    ResourceId: "DEV-PARALLEL-01",
                    WorkCenterId: "WC-PARALLEL",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero),
                    LockReasonCode: "existing-load")
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-parallel-capacity-001", GeneratedAtUtc);

        AssertAssignment(plan, "OP-CAPACITY", "DEV-PARALLEL-01",
            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Schedule_resource_load_available_minutes_reflect_capacity_units()
    {
        var problem = CreateParallelCapacityProblem() with
        {
            UnavailabilityWindows =
            [
                new SchedulingUnavailabilityWindowContract(
                    ResourceId: "DEV-PARALLEL-01",
                    WorkCenterId: "WC-PARALLEL",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    ReasonCode: "maintenance")
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-parallel-load-001", GeneratedAtUtc);

        var load = Assert.Single(plan.ResourceLoads, x => x.ResourceId == "DEV-PARALLEL-01");
        Assert.Equal(360, load.AvailableMinutes);
    }

    [Fact]
    public void Schedule_merges_overlapping_unavailability_when_computing_load()
    {
        var problem = CreateParallelCapacityProblem() with
        {
            UnavailabilityWindows =
            [
                new SchedulingUnavailabilityWindowContract(
                    ResourceId: "DEV-PARALLEL-01",
                    WorkCenterId: "WC-PARALLEL",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
                    ReasonCode: "maintenance"),
                new SchedulingUnavailabilityWindowContract(
                    ResourceId: "DEV-PARALLEL-01",
                    WorkCenterId: "WC-PARALLEL",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 10, 30, 0, TimeSpan.Zero),
                    ReasonCode: "alarm")
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-overlapping-unavailability-load-001", GeneratedAtUtc);

        var load = Assert.Single(plan.ResourceLoads, x => x.ResourceId == "DEV-PARALLEL-01");
        Assert.Equal(300, load.AvailableMinutes);
    }

    [Fact]
    public void Schedule_resolves_predecessors_within_order_when_operation_ids_are_reused()
    {
        var problem = CreateDuplicateLocalOperationIdProblem();
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-duplicate-operation-id-001", GeneratedAtUtc);

        var orderAOp10 = Assignment(plan, "WO-LOCAL-A", "OP10");
        var orderBOp10 = Assignment(plan, "WO-LOCAL-B", "OP10");
        var orderBOp20 = Assignment(plan, "WO-LOCAL-B", "OP20");
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), orderAOp10.StartUtc);
        Assert.True(orderBOp20.StartUtc >= orderBOp10.EndUtc);
    }

    [Theory]
    [InlineData("operation", "WO-SNAPSHOT-001-OP10")]
    [InlineData("order", "WO-SNAPSHOT-001")]
    [InlineData("sku", "FG-SNAPSHOT")]
    [InlineData("resource", "DEV-SNAPSHOT-01")]
    public void Schedule_applies_top_level_material_readiness_by_delaying_start(string scopeType, string scopeId)
    {
        var readyUtc = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var problem = CreateSingleOperationProblem() with
        {
            MaterialReadiness =
            [
                new SchedulingMaterialReadinessContract(
                    ScopeType: scopeType,
                    ScopeId: scopeId,
                    MaterialReadyUtc: readyUtc,
                    IsReady: false,
                    ReasonCodes: ["material-shortage"])
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-material-readiness-001", GeneratedAtUtc);

        var assignment = Assignment(plan, "WO-SNAPSHOT-001-OP10");
        Assert.Equal(readyUtc, assignment.StartUtc);
        Assert.Equal(readyUtc.AddMinutes(60), assignment.EndUtc);
    }

    [Theory]
    [InlineData("operation", "WO-SNAPSHOT-001-OP10")]
    [InlineData("order", "WO-SNAPSHOT-001")]
    [InlineData("sku", "FG-SNAPSHOT")]
    [InlineData("resource", "DEV-SNAPSHOT-01")]
    public void Schedule_applies_finite_top_level_quality_block_by_delaying_start(string scopeType, string scopeId)
    {
        var blockedUntilUtc = new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero);
        var problem = CreateSingleOperationProblem() with
        {
            QualityBlocks =
            [
                new SchedulingQualityBlockContract(
                    ScopeType: scopeType,
                    ScopeId: scopeId,
                    ReasonCode: "inspection-hold",
                    BlockedUntilUtc: blockedUntilUtc)
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-quality-delay-001", GeneratedAtUtc);

        var assignment = Assignment(plan, "WO-SNAPSHOT-001-OP10");
        Assert.Equal(blockedUntilUtc, assignment.StartUtc);
        Assert.Equal(blockedUntilUtc.AddMinutes(60), assignment.EndUtc);
    }

    [Fact]
    public void Schedule_reports_unscheduled_quality_reason_for_open_ended_top_level_quality_block()
    {
        var problem = CreateSingleOperationProblem() with
        {
            QualityBlocks =
            [
                new SchedulingQualityBlockContract(
                    ScopeType: "order",
                    ScopeId: "WO-SNAPSHOT-001",
                    ReasonCode: "quality-quarantine",
                    BlockedUntilUtc: null)
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-quality-blocked-001", GeneratedAtUtc);

        Assert.DoesNotContain(plan.Assignments, x => x.OperationId == "WO-SNAPSHOT-001-OP10");
        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-SNAPSHOT-001"
            && x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Quality);
        Assert.Contains(plan.Conflicts, x =>
            x.OrderId == "WO-SNAPSHOT-001"
            && x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Quality
            && x.Severity == ScheduleConflictSeverityContract.Error);
    }

    [Fact]
    public void Schedule_uses_unblocked_alternate_resource_when_resource_scoped_quality_block_exists()
    {
        var problem = CreateSingleOperationProblemWithAlternateResource() with
        {
            QualityBlocks =
            [
                new SchedulingQualityBlockContract(
                    ScopeType: "resource",
                    ScopeId: "DEV-SNAPSHOT-01",
                    ReasonCode: "inspection-hold",
                    BlockedUntilUtc: new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero))
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-resource-quality-alternate-001", GeneratedAtUtc);

        AssertAssignment(plan, "WO-SNAPSHOT-001-OP10", "DEV-SNAPSHOT-02",
            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Schedule_delays_resource_when_quality_block_extends_into_candidate_slot()
    {
        var problem = CreateSingleOperationProblem() with
        {
            LockedAssignments =
            [
                new SchedulingLockedAssignmentContract(
                    AssignmentId: "lock-before-quality-001",
                    OrderId: "WO-LOCKED-001",
                    OperationId: "LOCKED-OP10",
                    OperationSequence: 10,
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                    LockReasonCode: "existing-load")
            ],
            QualityBlocks =
            [
                new SchedulingQualityBlockContract(
                    ScopeType: "resource",
                    ScopeId: "DEV-SNAPSHOT-01",
                    ReasonCode: "quality-hold",
                    BlockedUntilUtc: new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero))
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-resource-quality-overlap-001", GeneratedAtUtc);

        AssertAssignment(plan, "WO-SNAPSHOT-001-OP10", "DEV-SNAPSHOT-01",
            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Schedule_reports_quality_reason_when_open_ended_resource_quality_block_has_no_alternate()
    {
        var problem = CreateSingleOperationProblem() with
        {
            QualityBlocks =
            [
                new SchedulingQualityBlockContract(
                    ScopeType: "resource",
                    ScopeId: "DEV-SNAPSHOT-01",
                    ReasonCode: "resource-quarantine",
                    BlockedUntilUtc: null)
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-resource-quality-blocked-001", GeneratedAtUtc);

        Assert.DoesNotContain(plan.Assignments, x => x.OperationId == "WO-SNAPSHOT-001-OP10");
        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-SNAPSHOT-001"
            && x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Quality
            && x.Message == "resource-quarantine");
        Assert.Contains(plan.Conflicts, x =>
            x.OrderId == "WO-SNAPSHOT-001"
            && x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Quality
            && x.Severity == ScheduleConflictSeverityContract.Error);
    }

    [Fact]
    public void Schedule_reports_material_reason_for_open_ended_top_level_material_unavailability()
    {
        var problem = CreateSingleOperationProblem() with
        {
            MaterialReadiness =
            [
                new SchedulingMaterialReadinessContract(
                    ScopeType: "order",
                    ScopeId: "WO-SNAPSHOT-001",
                    MaterialReadyUtc: null,
                    IsReady: false,
                    ReasonCodes: ["material-shortage"])
            ]
        };
        var scheduler = new FiniteCapacityScheduler();

        var plan = scheduler.Schedule(problem, "plan-material-blocked-001", GeneratedAtUtc);

        Assert.DoesNotContain(plan.Assignments, x => x.OperationId == "WO-SNAPSHOT-001-OP10");
        Assert.Contains(plan.UnscheduledOperations, x =>
            x.OrderId == "WO-SNAPSHOT-001"
            && x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Material
            && x.Message == "material-shortage");
        Assert.Contains(plan.Conflicts, x =>
            x.OrderId == "WO-SNAPSHOT-001"
            && x.OperationId == "WO-SNAPSHOT-001-OP10"
            && x.ReasonCode == ScheduleConflictReasonCodeContract.Material
            && x.Severity == ScheduleConflictSeverityContract.Error);
    }

    private static SchedulePlanContract ScheduleShockAbsorber()
    {
        var scheduler = new FiniteCapacityScheduler();
        return scheduler.Schedule(
            ShockAbsorberSchedulingFixture.CreateProblem(),
            "plan-preview-001",
            GeneratedAtUtc);
    }

    private static SchedulingProblemContract CreateSingleOperationProblem()
    {
        var shiftStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var shiftEnd = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-snapshot-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: shiftStart,
            HorizonEndUtc: shiftEnd,
            Orders:
            [
                new SchedulingOrderContract(
                    OrderId: "WO-SNAPSHOT-001",
                    SkuCode: "FG-SNAPSHOT",
                    Quantity: 1,
                    DueUtc: shiftEnd,
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "WO-SNAPSHOT-001-OP10",
                            OperationSequence: 10,
                            PredecessorOperationIds: [],
                            DurationMinutes: 60,
                            RequiredCapabilityCode: "CAP-SNAPSHOT",
                            EligibleResourceIds: ["DEV-SNAPSHOT-01"],
                            PrimaryResourceId: "DEV-SNAPSHOT-01",
                            EarliestStartUtc: shiftStart,
                            DueUtc: shiftEnd,
                            Priority: 1,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: null,
                            QualityBlockReason: null,
                            SourceReference: "TEST:SNAPSHOT")
                    ])
            ],
            Resources:
            [
                new SchedulingResourceContract(
                    ResourceId: "DEV-SNAPSHOT-01",
                    WorkCenterId: "WC-SNAPSHOT",
                    CapabilityCodes: ["CAP-SNAPSHOT"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-SNAPSHOT",
                    SortKey: "001")
            ],
            Calendars:
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-SNAPSHOT",
                    ShiftWindows:
                    [
                        new SchedulingTimeWindowContract(shiftStart, shiftEnd, "day-shift")
                    ])
            ],
            UnavailabilityWindows: [],
            MaterialReadiness: [],
            QualityBlocks: [],
            LockedAssignments: []);
    }

    private static SchedulingProblemContract CreateSingleOperationProblemWithAlternateResource()
    {
        var problem = CreateSingleOperationProblem();
        var operation = problem.Orders.Single().Operations.Single() with
        {
            EligibleResourceIds = ["DEV-SNAPSHOT-01", "DEV-SNAPSHOT-02"]
        };

        return problem with
        {
            Orders =
            [
                problem.Orders.Single() with
                {
                    Operations = [operation]
                }
            ],
            Resources =
            [
                ..problem.Resources,
                new SchedulingResourceContract(
                    ResourceId: "DEV-SNAPSHOT-02",
                    WorkCenterId: "WC-SNAPSHOT",
                    CapabilityCodes: ["CAP-SNAPSHOT"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-SNAPSHOT",
                    SortKey: "002")
            ]
        };
    }

    private static SchedulingProblemContract CreateFailedPredecessorProblem()
    {
        var problem = CreateSingleOperationProblem();
        return problem with
        {
            ProblemId = "problem-failed-predecessor-001",
            Orders =
            [
                new SchedulingOrderContract(
                    OrderId: "WO-DEPENDENT-001",
                    SkuCode: "FG-DEPENDENT",
                    Quantity: 1,
                    DueUtc: problem.HorizonEndUtc,
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "OP10",
                            OperationSequence: 10,
                            PredecessorOperationIds: [],
                            DurationMinutes: 60,
                            RequiredCapabilityCode: "CAP-MISSING",
                            EligibleResourceIds: ["DEV-SNAPSHOT-01"],
                            PrimaryResourceId: "DEV-SNAPSHOT-01",
                            EarliestStartUtc: problem.HorizonStartUtc,
                            DueUtc: problem.HorizonEndUtc,
                            Priority: 1,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: null,
                            QualityBlockReason: null,
                            SourceReference: "TEST:FAILED-PREDECESSOR"),
                        new SchedulingOperationContract(
                            OperationId: "OP20",
                            OperationSequence: 20,
                            PredecessorOperationIds: ["OP10"],
                            DurationMinutes: 60,
                            RequiredCapabilityCode: "CAP-SNAPSHOT",
                            EligibleResourceIds: ["DEV-SNAPSHOT-01"],
                            PrimaryResourceId: "DEV-SNAPSHOT-01",
                            EarliestStartUtc: problem.HorizonStartUtc,
                            DueUtc: problem.HorizonEndUtc,
                            Priority: 1,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: null,
                            QualityBlockReason: null,
                            SourceReference: "TEST:FAILED-PREDECESSOR")
                    ])
            ]
        };
    }

    private static SchedulingProblemContract CreateFailedPredecessorProblemWithRushDependent()
    {
        var problem = CreateFailedPredecessorProblem();
        var order = problem.Orders.Single();
        return problem with
        {
            Orders =
            [
                order with
                {
                    Operations =
                    [
                        order.Operations.Single(x => x.OperationId == "OP10") with
                        {
                            Priority = 1,
                            IsRush = false,
                            DueUtc = new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero)
                        },
                        order.Operations.Single(x => x.OperationId == "OP20") with
                        {
                            Priority = 100,
                            IsRush = true,
                            DueUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)
                        }
                    ]
                }
            ]
        };
    }

    private static SchedulingProblemContract CreateParallelCapacityProblem()
    {
        var shiftStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var shiftEnd = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-parallel-capacity-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: shiftStart,
            HorizonEndUtc: shiftEnd,
            Orders:
            [
                new SchedulingOrderContract(
                    OrderId: "WO-CAPACITY-001",
                    SkuCode: "FG-CAPACITY",
                    Quantity: 1,
                    DueUtc: shiftEnd,
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        new SchedulingOperationContract(
                            OperationId: "OP-CAPACITY",
                            OperationSequence: 10,
                            PredecessorOperationIds: [],
                            DurationMinutes: 180,
                            RequiredCapabilityCode: "CAP-PARALLEL",
                            EligibleResourceIds: ["DEV-PARALLEL-01"],
                            PrimaryResourceId: "DEV-PARALLEL-01",
                            EarliestStartUtc: shiftStart,
                            DueUtc: shiftEnd,
                            Priority: 1,
                            IsRush: false,
                            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                            MaterialReadyUtc: null,
                            QualityBlockReason: null,
                            SourceReference: "TEST:PARALLEL")
                    ])
            ],
            Resources:
            [
                new SchedulingResourceContract(
                    ResourceId: "DEV-PARALLEL-01",
                    WorkCenterId: "WC-PARALLEL",
                    CapabilityCodes: ["CAP-PARALLEL"],
                    CapacityUnits: 2,
                    CalendarId: "CAL-PARALLEL",
                    SortKey: "001")
            ],
            Calendars:
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-PARALLEL",
                    ShiftWindows:
                    [
                        new SchedulingTimeWindowContract(shiftStart, shiftEnd, "day-shift")
                    ])
            ],
            UnavailabilityWindows: [],
            MaterialReadiness: [],
            QualityBlocks: [],
            LockedAssignments: []);
    }

    private static SchedulingProblemContract CreateDuplicateLocalOperationIdProblem()
    {
        var shiftStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var shiftEnd = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: "problem-duplicate-operation-id-001",
            OrganizationId: "org-001",
            EnvironmentId: "prod",
            HorizonStartUtc: shiftStart,
            HorizonEndUtc: shiftEnd,
            Orders:
            [
                new SchedulingOrderContract(
                    OrderId: "WO-LOCAL-A",
                    SkuCode: "FG-LOCAL-A",
                    Quantity: 1,
                    DueUtc: shiftEnd,
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        CreateLocalOperation("OP10", 10, [], shiftStart, shiftEnd)
                    ]),
                new SchedulingOrderContract(
                    OrderId: "WO-LOCAL-B",
                    SkuCode: "FG-LOCAL-B",
                    Quantity: 1,
                    DueUtc: shiftEnd,
                    Priority: 1,
                    IsRush: false,
                    Operations:
                    [
                        CreateLocalOperation("OP10", 10, [], shiftStart, shiftEnd),
                        CreateLocalOperation("OP20", 20, ["OP10"], shiftStart, shiftEnd)
                    ])
            ],
            Resources:
            [
                new SchedulingResourceContract(
                    ResourceId: "DEV-LOCAL-01",
                    WorkCenterId: "WC-LOCAL",
                    CapabilityCodes: ["CAP-LOCAL"],
                    CapacityUnits: 1,
                    CalendarId: "CAL-LOCAL",
                    SortKey: "001")
            ],
            Calendars:
            [
                new SchedulingCalendarContract(
                    CalendarId: "CAL-LOCAL",
                    ShiftWindows:
                    [
                        new SchedulingTimeWindowContract(shiftStart, shiftEnd, "day-shift")
                    ])
            ],
            UnavailabilityWindows: [],
            MaterialReadiness: [],
            QualityBlocks: [],
            LockedAssignments: []);
    }

    private static SchedulingProblemContract ReplaceSingleOperation(
        SchedulingProblemContract problem,
        Func<SchedulingOperationContract, SchedulingOperationContract> replace)
    {
        var order = problem.Orders.Single();
        return problem with
        {
            Orders =
            [
                order with
                {
                    Operations = [replace(order.Operations.Single())]
                }
            ]
        };
    }

    private static SchedulingOperationContract CreateLocalOperation(
        string operationId,
        int sequence,
        IReadOnlyCollection<string> predecessorIds,
        DateTimeOffset shiftStart,
        DateTimeOffset shiftEnd)
    {
        return new SchedulingOperationContract(
            OperationId: operationId,
            OperationSequence: sequence,
            PredecessorOperationIds: predecessorIds,
            DurationMinutes: 60,
            RequiredCapabilityCode: "CAP-LOCAL",
            EligibleResourceIds: ["DEV-LOCAL-01"],
            PrimaryResourceId: "DEV-LOCAL-01",
            EarliestStartUtc: shiftStart,
            DueUtc: shiftEnd,
            Priority: 1,
            IsRush: false,
            SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
            MaterialReadyUtc: null,
            QualityBlockReason: null,
            SourceReference: "TEST:LOCAL");
    }

    private static ScheduleAssignmentContract Assignment(SchedulePlanContract plan, string operationId)
    {
        return plan.Assignments.Single(x => x.OperationId == operationId);
    }

    private static ScheduleAssignmentContract Assignment(SchedulePlanContract plan, string orderId, string operationId)
    {
        return plan.Assignments.Single(x => x.OrderId == orderId && x.OperationId == operationId);
    }

    private static void AssertAssignment(
        SchedulePlanContract plan,
        string operationId,
        string resourceId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        var assignment = Assignment(plan, operationId);
        Assert.Equal(resourceId, assignment.ResourceId);
        Assert.Equal(startUtc, assignment.StartUtc);
        Assert.Equal(endUtc, assignment.EndUtc);
    }
}
