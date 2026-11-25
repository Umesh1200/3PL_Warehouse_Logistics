using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Warehouse3PL.Application.Common.Models;
using Warehouse3PL.Domain.Entities.Reference;
using Warehouse3PL.Domain.Enums;

namespace Warehouse3PL.Infrastructure.Persistence.Seed;

/// <summary>
/// Authoritative, idempotent reference data and Draft-only statutory policy seeder for MDM-1.1.
/// Safe to invoke on empty databases or repeatedly on existing databases without overwriting approved policies.
/// </summary>
public static class ReferenceCatalogSeeder
{
    public const string PendingNoticeMarker = "PENDING_VERIFIED_STATUTORY_NOTICE_REVIEW";

    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        await SeedCountryProfilesAsync(db, cancellationToken);
        await SeedTimeZonesAsync(db, cancellationToken);
        await SeedAdministrativeDivisionsAsync(db, cancellationToken);
        await SeedDraftCompliancePoliciesAsync(db, cancellationToken);

        bool hasMdm13Schema = !db.Database.IsRelational();
        if (!hasMdm13Schema)
        {
            var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
            hasMdm13Schema = applied.Any(m => m.Contains("MDM1_3", StringComparison.OrdinalIgnoreCase));
        }

        if (hasMdm13Schema)
        {
            await SeedCurrenciesAsync(db, cancellationToken);
            await SeedUomDefinitionsAsync(db, cancellationToken);
            await SeedUomConversionsAsync(db, cancellationToken);
        }
    }

    public static Guid CreateDeterministicGuid(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"NEXUS3PL:MDM11:{key}"));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static async Task SeedCountryProfilesAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var profiles = new[]
        {
            CountryProfile.Create(
                countryCode: "US",
                isoAlpha3Code: "USA",
                numericCode: "840",
                name: "United States",
                defaultCurrencyCode: "USD",
                primaryTimeZoneId: "America/Chicago",
                addressSchemaType: AddressSchemaType.UsStateZip,
                postalCodeRegex: @"^\d{5}(-\d{4})?$",
                legacyPostalCodeRegex: null,
                primaryCalendarSystem: PrimaryCalendarSystem.Gregorian,
                fiscalCalendarSystem: FiscalCalendarSystem.Gregorian,
                defaultFiscalYearStartMonth: 1,
                seedVersion: 1),
            CountryProfile.Create(
                countryCode: "NP",
                isoAlpha3Code: "NPL",
                numericCode: "524",
                name: "Nepal",
                defaultCurrencyCode: "NPR",
                primaryTimeZoneId: "Asia/Kathmandu",
                addressSchemaType: AddressSchemaType.NepalProvinceDistrictPalika,
                postalCodeRegex: @"^\d{5}(\d{2})?$",
                legacyPostalCodeRegex: @"^\d{5}$",
                primaryCalendarSystem: PrimaryCalendarSystem.BikramSambatDual,
                fiscalCalendarSystem: FiscalCalendarSystem.BikramSambat,
                defaultFiscalYearStartMonth: 4, // BS month 4 = Shrawan (distinct from Gregorian April)
                seedVersion: 1)
        };

        var existingCodes = await db.CountryProfiles
            .AsNoTracking()
            .Select(c => c.CountryCode)
            .ToListAsync(cancellationToken);
        var existingSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            if (!existingSet.Contains(profile.CountryCode))
            {
                db.CountryProfiles.Add(profile);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedTimeZonesAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var zones = new[]
        {
            new TimeZoneProfile { TimeZoneId = "UTC", DisplayName = "Coordinated Universal Time", ObservesDst = false, CountryCode = null, IsActive = true },
            new TimeZoneProfile { TimeZoneId = "Asia/Kathmandu", DisplayName = "Nepal Time (Asia/Kathmandu)", ObservesDst = false, CountryCode = "NP", IsActive = true },
            new TimeZoneProfile { TimeZoneId = "America/New_York", DisplayName = "Eastern Time (US)", ObservesDst = true, CountryCode = "US", IsActive = true },
            new TimeZoneProfile { TimeZoneId = "America/Chicago", DisplayName = "Central Time (US)", ObservesDst = true, CountryCode = "US", IsActive = true },
            new TimeZoneProfile { TimeZoneId = "America/Denver", DisplayName = "Mountain Time (US)", ObservesDst = true, CountryCode = "US", IsActive = true },
            new TimeZoneProfile { TimeZoneId = "America/Phoenix", DisplayName = "Mountain Standard Time - Arizona (US)", ObservesDst = false, CountryCode = "US", IsActive = true },
            new TimeZoneProfile { TimeZoneId = "America/Los_Angeles", DisplayName = "Pacific Time (US)", ObservesDst = true, CountryCode = "US", IsActive = true },
            new TimeZoneProfile { TimeZoneId = "America/Anchorage", DisplayName = "Alaska Time (US)", ObservesDst = true, CountryCode = "US", IsActive = true },
            new TimeZoneProfile { TimeZoneId = "Pacific/Honolulu", DisplayName = "Hawaii-Aleutian Standard Time (US)", ObservesDst = false, CountryCode = "US", IsActive = true }
        };

        var existingIds = await db.TimeZoneProfiles
            .AsNoTracking()
            .Select(tz => tz.TimeZoneId)
            .ToListAsync(cancellationToken);
        var existingSet = existingIds.ToHashSet(StringComparer.Ordinal);

        foreach (var tz in zones)
        {
            if (!existingSet.Contains(tz.TimeZoneId))
            {
                db.TimeZoneProfiles.Add(tz);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedAdministrativeDivisionsAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var usStates = new (string Code, string Name)[]
        {
            ("AL", "Alabama"), ("AK", "Alaska"), ("AZ", "Arizona"), ("AR", "Arkansas"),
            ("CA", "California"), ("CO", "Colorado"), ("CT", "Connecticut"), ("DE", "Delaware"),
            ("DC", "District of Columbia"), ("FL", "Florida"), ("GA", "Georgia"), ("HI", "Hawaii"),
            ("ID", "Idaho"), ("IL", "Illinois"), ("IN", "Indiana"), ("IA", "Iowa"),
            ("KS", "Kansas"), ("KY", "Kentucky"), ("LA", "Louisiana"), ("ME", "Maine"),
            ("MD", "Maryland"), ("MA", "Massachusetts"), ("MI", "Michigan"), ("MN", "Minnesota"),
            ("MS", "Mississippi"), ("MO", "Missouri"), ("MT", "Montana"), ("NE", "Nebraska"),
            ("NV", "Nevada"), ("NH", "New Hampshire"), ("NJ", "New Jersey"), ("NM", "New Mexico"),
            ("NY", "New York"), ("NC", "North Carolina"), ("ND", "North Dakota"), ("OH", "Ohio"),
            ("OK", "Oklahoma"), ("OR", "Oregon"), ("PA", "Pennsylvania"), ("RI", "Rhode Island"),
            ("SC", "South Carolina"), ("SD", "South Dakota"), ("TN", "Tennessee"), ("TX", "Texas"),
            ("UT", "Utah"), ("VT", "Vermont"), ("VA", "Virginia"), ("WA", "Washington"),
            ("WV", "West Virginia"), ("WI", "Wisconsin"), ("WY", "Wyoming")
        };

        var npProvinces = new (string Code, string IsoCode, string Name, string LocalName)[]
        {
            ("P1", "NP-P1", "Koshi", "कोशी"),
            ("P2", "NP-P2", "Madhesh", "मधेश"),
            ("P3", "NP-P3", "Bagmati", "बागमती"),
            ("P4", "NP-P4", "Gandaki", "गण्डकी"),
            ("P5", "NP-P5", "Lumbini", "लुम्बिनी"),
            ("P6", "NP-P6", "Karnali", "कर्णाली"),
            ("P7", "NP-P7", "Sudurpashchim", "सुदूरपश्चिम")
        };

        var desired = new List<AdministrativeDivision>();

        foreach (var (code, name) in usStates)
        {
            desired.Add(new AdministrativeDivision
            {
                Id = CreateDeterministicGuid($"DIV:US:STATE:{code}"),
                CountryCode = "US",
                ParentDivisionId = null,
                DivisionLevel = AdministrativeDivisionLevel.State,
                IsoCode = $"US-{code}",
                Code = code,
                Name = name,
                IsActive = true
            });
        }

        foreach (var (code, isoCode, name, localName) in npProvinces)
        {
            desired.Add(new AdministrativeDivision
            {
                Id = CreateDeterministicGuid($"DIV:NP:PROVINCE:{code}"),
                CountryCode = "NP",
                ParentDivisionId = null,
                DivisionLevel = AdministrativeDivisionLevel.Province,
                IsoCode = isoCode,
                Code = code,
                Name = name,
                LocalName = localName,
                IsActive = true
            });
        }

        // Representative Nepal Province -> District -> Palika hierarchy entries
        var bagmatiId = CreateDeterministicGuid("DIV:NP:PROVINCE:P3");
        var gandakiId = CreateDeterministicGuid("DIV:NP:PROVINCE:P4");
        var koshiId = CreateDeterministicGuid("DIV:NP:PROVINCE:P1");

        var kathmanduDistrictId = CreateDeterministicGuid("DIV:NP:DISTRICT:KTM");
        var lalitpurDistrictId = CreateDeterministicGuid("DIV:NP:DISTRICT:LAL");
        var bhaktapurDistrictId = CreateDeterministicGuid("DIV:NP:DISTRICT:BKT");
        var kaskiDistrictId = CreateDeterministicGuid("DIV:NP:DISTRICT:KAS");
        var morangDistrictId = CreateDeterministicGuid("DIV:NP:DISTRICT:MOR");

        desired.AddRange(
        [
            new AdministrativeDivision
            {
                Id = kathmanduDistrictId, CountryCode = "NP", ParentDivisionId = bagmatiId,
                DivisionLevel = AdministrativeDivisionLevel.District, Code = "KTM", Name = "Kathmandu", LocalName = "काठमाडौं", PostalPrefix = "446", IsActive = true
            },
            new AdministrativeDivision
            {
                Id = lalitpurDistrictId, CountryCode = "NP", ParentDivisionId = bagmatiId,
                DivisionLevel = AdministrativeDivisionLevel.District, Code = "LAL", Name = "Lalitpur", LocalName = "ललितपुर", PostalPrefix = "447", IsActive = true
            },
            new AdministrativeDivision
            {
                Id = bhaktapurDistrictId, CountryCode = "NP", ParentDivisionId = bagmatiId,
                DivisionLevel = AdministrativeDivisionLevel.District, Code = "BKT", Name = "Bhaktapur", LocalName = "भक्तपुर", PostalPrefix = "448", IsActive = true
            },
            new AdministrativeDivision
            {
                Id = kaskiDistrictId, CountryCode = "NP", ParentDivisionId = gandakiId,
                DivisionLevel = AdministrativeDivisionLevel.District, Code = "KAS", Name = "Kaski", LocalName = "कास्की", PostalPrefix = "337", IsActive = true
            },
            new AdministrativeDivision
            {
                Id = morangDistrictId, CountryCode = "NP", ParentDivisionId = koshiId,
                DivisionLevel = AdministrativeDivisionLevel.District, Code = "MOR", Name = "Morang", LocalName = "मोरङ", PostalPrefix = "566", IsActive = true
            },
            new AdministrativeDivision
            {
                Id = CreateDeterministicGuid("DIV:NP:PALIKA:KTM-KMC"), CountryCode = "NP", ParentDivisionId = kathmanduDistrictId,
                DivisionLevel = AdministrativeDivisionLevel.LocalLevelPalika, Code = "KTM-KMC", Name = "Kathmandu Metropolitan City", LocalName = "काठमाडौं महानगरपालिका", PostalPrefix = "44600", IsActive = true
            },
            new AdministrativeDivision
            {
                Id = CreateDeterministicGuid("DIV:NP:PALIKA:LAL-LMC"), CountryCode = "NP", ParentDivisionId = lalitpurDistrictId,
                DivisionLevel = AdministrativeDivisionLevel.LocalLevelPalika, Code = "LAL-LMC", Name = "Lalitpur Metropolitan City", LocalName = "ललितपुर महानगरपालिका", PostalPrefix = "44700", IsActive = true
            },
            new AdministrativeDivision
            {
                Id = CreateDeterministicGuid("DIV:NP:PALIKA:KAS-PMC"), CountryCode = "NP", ParentDivisionId = kaskiDistrictId,
                DivisionLevel = AdministrativeDivisionLevel.LocalLevelPalika, Code = "KAS-PMC", Name = "Pokhara Metropolitan City", LocalName = "पोखरा महानगरपालिका", PostalPrefix = "33700", IsActive = true
            },
            new AdministrativeDivision
            {
                Id = CreateDeterministicGuid("DIV:NP:PALIKA:MOR-BMC"), CountryCode = "NP", ParentDivisionId = morangDistrictId,
                DivisionLevel = AdministrativeDivisionLevel.LocalLevelPalika, Code = "MOR-BMC", Name = "Biratnagar Metropolitan City", LocalName = "विराटनगर महानगरपालिका", PostalPrefix = "56613", IsActive = true
            }
        ]);

        var existingKeys = await db.AdministrativeDivisions
            .AsNoTracking()
            .Select(d => new { d.CountryCode, d.DivisionLevel, d.Code })
            .ToListAsync(cancellationToken);

        var keySet = existingKeys
            .Select(k => $"{k.CountryCode}|{k.DivisionLevel}|{k.Code}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var division in desired)
        {
            var key = $"{division.CountryCode}|{division.DivisionLevel}|{division.Code}";
            if (!keySet.Contains(key))
            {
                db.AdministrativeDivisions.Add(division);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedDraftCompliancePoliciesAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var effectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var vctsPredicate = JsonSerializer.Serialize(new ComplianceApplicabilityPredicate
        {
            RequiredBusinessClassifications = ["B2BWholesale", "ImporterDistributor"],
            RequiredOrderCategories = ["CommercialWholesaleDispatch", "CrossDistrictTransfer"],
            RequiredCargoClasses = ["CommercialGoods"],
            TreatUnlistedValuesAsIndeterminate = true,
            Notes = "Draft baseline predicate requiring verified DRI statutory notice review before publication."
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var cbmsPredicate = JsonSerializer.Serialize(new ComplianceApplicabilityPredicate
        {
            RequiresTaxpayerMandateFlag = true,
            TreatUnlistedValuesAsIndeterminate = true,
            Notes = "Draft baseline predicate conditioned on taxpayer CBMS mandate flag; non-enforcing until approved."
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var vatPredicate = JsonSerializer.Serialize(new ComplianceApplicabilityPredicate
        {
            RequiresTaxpayerMandateFlag = true,
            TreatUnlistedValuesAsIndeterminate = true,
            Notes = "Draft Nepal VAT standard policy template; non-enforcing until verified Staff approval."
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // MDM11-D01: All seeded regulatory policies MUST initialize strictly as Draft & IsActive = false.
        // OfficialNoticeCitation contains PendingNoticeMarker so accidental bare approval without
        // validated statutory citation & evidence attestation fails closed.
        var drafts = new[]
        {
            CountryCompliancePolicy.CreateDraft(
                countryCode: "NP",
                policyCode: "NP-DRI-VCTS",
                version: 1,
                title: "Nepal DRI Vehicle and Consignment Tracking System (VCTS)",
                statutorySourceRef: "Revenue Leakage (Investigation and Control) Act & DRI VCTS Directive",
                officialNoticeCitation: PendingNoticeMarker,
                applicabilityPredicateJson: vctsPredicate,
                enforcementMode: ComplianceEnforcementMode.ConditionalRequired,
                effectiveFromUtc: effectiveFrom,
                effectiveToUtc: null,
                id: CreateDeterministicGuid("POLICY:NP:NP-DRI-VCTS:V1")),
            CountryCompliancePolicy.CreateDraft(
                countryCode: "NP",
                policyCode: "NP-IRD-CBMS",
                version: 1,
                title: "Nepal IRD Central Billing Monitoring System (CBMS)",
                statutorySourceRef: "Inland Revenue Department Electronic Billing Directive",
                officialNoticeCitation: PendingNoticeMarker,
                applicabilityPredicateJson: cbmsPredicate,
                enforcementMode: ComplianceEnforcementMode.ConditionalRequired,
                effectiveFromUtc: effectiveFrom,
                effectiveToUtc: null,
                id: CreateDeterministicGuid("POLICY:NP:NP-IRD-CBMS:V1")),
            CountryCompliancePolicy.CreateDraft(
                countryCode: "NP",
                policyCode: "NP-IRD-VAT",
                version: 1,
                title: "Nepal Value Added Tax (VAT) Statutory Invoicing Policy",
                statutorySourceRef: "Value Added Tax Act, 2052 (Nepal)",
                officialNoticeCitation: PendingNoticeMarker,
                applicabilityPredicateJson: vatPredicate,
                enforcementMode: ComplianceEnforcementMode.ConditionalRequired,
                effectiveFromUtc: effectiveFrom,
                effectiveToUtc: null,
                id: CreateDeterministicGuid("POLICY:NP:NP-IRD-VAT:V1"))
        };

        var existingVersions = await db.CountryCompliancePolicies
            .AsNoTracking()
            .Select(p => new { p.CountryCode, p.PolicyCode, p.Version })
            .ToListAsync(cancellationToken);

        var versionSet = existingVersions
            .Select(v => $"{v.CountryCode}|{v.PolicyCode}|{v.Version}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var draft in drafts)
        {
            var key = $"{draft.CountryCode}|{draft.PolicyCode}|{draft.Version}";
            if (!versionSet.Contains(key))
            {
                db.CountryCompliancePolicies.Add(draft);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCurrenciesAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var desired = new[]
        {
            CurrencyMaster.Create("USD", "840", "US Dollar", "$", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("NPR", "524", "Nepalese Rupee", "रू", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("INR", "356", "Indian Rupee", "₹", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("EUR", "978", "Euro", "€", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("GBP", "826", "Pound Sterling", "£", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("CAD", "124", "Canadian Dollar", "CA$", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("AUD", "036", "Australian Dollar", "A$", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("JPY", "392", "Japanese Yen", "¥", 0, symbolBeforeAmount: true),
            CurrencyMaster.Create("KWD", "414", "Kuwaiti Dinar", "KWD", 3, symbolBeforeAmount: true),
            CurrencyMaster.Create("SGD", "702", "Singapore Dollar", "S$", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("CNY", "156", "Yuan Renminbi", "CN¥", 2, symbolBeforeAmount: true),
            CurrencyMaster.Create("AED", "784", "UAE Dirham", "AED", 2, symbolBeforeAmount: true)
        };

        var existingCodes = (await db.Currencies
            .AsNoTracking()
            .Select(c => c.CurrencyCode)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var currency in desired)
        {
            if (!existingCodes.Contains(currency.CurrencyCode))
            {
                db.Currencies.Add(currency);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedUomDefinitionsAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var desired = new[]
        {
            // Mass (canonical base: KG matching canonical WeightKg)
            new UomDefinition { UomCode = "KG", UneceRec20Code = "KGM", Name = "Kilogram", Dimension = UomDimension.Mass, SystemOfMeasure = UomSystemOfMeasure.SI, IsCanonicalBase = true, RoundingPrecision = 4, IsActive = true },
            new UomDefinition { UomCode = "G", UneceRec20Code = "GRM", Name = "Gram", Dimension = UomDimension.Mass, SystemOfMeasure = UomSystemOfMeasure.SI, IsCanonicalBase = false, RoundingPrecision = 4, IsActive = true },
            new UomDefinition { UomCode = "LB", UneceRec20Code = "LBR", Name = "Pound", Dimension = UomDimension.Mass, SystemOfMeasure = UomSystemOfMeasure.Imperial, IsCanonicalBase = false, RoundingPrecision = 4, IsActive = true },
            new UomDefinition { UomCode = "OZ", UneceRec20Code = "ONZ", Name = "Ounce", Dimension = UomDimension.Mass, SystemOfMeasure = UomSystemOfMeasure.Imperial, IsCanonicalBase = false, RoundingPrecision = 4, IsActive = true },

            // Length (canonical base: CM matching canonical LengthCm/WidthCm/HeightCm)
            new UomDefinition { UomCode = "CM", UneceRec20Code = "CMT", Name = "Centimetre", Dimension = UomDimension.Length, SystemOfMeasure = UomSystemOfMeasure.SI, IsCanonicalBase = true, RoundingPrecision = 4, IsActive = true },
            new UomDefinition { UomCode = "M", UneceRec20Code = "MTR", Name = "Metre", Dimension = UomDimension.Length, SystemOfMeasure = UomSystemOfMeasure.SI, IsCanonicalBase = false, RoundingPrecision = 4, IsActive = true },
            new UomDefinition { UomCode = "IN", UneceRec20Code = "INH", Name = "Inch", Dimension = UomDimension.Length, SystemOfMeasure = UomSystemOfMeasure.Imperial, IsCanonicalBase = false, RoundingPrecision = 4, IsActive = true },
            new UomDefinition { UomCode = "FT", UneceRec20Code = "FOT", Name = "Foot", Dimension = UomDimension.Length, SystemOfMeasure = UomSystemOfMeasure.Imperial, IsCanonicalBase = false, RoundingPrecision = 4, IsActive = true },

            // Volume (canonical base: CM3 matching canonical VolumeCm3)
            new UomDefinition { UomCode = "CM3", UneceRec20Code = "CMQ", Name = "Cubic Centimetre", Dimension = UomDimension.Volume, SystemOfMeasure = UomSystemOfMeasure.SI, IsCanonicalBase = true, RoundingPrecision = 4, IsActive = true },
            new UomDefinition { UomCode = "M3", UneceRec20Code = "MTQ", Name = "Cubic Metre", Dimension = UomDimension.Volume, SystemOfMeasure = UomSystemOfMeasure.SI, IsCanonicalBase = false, RoundingPrecision = 6, IsActive = true },
            new UomDefinition { UomCode = "FT3", UneceRec20Code = "FTQ", Name = "Cubic Foot", Dimension = UomDimension.Volume, SystemOfMeasure = UomSystemOfMeasure.Imperial, IsCanonicalBase = false, RoundingPrecision = 6, IsActive = true },
            new UomDefinition { UomCode = "IN3", UneceRec20Code = "INQ", Name = "Cubic Inch", Dimension = UomDimension.Volume, SystemOfMeasure = UomSystemOfMeasure.Imperial, IsCanonicalBase = false, RoundingPrecision = 4, IsActive = true },

            // Packaging / Count (canonical base: EA; packaging ratios depend on Item PackagingUnit)
            new UomDefinition { UomCode = "EA", UneceRec20Code = "EA", Name = "Each", Dimension = UomDimension.Count, SystemOfMeasure = UomSystemOfMeasure.Packaging, IsCanonicalBase = true, RoundingPrecision = 0, IsActive = true },
            new UomDefinition { UomCode = "CS", UneceRec20Code = "CS", Name = "Case", Dimension = UomDimension.Count, SystemOfMeasure = UomSystemOfMeasure.Packaging, IsCanonicalBase = false, RoundingPrecision = 0, IsActive = true },
            new UomDefinition { UomCode = "PLT", UneceRec20Code = "PF", Name = "Pallet", Dimension = UomDimension.Count, SystemOfMeasure = UomSystemOfMeasure.Packaging, IsCanonicalBase = false, RoundingPrecision = 0, IsActive = true }
        };

        var existingCodes = (await db.UomDefinitions
            .AsNoTracking()
            .Select(d => d.UomCode)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var def in desired)
        {
            if (!existingCodes.Contains(def.UomCode))
            {
                db.UomDefinitions.Add(def);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedUomConversionsAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var desired = new[]
        {
            // Mass
            new UomConversion { FromUomCode = "KG", ToUomCode = "G", Dimension = UomDimension.Mass, ConversionFactor = 1000m, InverseFactor = 0.001m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "LB", ToUomCode = "KG", Dimension = UomDimension.Mass, ConversionFactor = 0.45359237m, InverseFactor = 2.2046226218m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "OZ", ToUomCode = "LB", Dimension = UomDimension.Mass, ConversionFactor = 0.0625m, InverseFactor = 16m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "OZ", ToUomCode = "KG", Dimension = UomDimension.Mass, ConversionFactor = 0.0283495231m, InverseFactor = 35.2739619496m, IsExact = false, IsActive = true },
            new UomConversion { FromUomCode = "OZ", ToUomCode = "G", Dimension = UomDimension.Mass, ConversionFactor = 28.349523125m, InverseFactor = 0.0352739619m, IsExact = true, IsActive = true },

            // Length
            new UomConversion { FromUomCode = "M", ToUomCode = "CM", Dimension = UomDimension.Length, ConversionFactor = 100m, InverseFactor = 0.01m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "IN", ToUomCode = "CM", Dimension = UomDimension.Length, ConversionFactor = 2.54m, InverseFactor = 0.3937007874m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "FT", ToUomCode = "CM", Dimension = UomDimension.Length, ConversionFactor = 30.48m, InverseFactor = 0.0328083990m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "FT", ToUomCode = "IN", Dimension = UomDimension.Length, ConversionFactor = 12m, InverseFactor = 0.0833333333m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "FT", ToUomCode = "M", Dimension = UomDimension.Length, ConversionFactor = 0.3048m, InverseFactor = 3.2808398950m, IsExact = true, IsActive = true },

            // Volume
            new UomConversion { FromUomCode = "M3", ToUomCode = "CM3", Dimension = UomDimension.Volume, ConversionFactor = 1000000m, InverseFactor = 0.000001m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "IN3", ToUomCode = "CM3", Dimension = UomDimension.Volume, ConversionFactor = 16.387064m, InverseFactor = 0.0610237441m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "FT3", ToUomCode = "CM3", Dimension = UomDimension.Volume, ConversionFactor = 28316.846592m, InverseFactor = 0.0000353147m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "FT3", ToUomCode = "IN3", Dimension = UomDimension.Volume, ConversionFactor = 1728m, InverseFactor = 0.0005787037m, IsExact = true, IsActive = true },
            new UomConversion { FromUomCode = "FT3", ToUomCode = "M3", Dimension = UomDimension.Volume, ConversionFactor = 0.0283168466m, InverseFactor = 35.3146667215m, IsExact = false, IsActive = true }
        };

        var existingPairs = (await db.UomConversions
            .AsNoTracking()
            .Select(c => new { c.FromUomCode, c.ToUomCode })
            .ToListAsync(cancellationToken))
            .Select(c => $"{c.FromUomCode}|{c.ToUomCode}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var conv in desired)
        {
            var key = $"{conv.FromUomCode}|{conv.ToUomCode}";
            if (!existingPairs.Contains(key))
            {
                db.UomConversions.Add(conv);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
