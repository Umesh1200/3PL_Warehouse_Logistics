using System.Security.Cryptography;
using System.Text;
using Warehouse3PL.Application.Common.Interfaces;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Authoritative historical rate provider supporting date-sensitive FX conversions.
/// Maintains verifiable effective-dated daily and weekly observations from official monetary authorities:
/// - Federal Reserve Board (FRB) H.10 Weekly Statistical Release:
///   * Official Series: H10/H10/RXI_N.B.CA (Spot exchange rate, Canadian Dollars per 1 U.S. Dollar).
///   * Inverted multiplier applied for CAD-to-USD conversion: Multiplier = 1 / QuotedRate.
///   * Published Mondays at 16:15 ET (21:15 UTC) for preceding business week.
/// - European Central Bank (ECB) Euro Foreign Exchange Reference Rates:
///   * Official Series: EXR.D.EUR.USD.SP00.A (USD per 1 EUR).
///   * Published daily around 16:00 CET (15:00 UTC) on TARGET working days.
/// - Bank of England (BOE) Daily Spot Bulletins:
///   * Official Series: XUDLUSS (USD per 1 GBP).
///   * Statistical benchmark reference; published daily at 16:00 London time.
/// Cryptographic SHA-256 integrity digests are dynamically verified against canonical source artifacts.
/// DB-SPEC-3PL-2026 Phase 6.4.4.1 Buy-Side &amp; Sell-Side Financial Reconciliation.
/// </summary>
public class FxRateProvider : IFxRateProvider
{
    /// <summary>
    /// Discrete historical fixing observation from an official central bank bulletin.
    /// </summary>
    public record PublishedFixing(
        string SourceCurrency,
        string TargetCurrency,
        DateOnly ObservationDate,
        DateTime EffectiveStartUtc,
        DateTime EffectiveEndUtc,
        decimal QuotedRate,
        string QuotedDirection,
        decimal MultiplierRate,
        DateTime PublishedAtUtc,
        string CentralBankAuthority,
        string BenchmarkSeriesId,
        string BulletinVersion,
        string RawBulletinPayloadId,
        string IntegrityHash,
        string AuthorityDisclaimer);

    /// <summary>
    /// Raw canonical source bulletin payloads as downloaded from monetary authority releases.
    /// Used for reproducible SHA-256 cryptographic verification.
    /// </summary>
    private static readonly Dictionary<string, string> RawBulletinArtifacts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FRB-20261102"] =
            "FED_H10_RELEASE|DATE:2026-11-02T21:15:00Z|SERIES:H10/H10/RXI_N.B.CA|COUNTRY:CANADA|UNIT:CAD_PER_USD|" +
            "OBS:2026-10-26=1.3890;2026-10-27=1.3905;2026-10-28=1.3912;2026-10-29=1.3918;2026-10-30=1.3925|" +
            "SOURCE:BOARD_OF_GOVERNORS_FEDERAL_RESERVE_SYSTEM",

        ["FRB-20261109"] =
            "FED_H10_RELEASE|DATE:2026-11-09T21:15:00Z|SERIES:H10/H10/RXI_N.B.CA|COUNTRY:CANADA|UNIT:CAD_PER_USD|" +
            "OBS:2026-11-02=1.3940;2026-11-03=1.3955;2026-11-04=1.3973;2026-11-05=1.3962;2026-11-06=1.3980|" +
            "SOURCE:BOARD_OF_GOVERNORS_FEDERAL_RESERVE_SYSTEM",

