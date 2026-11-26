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
/// For <see cref="EntityState.Modified"/> entities whose scope keys changed, both original and destination
/// scopes are invalidated atomically.
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

        void RegisterGuidBothStates(
            EntityEntry entry,
            ConfigurationScopeType scopeType,
            string propName,
            Guid? currentVal,
            string entityType,
            string entityId)
        {
            if (currentVal.HasValue && currentVal.Value != Guid.Empty)
            {
                Register(scopeType, currentVal.Value.ToString("D"), entityType, entityId);
            }

            if (entry.State == EntityState.Modified)
            {
                var origVal = entry.OriginalValues[propName] as Guid?;
                if (origVal.HasValue && origVal.Value != Guid.Empty && origVal != currentVal)
                {
                    Register(scopeType, origVal.Value.ToString("D"), entityType, entityId);
                }
            }
        }

        void RegisterCountryBothStates(
            EntityEntry entry,
            string propName,
            string? currentVal,
            string entityType,
            string entityId)
        {
            if (!string.IsNullOrWhiteSpace(currentVal))
            {
                Register(ConfigurationScopeType.Country, NormalizeCountry(currentVal), entityType, entityId);
            }

            if (entry.State == EntityState.Modified)
            {
                var origVal = entry.OriginalValues[propName] as string;
                if (!string.IsNullOrWhiteSpace(origVal))
                {
                    var normOrig = NormalizeCountry(origVal);
                    if (string.IsNullOrWhiteSpace(currentVal) || !string.Equals(normOrig, NormalizeCountry(currentVal), StringComparison.Ordinal))
                    {
                        Register(ConfigurationScopeType.Country, normOrig, entityType, entityId);
                    }
                }
            }
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
                    RegisterCountryBothStates(entry, nameof(CountryCompliancePolicy.CountryCode), policy.CountryCode, nameof(CountryCompliancePolicy), policy.Id.ToString("D"));
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(CountryCompliancePolicy), policy.Id.ToString("D"));
                    break;

                case LegalEntity legalEntity:
                    Register(ConfigurationScopeType.LegalEntity, legalEntity.Id.ToString("D"), nameof(LegalEntity), legalEntity.Id.ToString("D"));
                    RegisterCountryBothStates(entry, nameof(LegalEntity.CountryCode), legalEntity.CountryCode, nameof(LegalEntity), legalEntity.Id.ToString("D"));
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

                case CountryProfile countryProfile:
                    RegisterCountryBothStates(entry, nameof(CountryProfile.CountryCode), countryProfile.CountryCode, nameof(CountryProfile), countryProfile.CountryCode);
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(CountryProfile), countryProfile.CountryCode);
                    break;

                case CountryNormalizationAlias alias:
                    RegisterCountryBothStates(entry, nameof(CountryNormalizationAlias.CountryCode), alias.CountryCode, nameof(CountryNormalizationAlias), alias.AliasKey);
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(CountryNormalizationAlias), alias.AliasKey);
                    break;

                case FacilityCalendar facCal:
                    RegisterGuidBothStates(entry, ConfigurationScopeType.Facility, nameof(FacilityCalendar.FacilityId), facCal.FacilityId, nameof(FacilityCalendar), facCal.Id.ToString("D"));
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(FacilityCalendar), facCal.Id.ToString("D"));
                    break;

                case HolidayCalendar holiday:
                    RegisterGuidBothStates(entry, ConfigurationScopeType.Facility, nameof(HolidayCalendar.FacilityId), holiday.FacilityId, nameof(HolidayCalendar), holiday.Id.ToString("D"));
                    // When HolidayScope or CountryCode changes (or is National/Regional), ensure both original and destination Country scopes invalidate
                    var origHolidayFac = entry.State == EntityState.Modified
                        ? entry.OriginalValues[nameof(HolidayCalendar.FacilityId)] as Guid?
                        : null;
                    bool wasCountryScoped = entry.State == EntityState.Modified && (!origHolidayFac.HasValue || origHolidayFac.Value == Guid.Empty);
                    if (!holiday.FacilityId.HasValue || holiday.FacilityId.Value == Guid.Empty || wasCountryScoped)
                    {
                        RegisterCountryBothStates(entry, nameof(HolidayCalendar.CountryCode), holiday.CountryCode, nameof(HolidayCalendar), holiday.Id.ToString("D"));
                    }
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(HolidayCalendar), holiday.Id.ToString("D"));
                    break;

                case Facility facility:
                    Register(ConfigurationScopeType.Facility, facility.Id.ToString("D"), nameof(Facility), facility.Id.ToString("D"));
                    RegisterCountryBothStates(entry, nameof(Facility.CountryCode), facility.CountryCode, nameof(Facility), facility.Id.ToString("D"));
                    Register(ConfigurationScopeType.Global, GlobalScopeKey, nameof(Facility), facility.Id.ToString("D"));
                    break;

                case ClientFacilityAccess access:
                    RegisterGuidBothStates(entry, ConfigurationScopeType.Client, nameof(ClientFacilityAccess.ClientId), access.ClientId, nameof(ClientFacilityAccess), access.Id.ToString("D"));
                    RegisterGuidBothStates(entry, ConfigurationScopeType.Facility, nameof(ClientFacilityAccess.FacilityId), access.FacilityId, nameof(ClientFacilityAccess), access.Id.ToString("D"));
                    break;

                case Client client:
                    // Handle Added, Modified, and Deleted/Soft-Deleted Client configuration lifecycle uniformly
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
