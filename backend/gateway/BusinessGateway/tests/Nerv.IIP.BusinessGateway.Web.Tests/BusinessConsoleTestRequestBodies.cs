namespace Nerv.IIP.BusinessGateway.Web.Tests;

internal static class BusinessConsoleTestRequestBodies
{
    public static bool IsMasterDataCreatePath(string path) => path is
        "/api/business-console/v1/master-data/skus" or
        "/api/business-console/v1/master-data/product-categories" or
        "/api/business-console/v1/master-data/skills" or
        "/api/business-console/v1/master-data/business-partners" or
        "/api/business-console/v1/master-data/units-of-measure" or
        "/api/business-console/v1/master-data/uom-conversions" or
        "/api/business-console/v1/master-data/workshops" or
        "/api/business-console/v1/master-data/sites" or
        "/api/business-console/v1/master-data/production-lines" or
        "/api/business-console/v1/master-data/work-centers" or
        "/api/business-console/v1/master-data/device-assets" or
        "/api/business-console/v1/master-data/shifts" or
        "/api/business-console/v1/master-data/work-calendars" or
        "/api/business-console/v1/master-data/teams" or
        "/api/business-console/v1/master-data/departments" or
        "/api/business-console/v1/master-data/personnel-skills" or
        "/api/business-console/v1/master-data/reference-data";

    public static bool IsEngineeringWritePath(string path) => path is
        "/api/business-console/v1/engineering/documents" or
        "/api/business-console/v1/engineering/items" or
        "/api/business-console/v1/engineering/engineering-boms/release" or
        "/api/business-console/v1/engineering/manufacturing-boms/release" or
        "/api/business-console/v1/engineering/routings/release" or
        "/api/business-console/v1/engineering/standard-operations" or
        "/api/business-console/v1/engineering/standard-operations/OP-001" or
        "/api/business-console/v1/engineering/standard-operations/OP-001/archive" or
        "/api/business-console/v1/engineering/engineering-changes/release" or
        "/api/business-console/v1/engineering/production-versions" or
        "/api/business-console/v1/engineering/production-versions/pv-001" or
        "/api/business-console/v1/engineering/production-versions/pv-001/archive";