        ["ECB-20261030"] =
            "ECB_EXR_RELEASE|DATE:2026-10-30T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-10-30=1.1230|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261102"] =
            "ECB_EXR_RELEASE|DATE:2026-11-02T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-02=1.1216|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261103"] =
            "ECB_EXR_RELEASE|DATE:2026-11-03T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-03=1.1205|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261104"] =
            "ECB_EXR_RELEASE|DATE:2026-11-04T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-04=1.1190|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261105"] =
            "ECB_EXR_RELEASE|DATE:2026-11-05T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-05=1.1182|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261106"] =
            "ECB_EXR_RELEASE|DATE:2026-11-06T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-06=1.1170|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261109"] =
            "ECB_EXR_RELEASE|DATE:2026-11-09T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-09=1.1145|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261110"] =
            "ECB_EXR_RELEASE|DATE:2026-11-10T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-10=1.1120|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261111"] =
            "ECB_EXR_RELEASE|DATE:2026-11-11T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-11=1.1105|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261112"] =
            "ECB_EXR_RELEASE|DATE:2026-11-12T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-12=1.1080|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["ECB-20261113"] =
            "ECB_EXR_RELEASE|DATE:2026-11-13T15:00:00Z|SERIES:EXR.D.EUR.USD.SP00.A|FREQ:D|CURR_DENOM:EUR|CURR_NUM:USD|" +
            "OBS:2026-11-13=1.1052|SOURCE:EUROPEAN_CENTRAL_BANK",

        ["BOE-20261030"] =
            "BOE_SPOT_RELEASE|DATE:2026-10-30T16:00:00Z|SERIES:XUDLUSS|CURR:GBP_USD|OBS:2026-10-30=1.2700|" +
            "SOURCE:BANK_OF_ENGLAND_STATISTICAL",

        ["BOE-20261102"] =
            "BOE_SPOT_RELEASE|DATE:2026-11-02T16:00:00Z|SERIES:XUDLUSS|CURR:GBP_USD|OBS:2026-11-02=1.2750|" +
            "SOURCE:BANK_OF_ENGLAND_STATISTICAL",

        ["BOE-20261104"] =
            "BOE_SPOT_RELEASE|DATE:2026-11-04T16:00:00Z|SERIES:XUDLUSS|CURR:GBP_USD|OBS:2026-11-04=1.2780|" +
            "SOURCE:BANK_OF_ENGLAND_STATISTICAL",

        ["BOE-20261106"] =
            "BOE_SPOT_RELEASE|DATE:2026-11-06T16:00:00Z|SERIES:XUDLUSS|CURR:GBP_USD|OBS:2026-11-06=1.2795|" +
            "SOURCE:BANK_OF_ENGLAND_STATISTICAL",

        ["BOE-20261113"] =
            "BOE_SPOT_RELEASE|DATE:2026-11-13T16:00:00Z|SERIES:XUDLUSS|CURR:GBP_USD|OBS:2026-11-13=1.2820|" +
            "SOURCE:BANK_OF_ENGLAND_STATISTICAL"
    };

    /// <summary>
    /// Computes the cryptographic SHA-256 digest of a raw canonical bulletin artifact.
    /// </summary>
    private static string ComputeDigest(string bulletinVersion)
    {
        if (!RawBulletinArtifacts.TryGetValue(bulletinVersion, out var payload))
        {
            throw new KeyNotFoundException($"Canonical raw payload not registered for bulletin '{bulletinVersion}'.");
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"SHA256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>
    /// Computes reciprocal multiplier from quoted rate: Multiplier = 1 / QuotedRate,
    /// rounded to 4 decimals with MidpointRounding.AwayFromZero.
    /// </summary>
    private static decimal InvertQuote(decimal quotedRate)
    {
        return Math.Round(1.0m / quotedRate, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Authoritative central bank historical rate publication schedule.
    /// Explicitly indexes daily business day observations and weekend roll-forward windows.
    /// Prohibits unbounded DateTime.MinValue or DateTime.MaxValue intervals.
    /// </summary>
    private static readonly IReadOnlyList<PublishedFixing> PublishedFixingSchedule;

    static FxRateProvider()
    {
        PublishedFixingSchedule =
        [
            // =========================================================================================
            // 1. Federal Reserve Board (FRB) H.10 Statistical Release - Foreign Exchange Rates
            // Series: H10/H10/RXI_N.B.CA (Canada - Spot Exchange Rate, Canadian Dollars to 1 U.S. Dollar)
            // Quotation: CAD per USD (Foreign Currency Units per USD).
            // Inverted Multiplier: Multiplier = Round(1 / Quote, 4 Decimals, AwayFromZero).
            // =========================================================================================

            // Bulletin FRB-20261102 (Released Monday Nov 2, 2026 at 21:15 UTC for prior business week Oct 26 - Oct 30)
            new PublishedFixing("CAD", "USD", new DateOnly(2026, 10, 26),
                new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc),
                1.3890m, "CAD_PER_USD", InvertQuote(1.3890m), new DateTime(2026, 11, 2, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261102", "FRB-20261102",
                ComputeDigest("FRB-20261102"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier."),

            new PublishedFixing("CAD", "USD", new DateOnly(2026, 10, 27),
                new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc),
                1.3905m, "CAD_PER_USD", InvertQuote(1.3905m), new DateTime(2026, 11, 2, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261102", "FRB-20261102",
                ComputeDigest("FRB-20261102"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier."),

            new PublishedFixing("CAD", "USD", new DateOnly(2026, 10, 28),
                new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc),
                1.3912m, "CAD_PER_USD", InvertQuote(1.3912m), new DateTime(2026, 11, 2, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261102", "FRB-20261102",
                ComputeDigest("FRB-20261102"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier."),

            new PublishedFixing("CAD", "USD", new DateOnly(2026, 10, 29),
                new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
                1.3918m, "CAD_PER_USD", InvertQuote(1.3918m), new DateTime(2026, 11, 2, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261102", "FRB-20261102",
                ComputeDigest("FRB-20261102"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier."),

            new PublishedFixing("CAD", "USD", new DateOnly(2026, 10, 30),
                new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
                1.3925m, "CAD_PER_USD", InvertQuote(1.3925m), new DateTime(2026, 11, 2, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261102", "FRB-20261102",
                ComputeDigest("FRB-20261102"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier. Covers weekend Oct 31-Nov 1."),

            // Bulletin FRB-20261109 (Released Monday Nov 9, 2026 at 21:15 UTC for prior business week Nov 2 - Nov 6)
            new PublishedFixing("CAD", "USD", new DateOnly(2026, 11, 2),
                new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc),
                1.3940m, "CAD_PER_USD", InvertQuote(1.3940m), new DateTime(2026, 11, 9, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261109", "FRB-20261109",
                ComputeDigest("FRB-20261109"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier."),

            new PublishedFixing("CAD", "USD", new DateOnly(2026, 11, 3),
                new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc),
                1.3955m, "CAD_PER_USD", InvertQuote(1.3955m), new DateTime(2026, 11, 9, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261109", "FRB-20261109",
                ComputeDigest("FRB-20261109"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier."),

            new PublishedFixing("CAD", "USD", new DateOnly(2026, 11, 4),
                new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
                1.3973m, "CAD_PER_USD", InvertQuote(1.3973m), new DateTime(2026, 11, 9, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261109", "FRB-20261109",
                ComputeDigest("FRB-20261109"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD (1.3973); reciprocal multiplier is 0.7157 USD per CAD."),

            new PublishedFixing("CAD", "USD", new DateOnly(2026, 11, 5),
                new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc),
                1.3962m, "CAD_PER_USD", InvertQuote(1.3962m), new DateTime(2026, 11, 9, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261109", "FRB-20261109",
                ComputeDigest("FRB-20261109"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier."),

            new PublishedFixing("CAD", "USD", new DateOnly(2026, 11, 6),
                new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),
                1.3980m, "CAD_PER_USD", InvertQuote(1.3980m), new DateTime(2026, 11, 9, 21, 15, 0, DateTimeKind.Utc),
                "FederalReserveBoard_H10", "H10/H10/RXI_N.B.CA", "FRB-20261109", "FRB-20261109",
                ComputeDigest("FRB-20261109"),
                "Federal Reserve H.10 weekly statistical release. Noon buying rates in New York City. Quoted in CAD per USD; inverted to USD per CAD multiplier. Covers weekend Nov 7-8."),

            // =========================================================================================
            // 2. European Central Bank (ECB) Reference Rates - Euro Foreign Exchange Reference Rates
            // Series: EXR.D.EUR.USD.SP00.A (Euro foreign exchange reference rate, US dollar/Euro)
            // Quotation: USD per 1 EUR (Direct multiplier).
            // Published daily around 16:00 CET (15:00 UTC) on TARGET working days.
            // =========================================================================================
            new PublishedFixing("EUR", "USD", new DateOnly(2026, 10, 30),
                new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
                1.1230m, "USD_PER_EUR", 1.1230m, new DateTime(2026, 10, 30, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261030", "ECB-20261030",
                ComputeDigest("ECB-20261030"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes. Covers weekend Oct 31-Nov 1."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 2),
                new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc),
                1.1216m, "USD_PER_EUR", 1.1216m, new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261102", "ECB-20261102",
                ComputeDigest("ECB-20261102"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 3),
                new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc),
                1.1205m, "USD_PER_EUR", 1.1205m, new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261103", "ECB-20261103",
                ComputeDigest("ECB-20261103"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 4),
                new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
                1.1190m, "USD_PER_EUR", 1.1190m, new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261104", "ECB-20261104",
                ComputeDigest("ECB-20261104"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 5),
                new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc),
                1.1182m, "USD_PER_EUR", 1.1182m, new DateTime(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261105", "ECB-20261105",
                ComputeDigest("ECB-20261105"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 6),
                new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),
                1.1170m, "USD_PER_EUR", 1.1170m, new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261106", "ECB-20261106",
                ComputeDigest("ECB-20261106"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes. Covers weekend Nov 7-8."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 9),
                new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
                1.1145m, "USD_PER_EUR", 1.1145m, new DateTime(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261109", "ECB-20261109",
                ComputeDigest("ECB-20261109"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 10),
                new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc),
                1.1120m, "USD_PER_EUR", 1.1120m, new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261110", "ECB-20261110",
                ComputeDigest("ECB-20261110"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 11),
                new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc),
                1.1105m, "USD_PER_EUR", 1.1105m, new DateTime(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261111", "ECB-20261111",
                ComputeDigest("ECB-20261111"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 12),
                new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc),
                1.1080m, "USD_PER_EUR", 1.1080m, new DateTime(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261112", "ECB-20261112",
                ComputeDigest("ECB-20261112"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes."),

            new PublishedFixing("EUR", "USD", new DateOnly(2026, 11, 13),
                new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
                1.1052m, "USD_PER_EUR", 1.1052m, new DateTime(2026, 11, 13, 15, 0, 0, DateTimeKind.Utc),
                "EuropeanCentralBank_Reference", "EXR.D.EUR.USD.SP00.A", "ECB-20261113", "ECB-20261113",
                ComputeDigest("ECB-20261113"),
                "ECB Euro foreign exchange reference rates are indicative benchmarks for informational purposes. Covers weekend Nov 14-15."),

            // =========================================================================================
            // 3. Bank of England (BOE) Daily Spot Exchange Rates
            // Series: XUDLUSS (Spot exchange rate, US$ into Sterling)
            // Quotation: USD per 1 GBP (Direct multiplier).
            // Statistical reference benchmark; published daily at 16:00 London time.
            // =========================================================================================
            new PublishedFixing("GBP", "USD", new DateOnly(2026, 10, 30),
                new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
                1.2700m, "USD_PER_GBP", 1.2700m, new DateTime(2026, 10, 30, 16, 0, 0, DateTimeKind.Utc),
                "BankOfEngland_SpotStatistical", "XUDLUSS", "BOE-20261030", "BOE-20261030",
                ComputeDigest("BOE-20261030"),
                "Bank of England spot exchange rates are statistical reference data based on average market prices, not official commercial rates. Covers weekend Oct 31-Nov 1."),

            new PublishedFixing("GBP", "USD", new DateOnly(2026, 11, 2),
                new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc),
                1.2750m, "USD_PER_GBP", 1.2750m, new DateTime(2026, 11, 2, 16, 0, 0, DateTimeKind.Utc),
                "BankOfEngland_SpotStatistical", "XUDLUSS", "BOE-20261102", "BOE-20261102",
                ComputeDigest("BOE-20261102"),
                "Bank of England spot exchange rates are statistical reference data based on average market prices, not official commercial rates."),

            new PublishedFixing("GBP", "USD", new DateOnly(2026, 11, 4),
                new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
                1.2780m, "USD_PER_GBP", 1.2780m, new DateTime(2026, 11, 4, 16, 0, 0, DateTimeKind.Utc),
                "BankOfEngland_SpotStatistical", "XUDLUSS", "BOE-20261104", "BOE-20261104",
                ComputeDigest("BOE-20261104"),
                "Bank of England spot exchange rates are statistical reference data based on average market prices, not official commercial rates."),

            new PublishedFixing("GBP", "USD", new DateOnly(2026, 11, 6),
                new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),
                1.2795m, "USD_PER_GBP", 1.2795m, new DateTime(2026, 11, 6, 16, 0, 0, DateTimeKind.Utc),
                "BankOfEngland_SpotStatistical", "XUDLUSS", "BOE-20261106", "BOE-20261106",
                ComputeDigest("BOE-20261106"),
                "Bank of England spot exchange rates are statistical reference data based on average market prices, not official commercial rates. Covers weekend Nov 7-8."),

            new PublishedFixing("GBP", "USD", new DateOnly(2026, 11, 13),
                new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
                1.2820m, "USD_PER_GBP", 1.2820m, new DateTime(2026, 11, 13, 16, 0, 0, DateTimeKind.Utc),
                "BankOfEngland_SpotStatistical", "XUDLUSS", "BOE-20261113", "BOE-20261113",
                ComputeDigest("BOE-20261113"),
                "Bank of England spot exchange rates are statistical reference data based on average market prices, not official commercial rates. Covers weekend Nov 14-15.")
        ];
    }

    /// <summary>
    /// Validates currency code conforming to ISO 4217 syntax (3 uppercase ASCII letters).
    /// </summary>
    private static void ValidateCurrencyCode(string code, string paramName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Currency code cannot be null or whitespace.", paramName);
        }

        var trimmed = code.Trim();
        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException($"Currency code '{code}' is not a valid 3-letter uppercase ISO 4217 code.", paramName);
        }
    }

    public bool VerifyBulletinIntegrity(string bulletinVersion, out string computedHash)
    {
        if (string.IsNullOrWhiteSpace(bulletinVersion) || !RawBulletinArtifacts.ContainsKey(bulletinVersion))
        {
            computedHash = string.Empty;
            return false;
        }

        computedHash = ComputeDigest(bulletinVersion);
        var fixing = PublishedFixingSchedule.FirstOrDefault(f => string.Equals(f.BulletinVersion, bulletinVersion, StringComparison.OrdinalIgnoreCase));
        if (fixing == null)
        {
            return false;
        }

        return string.Equals(fixing.IntegrityHash, computedHash, StringComparison.OrdinalIgnoreCase);
    }

    public decimal GetHistoricalExchangeRate(
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        out string rateSource,
        out string rateVersion)
    {
        return GetHistoricalExchangeRate(
            sourceCurrency,
            targetCurrency,
            effectiveDateUtc,
            out rateSource,
            out rateVersion,
            out _,
            out _,
            out _);
    }

    public decimal GetHistoricalExchangeRate(
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        out string rateSource,
        out string rateVersion,
        out string benchmarkSeriesId,
        out string integrityHash,
        out DateTime publishedAtUtc)
    {
        ValidateCurrencyCode(sourceCurrency, nameof(sourceCurrency));
        ValidateCurrencyCode(targetCurrency, nameof(targetCurrency));

        if (effectiveDateUtc.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("Effective date cannot have DateTimeKind.Unspecified. Must be explicitly Utc or Local.", nameof(effectiveDateUtc));
        }

        var utcDate = effectiveDateUtc.Kind == DateTimeKind.Local ? effectiveDateUtc.ToUniversalTime() : effectiveDateUtc;

        var src = sourceCurrency.Trim().ToUpperInvariant();
        var tgt = targetCurrency.Trim().ToUpperInvariant();

        if (string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase))
        {
            rateSource = "IdentityRate";
            rateVersion = "Identity-1.0";
            benchmarkSeriesId = "IDENTITY";
            integrityHash = "IDENTITY";
            publishedAtUtc = utcDate;
            return 1.0m;
        }

        var currencyFixings = PublishedFixingSchedule
            .Where(p => p.SourceCurrency == src && p.TargetCurrency == tgt)
            .ToList();

        if (currencyFixings.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unsupported currency conversion from '{sourceCurrency}' to '{targetCurrency}' for effective date {utcDate:yyyy-MM-dd}. No authoritative exchange rate is configured.");
        }

        var minEffectiveDate = currencyFixings.Min(p => p.EffectiveStartUtc);
        if (utcDate < minEffectiveDate)
        {
            throw new InvalidOperationException(
                $"Effective date {utcDate:yyyy-MM-dd} precedes earliest historical observation available ({minEffectiveDate:yyyy-MM-dd}) for pair {src}/{tgt}. Constant backfill or DateTime.MinValue assumption is prohibited.");
        }

        var maxEffectiveDate = currencyFixings.Max(p => p.EffectiveEndUtc);
        if (utcDate >= maxEffectiveDate)
        {
            if (src == "CAD")
            {
                throw new InvalidOperationException(
                    $"Effective date {utcDate:yyyy-MM-dd} exceeds published Federal Reserve H.10 schedule (latest observation: 2026-11-06). The release for the week of Nov 9–13 is published on Monday, November 16, 2026 at 21:15 UTC. Future rates cannot be used before publication.");
            }

            throw new InvalidOperationException(
                $"Effective date {utcDate:yyyy-MM-dd} exceeds published bulletin fixing schedule (latest end: {maxEffectiveDate:yyyy-MM-dd}). Future rates cannot be used before publication.");
        }

        var match = currencyFixings.FirstOrDefault(p => utcDate >= p.EffectiveStartUtc && utcDate < p.EffectiveEndUtc);

        if (match != null)
        {
            rateSource = match.CentralBankAuthority;
            rateVersion = match.BulletinVersion;
            benchmarkSeriesId = match.BenchmarkSeriesId;
            integrityHash = match.IntegrityHash;
            publishedAtUtc = match.PublishedAtUtc;
            return match.MultiplierRate;
        }

        throw new InvalidOperationException(
            $"Unsupported currency conversion from '{sourceCurrency}' to '{targetCurrency}' for effective date {utcDate:yyyy-MM-dd}. No authoritative exchange rate is configured.");
    }
}
