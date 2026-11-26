using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Warehouse3PL.Domain.Entities.Facilities;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Entities.Security;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence;

internal sealed record PendingConfigurationScopeMutation(
    ConfigurationScopeType ScopeType,
    string ScopeKey,
    string EntityType,
    string EntityId);

/// <summary>
/// Maps modified, inserted, or deleted MDM configuration entities in an EF Core ChangeTracker
/// to deterministic, lock-ordered <see cref="ConfigurationScopeEpoch"/> partitions.
/// </summary>
internal static class ConfigurationScopeMapper
{
    public const string GlobalScopeKey = "GLOBAL";

    public static IReadOnlyList<PendingConfigurationScopeMutation> ExtractImpactedScopes(ChangeTracker changeTracker)
    {
        var map = new Dictionary<(ConfigurationScopeType ScopeType, string ScopeKey), PendingConfigurationScopeMutation>();

        void Register(ConfigurationScopeType scopeType, string scopeKey, string entityType, string entityId)
        {
            if (string.IsNullOrWhiteSpace(scopeKey)) return;
            var normKey = scopeKey.Trim().ToUpperInvariant();
            if (normKey.Length > 120) normKey = normKey[..120];
            var key = (scopeType, normKey);
            map.TryAdd(key, new PendingConfigurationScopeMutation(scopeType, normKey, entityType, entityId));
        }

        foreach (var entry in changeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case ConfigurationScopeEpoch:
                case ConfigurationInvalidationEvent:
                    continue;

                case CountryCompliancePolicy policy:
                    Register(ConfigurationScopeType.Country, policy.CountryCode, nameof(CountryCompliancePolicy), policy.Id.ToString("D"));
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(CountryCompliancePolicy), policy.Id.ToString("D"));
                    break;

                case LegalEntity legalEntity:
                    Register(ConfigurationScopeType.LegalEntity, legalEntity.Id.ToString("D"), nameof(LegalEntity), legalEntity.Id.ToString("D"));
                    Register(ConfigurationScopeType.Country, legalEntity.CountryCode, nameof(LegalEntity), legalEntity.Id.ToString("D"));
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(LegalEntity), legalEntity.Id.ToString("D"));
                    break;

                case FxRateRecord fxRate:
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(FxRateRecord), fxRate.Id.ToString("D"));
                    break;

                case CurrencyMaster currency:
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(CurrencyMaster), currency.CurrencyCode);
                    break;

                case UomDefinition uomDef:
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(UomDefinition), uomDef.UomCode);
                    break;

                case UomConversion uomConv:
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(UomConversion), $"{uomConv.FromUomCode}->{uomConv.ToUomCode}");
                    break;

                case FacilityCalendar facCal:
                    Register(ConfigurationScopeType.Facility, facCal.FacilityId.ToString("D"), nameof(FacilityCalendar), facCal.Id.ToString("D"));
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(FacilityCalendar), facCal.Id.ToString("D"));
                    break;

                case HolidayCalendar holiday:
                    if (holiday.FacilityId.HasValue && holiday.FacilityId.Value != Guid.Empty)
                    {
                        Register(ConfigurationScopeType.Facility, holiday.FacilityId.Value.ToString("D"), nameof(HolidayCalendar), holiday.Id.ToString("D"));
                    }
                    else
                    {
                        Register(ConfigurationScopeType.Country, holiday.CountryCode, nameof(HolidayCalendar), holiday.Id.ToString("D"));
                    }
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(HolidayCalendar), holiday.Id.ToString("D"));
                    break;

                case Facility facility:
                    Register(ConfigurationScopeType.Facility, facility.Id.ToString("D"), nameof(Facility), facility.Id.ToString("D"));
                    if (!string.IsNullOrWhiteSpace(facility.CountryCode))
                    {
                        Register(ConfigurationScopeType.Country, NormalizeCountry(facility.CountryCode), nameof(Facility), facility.Id.ToString("D"));
                    }
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(Facility), facility.Id.ToString("D"));
                    break;

                case ClientFacilityAccess access:
                    Register(ConfigurationScopeType.Client, access.ClientId.ToString("D"), nameof(ClientFacilityAccess), access.Id.ToString("D"));
                    Register(ConfigurationScopeType.Facility, access.FacilityId.ToString("D"), nameof(ClientFacilityAccess), access.Id.ToString("D"));
                    break;

                case Client client when entry.State == EntityState.Modified:
                    Register(ConfigurationScopeType.Client, client.Id.ToString("D"), nameof(Client), client.Id.ToString("D"));
                    break;
            }
        }

        // Strict deterministic sort order avoids multi-scope lock inversion deadlocks on concurrent writes.
        return map.Values
            .OrderBy(v => (int)v.ScopeType)
            .ThenBy(v => v.ScopeKey, StringComparer.Ordinal)
            .ToList();
    }

    private static string NormalizeCountry(string raw)
    {
        var c = raw.Trim().ToUpperInvariant();
        return c switch
        {
            "USA" => "US",
            "NPL" => "NP",
            _ => c.Length >= 2 ? c : GlobalScopeKey
        };
    }
}