    public static Dictionary<string, object?> ValidMasterDataCreateBody(string path) => path switch
    {
        "/api/business-console/v1/master-data/skus" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "SKU-001",
            ["name"] = "Demo SKU",
            ["baseUomCode"] = "EA",
            ["category"] = "finished-good",
            ["materialType"] = "standard",
            ["batchTrackingPolicy"] = "none",
            ["serialTrackingPolicy"] = "none",
            ["shelfLifePolicyCode"] = "none",
            ["storageConditionCode"] = "ambient",
            ["defaultBarcodeRuleCode"] = "default",
            ["qualityRequired"] = true,
            ["complianceTags"] = Array.Empty<string>(),
        },
        "/api/business-console/v1/master-data/product-categories" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["categoryCode"] = "CAT-001",
            ["categoryName"] = "Finished Goods",
            ["parentCode"] = null,
            ["description"] = "Finished goods category",
        },
        "/api/business-console/v1/master-data/skills" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["skillCode"] = "SK-WELD",
            ["skillName"] = "Welding",
            ["groupName"] = "Manufacturing",
            ["requiresCertification"] = true,
            ["validityMonths"] = 24,
            ["description"] = "Welding qualification",
        },
        "/api/business-console/v1/master-data/business-partners" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "SUP-001",
            ["partnerType"] = "supplier",
            ["name"] = "Demo Supplier",
        },
        "/api/business-console/v1/master-data/units-of-measure" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "EA",
            ["name"] = "Each",
            ["dimensionType"] = "count",
            ["precision"] = 0,
            ["roundingMode"] = "half-up",
        },
        "/api/business-console/v1/master-data/uom-conversions" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["fromUomCode"] = "BOX",
            ["toUomCode"] = "EA",
            ["factor"] = 12,
            ["offset"] = 0,
            ["precision"] = 6,
            ["roundingMode"] = "half-up",
            ["effectiveFrom"] = "2026-01-01",
        },
        "/api/business-console/v1/master-data/workshops" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "WS-01",
            ["name"] = "Workshop 1",
            ["siteCode"] = "SITE-01",
            ["managerUserId"] = "user-manager",
            ["description"] = "Process area",
        },
        "/api/business-console/v1/master-data/sites" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "SITE-01",
            ["name"] = "Main Site",
            ["timezone"] = "Asia/Shanghai",
        },
        "/api/business-console/v1/master-data/production-lines" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "LINE-01",
            ["name"] = "Line 1",
            ["siteCode"] = "SITE-01",
        },
        "/api/business-console/v1/master-data/work-centers" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "WC-01",
            ["name"] = "Work Center 1",
            ["capacityMinutesPerDay"] = 480,
            ["resourceType"] = "line",
            ["plantCode"] = "SITE-01",
            ["lineCode"] = "LINE-01",
            ["defaultCalendarCode"] = "CAL-01",
            ["capacityUnit"] = "minute",
            ["finiteCapacity"] = true,
        },
        "/api/business-console/v1/master-data/device-assets" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "DEV-01",
            ["model"] = "Robot",
            ["lineCode"] = "LINE-01",
            ["workCenterCode"] = "WC-01",
            ["assetClassCode"] = "robot",
            ["manufacturer"] = "Demo Maker",
            ["serialNo"] = "SN-001",
            ["minimumCapacity"] = 1,
            ["maximumCapacity"] = 10,
            ["capacityUomCode"] = "EA",
            ["criticality"] = "high",
            ["maintainable"] = true,
            ["telemetryEnabled"] = true,
            ["externalReferences"] = new Dictionary<string, string> { ["iiot"] = "DEV-01" },
        },
        "/api/business-console/v1/master-data/shifts" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "SHIFT-A",
            ["name"] = "Shift A",
            ["startsAt"] = "08:00:00",
            ["endsAt"] = "16:00:00",
            ["paidMinutes"] = 480,
        },
        "/api/business-console/v1/master-data/work-calendars" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "CAL-01",
            ["name"] = "Standard Calendar",
        },
        "/api/business-console/v1/master-data/teams" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "TEAM-A",
            ["name"] = "Team A",
            ["departmentCode"] = "DEP-01",
            ["shiftCode"] = "SHIFT-A",
        },
        "/api/business-console/v1/master-data/departments" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["code"] = "DEP-01",
            ["name"] = "Production Department",
            ["parentDepartmentCode"] = null,
        },
        "/api/business-console/v1/master-data/personnel-skills" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["userId"] = "user-001",
            ["skillCode"] = "WELD",
            ["level"] = "L2",
            ["effectiveFrom"] = "2026-01-01",
            ["effectiveTo"] = "2026-12-31",
        },
        "/api/business-console/v1/master-data/reference-data" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["codeSet"] = "asset-class",
            ["code"] = "robot",
            ["name"] = "Robot",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown MasterData create path."),
    };

    public static Dictionary<string, object?> ValidEngineeringWriteBody(string path) => path switch
    {
        "/api/business-console/v1/engineering/documents" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["documentNumber"] = "DOC-001",
            ["revision"] = "A",
            ["fileId"] = "file-001",
            ["fileName"] = "design.dwg",
            ["contentType"] = "application/dwg",
            ["documentType"] = "cad",
            ["idempotencyKey"] = "doc-001",
        },
        "/api/business-console/v1/engineering/items" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["itemCode"] = "ITEM-001",
            ["revision"] = "A",
            ["name"] = "Demo item",
            ["release"] = true,
            ["idempotencyKey"] = "item-001",
        },
        "/api/business-console/v1/engineering/engineering-boms/release" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["bomCode"] = "EBOM-001",
            ["revision"] = "A",
            ["parentItemCode"] = "ITEM-001",
            ["effectiveDate"] = "2026-06-01",
            ["lines"] = new[] { new { componentCode = "ITEM-002", quantity = 1, unitOfMeasureCode = "EA" } },
            ["idempotencyKey"] = "ebom-001",
        },
        "/api/business-console/v1/engineering/manufacturing-boms/release" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["bomCode"] = "MBOM-001",
            ["revision"] = "A",
            ["skuCode"] = "SKU-001",
            ["engineeringBomCode"] = "EBOM-001",
            ["engineeringBomRevision"] = "A",
            ["effectiveDate"] = "2026-06-01",
            ["materialLines"] = new[] { new { skuCode = "RM-001", quantity = 1, unitOfMeasureCode = "EA", scrapRate = 0 } },
            ["recipeLines"] = Array.Empty<object>(),
            ["idempotencyKey"] = "mbom-001",
        },
        "/api/business-console/v1/engineering/routings/release" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["routingCode"] = "RTG-001",
            ["revision"] = "A",
            ["skuCode"] = "SKU-001",
            ["effectiveDate"] = "2026-06-01",
            ["operations"] = new[] { new { sequence = 10, workCenterCode = "WC-001", operationCode = "assembly", operationName = "装配", standardMinutes = 15 } },
            ["idempotencyKey"] = "routing-001",
        },
        "/api/business-console/v1/engineering/standard-operations" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["operationCode"] = "OP-001",
            ["operationName"] = "Assembly",
            ["defaultWorkCenterCode"] = "WC-001",
            ["standardSetupMinutes"] = 5,
            ["standardRunMinutes"] = 15,
            ["controlKey"] = "INHOUSE",
            ["requiresReporting"] = true,
            ["requiresQualityInspection"] = false,
            ["isOutsourced"] = false,
            ["description"] = "Assembly operation",
        },
        "/api/business-console/v1/engineering/standard-operations/OP-001" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["operationCode"] = "OP-001",
            ["operationName"] = "Assembly",
            ["defaultWorkCenterCode"] = "WC-002",
            ["standardSetupMinutes"] = 6,
            ["standardRunMinutes"] = 18,
            ["controlKey"] = "INHOUSE",
            ["requiresReporting"] = true,
            ["requiresQualityInspection"] = true,
            ["isOutsourced"] = false,
            ["description"] = "Updated assembly operation",
        },
        "/api/business-console/v1/engineering/standard-operations/OP-001/archive" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["operationCode"] = "OP-001",
            ["reason"] = "Superseded",
        },
        "/api/business-console/v1/engineering/engineering-changes/release" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["changeNumber"] = "ECO-001",
            ["reason"] = "Initial release",
            ["approvalReferenceId"] = "approval-001",
            ["effectiveDate"] = "2026-06-01",
            ["affectedVersions"] = new[] { new { versionKind = "mbom", versionId = "MBOM-001:A" } },
            ["idempotencyKey"] = "eco-001",
        },
        "/api/business-console/v1/engineering/production-versions" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["skuCode"] = "SKU-001",
            ["mbomVersionId"] = "MBOM-001:A",
            ["routingVersionId"] = "RTG-001:A",
            ["validFrom"] = "2026-06-01",
            ["validTo"] = null,
            ["lotSizeMin"] = 1,
            ["lotSizeMax"] = 100,
            ["priority"] = 10,
            ["isDefault"] = true,
        },
        "/api/business-console/v1/engineering/production-versions/pv-001" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["productionVersionId"] = "pv-001",
            ["mbomVersionId"] = "MBOM-001:B",
            ["routingVersionId"] = "RTG-001:B",
            ["validFrom"] = "2026-07-01",
            ["validTo"] = null,
            ["lotSizeMin"] = 1,
            ["lotSizeMax"] = 100,
            ["priority"] = 20,
            ["isDefault"] = true,
        },
        "/api/business-console/v1/engineering/production-versions/pv-001/archive" => new()
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["productionVersionId"] = "pv-001",
            ["reason"] = "Superseded",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown ProductEngineering write path."),
    };
}
