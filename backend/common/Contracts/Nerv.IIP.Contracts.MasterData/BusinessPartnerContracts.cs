namespace Nerv.IIP.Contracts.MasterData;

public sealed record BusinessPartnerCreditProfile(
    string OrganizationId,
    string EnvironmentId,
    string CustomerCode,
    decimal CreditLimit,
    string CurrencyCode,
    string SnapshotVersion);
