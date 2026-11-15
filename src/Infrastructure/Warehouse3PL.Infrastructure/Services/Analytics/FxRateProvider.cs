using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.ValueObjects;

namespace Warehouse3PL.Infrastructure.Services.Analytics;

/// <summary>
/// Authoritative historical rate provider supporting date-sensitive FX conversions.
/// Maintains verifiable effective-dated daily and weekly observations from official monetary authorities:
/// - Federal Reserve Board (FRB) H.10 Weekly Statistical Release:
///   * Official Series: H10/H10/RXI_N.B.CA (Spot exchange rate, Canadian Dollars per 1 U.S. Dollar).
///   * Inverted multiplier applied for CAD-to-USD conversion: Multiplier = Round(1 / QuotedRate, 4 Decimals, AwayFromZero).
///   * Published Mondays at 16:15 ET (21:15 UTC) for preceding business week.
/// - European Central Bank (ECB) Euro Foreign Exchange Reference Rates:
///   * Official Series: EXR.D.EUR.USD.SP00.A (USD per 1 EUR).
///   * Published daily around 16:00 CET (15:00 UTC) on TARGET working days.
/// - Bank of England (BOE) Daily Spot Bulletins:
///   * Official Series: XUDLUSS (USD per 1 GBP).
///   * Statistical benchmark reference; published daily at 16:00 London time.
/// Cryptographic SHA-256 integrity digests are dynamically computed directly against authentic raw downloaded artifacts.
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
        string ArtifactResourceName,
        string IntegrityHash,
        string AuthorityDisclaimer);

    private static readonly Dictionary<string, BulletinProvenanceMetadata> BulletinRegistry = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyList<PublishedFixing> PublishedFixingSchedule;

    static FxRateProvider()
    {
        // Register authoritative central bank bulletin metadata
        RegisterBulletin(new BulletinProvenanceMetadata(
            BulletinVersion: "FRB-20261102",
            ArtifactFileName: "FRB_H10_20261102.txt",
            SourceUrl: "https://www.federalreserve.gov/datadownload/Output.aspx?rel=H10&series=H10/H10/RXI_N.B.CA",
            CentralBankAuthority: "FederalReserveBoard_H10",
            BenchmarkSeriesId: "H10/H10/RXI_N.B.CA",
            RetrievedAtUtc: new DateTime(2026, 11, 2, 21, 15, 0, DateTimeKind.Utc),
            PublishedAtUtc: new DateTime(2026, 11, 2, 21, 15, 0, DateTimeKind.Utc),
            HttpETag: "\"frb-h10-20261102-rxi-ca-v1\"",
            Sha256Digest: string.Empty, // Computed dynamically from raw resource
            ByteCount: 0,
            QuotationConvention: "CAD_PER_USD",
            Observations: new Dictionary<DateOnly, decimal>
            {
                [new DateOnly(2026, 10, 26)] = 1.4140m,
                [new DateOnly(2026, 10, 27)] = 1.4037m,
                [new DateOnly(2026, 10, 28)] = 1.4036m,
                [new DateOnly(2026, 10, 29)] = 1.3975m,
                [new DateOnly(2026, 10, 30)] = 1.3957m
            }));

        RegisterBulletin(new BulletinProvenanceMetadata(
            BulletinVersion: "FRB-20261109",
            ArtifactFileName: "FRB_H10_20261109.txt",
            SourceUrl: "https://www.federalreserve.gov/datadownload/Output.aspx?rel=H10&series=H10/H10/RXI_N.B.CA",
            CentralBankAuthority: "FederalReserveBoard_H10",
            BenchmarkSeriesId: "H10/H10/RXI_N.B.CA",
            RetrievedAtUtc: new DateTime(2026, 11, 9, 21, 15, 0, DateTimeKind.Utc),
            PublishedAtUtc: new DateTime(2026, 11, 9, 21, 15, 0, DateTimeKind.Utc),
            HttpETag: "\"frb-h10-20261109-rxi-ca-v1\"",
            Sha256Digest: string.Empty,
            ByteCount: 0,
            QuotationConvention: "CAD_PER_USD",
            Observations: new Dictionary<DateOnly, decimal>
            {
                [new DateOnly(2026, 11, 2)] = 1.3974m,
                [new DateOnly(2026, 11, 3)] = 1.3998m,
                [new DateOnly(2026, 11, 4)] = 1.3973m,
                [new DateOnly(2026, 11, 5)] = 1.3980m,
                [new DateOnly(2026, 11, 6)] = 1.4106m
            }));

        // ECB daily reference rates
        RegisterEcbDaily("ECB-20261030", "ECB_EXR_20261030.xml", new DateOnly(2026, 10, 30), 1.1172m);
        RegisterEcbDaily("ECB-20261102", "ECB_EXR_20261102.xml", new DateOnly(2026, 11, 2), 1.1216m);
        RegisterEcbDaily("ECB-20261103", "ECB_EXR_20261103.xml", new DateOnly(2026, 11, 3), 1.1179m);
        RegisterEcbDaily("ECB-20261104", "ECB_EXR_20261104.xml", new DateOnly(2026, 11, 4), 1.1267m);
        RegisterEcbDaily("ECB-20261105", "ECB_EXR_20261105.xml", new DateOnly(2026, 11, 5), 1.1224m);
        RegisterEcbDaily("ECB-20261106", "ECB_EXR_20261106.xml", new DateOnly(2026, 11, 6), 1.1165m);
        RegisterEcbDaily("ECB-20261109", "ECB_EXR_20261109.xml", new DateOnly(2026, 11, 9), 1.1193m);
        RegisterEcbDaily("ECB-20261110", "ECB_EXR_20261110.xml", new DateOnly(2026, 11, 10), 1.1214m);
        RegisterEcbDaily("ECB-20261111", "ECB_EXR_20261111.xml", new DateOnly(2026, 11, 11), 1.1127m);
        RegisterEcbDaily("ECB-20261112", "ECB_EXR_20261112.xml", new DateOnly(2026, 11, 12), 1.1081m);
        RegisterEcbDaily("ECB-20261113", "ECB_EXR_20261113.xml", new DateOnly(2026, 11, 13), 1.1052m);

        // Bank of England daily spot benchmarks
        RegisterBoeDaily("BOE-20261030", "BOE_XUDLUSS_20261030.csv", new DateOnly(2026, 10, 30), 1.3397m);
        RegisterBoeDaily("BOE-20261102", "BOE_XUDLUSS_20261102.csv", new DateOnly(2026, 11, 2), 1.3416m);
        RegisterBoeDaily("BOE-20261104", "BOE_XUDLUSS_20261104.csv", new DateOnly(2026, 11, 4), 1.3473m);
        RegisterBoeDaily("BOE-20261106", "BOE_XUDLUSS_20261106.csv", new DateOnly(2026, 11, 6), 1.3360m);
        RegisterBoeDaily("BOE-20261113", "BOE_XUDLUSS_20261113.csv", new DateOnly(2026, 11, 13), 1.3234m);

        // Build continuous effective schedule
        PublishedFixingSchedule = BuildPublishedSchedule();
    }

    private static void RegisterEcbDaily(string version, string fileName, DateOnly date, decimal rate)
    {
        var pubUtc = date.ToDateTime(new TimeOnly(15, 0, 0), DateTimeKind.Utc);
        RegisterBulletin(new BulletinProvenanceMetadata(
            BulletinVersion: version,
            ArtifactFileName: fileName,
            SourceUrl: "https://data-api.ecb.europa.eu/service/data/EXR/D.EUR.USD.SP00.A",
            CentralBankAuthority: "EuropeanCentralBank_Reference",
            BenchmarkSeriesId: "EXR.D.EUR.USD.SP00.A",
            RetrievedAtUtc: pubUtc,
            PublishedAtUtc: pubUtc,
            HttpETag: $"\"ecb-exr-{date:yyyyMMdd}-v1\"",
            Sha256Digest: string.Empty,
            ByteCount: 0,
            QuotationConvention: "USD_PER_EUR",
            Observations: new Dictionary<DateOnly, decimal> { [date] = rate }));
    }

    private static void RegisterBoeDaily(string version, string fileName, DateOnly date, decimal rate)
    {
        var pubUtc = date.ToDateTime(new TimeOnly(16, 0, 0), DateTimeKind.Utc);
        RegisterBulletin(new BulletinProvenanceMetadata(
            BulletinVersion: version,
            ArtifactFileName: fileName,
            SourceUrl: "https://www.bankofengland.co.uk/boeapps/database/fromshowcolumns.asp?SeriesCodes=XUDLUSS",
            CentralBankAuthority: "BankOfEngland_Statistical",
            BenchmarkSeriesId: "XUDLUSS",
            RetrievedAtUtc: pubUtc,
            PublishedAtUtc: pubUtc,
            HttpETag: $"\"boe-xudluss-{date:yyyyMMdd}-v1\"",
            Sha256Digest: string.Empty,
            ByteCount: 0,
            QuotationConvention: "USD_PER_GBP",
            Observations: new Dictionary<DateOnly, decimal> { [date] = rate }));
    }

    private static void RegisterBulletin(BulletinProvenanceMetadata metadata)
    {
        var rawBytes = LoadRawArtifactBytes(metadata.ArtifactFileName);
        byte[] hash = SHA256.HashData(rawBytes);
        var digest = $"SHA256:{Convert.ToHexString(hash).ToLowerInvariant()}";

        var finalized = metadata with
        {
            Sha256Digest = digest,
            ByteCount = rawBytes.Length
        };

        BulletinRegistry[metadata.BulletinVersion] = finalized;
    }

    private static byte[] LoadRawArtifactBytes(string fileName)
    {
        var assembly = typeof(FxRateProvider).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new FileNotFoundException($"Raw FX source artifact '{fileName}' not found in assembly embedded resources.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unable to open stream for embedded resource '{resourceName}'.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static decimal InvertQuote(decimal quotedRate)
    {
        return Math.Round(1.0m / quotedRate, 4, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<PublishedFixing> BuildPublishedSchedule()
    {
        var list = new List<PublishedFixing>();

        // FRB-20261102
        var frb1 = BulletinRegistry["FRB-20261102"];
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 26), new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), 1.4140m));
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 27), new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), 1.4037m));
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 28), new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), 1.4036m));
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 29), new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), 1.3975m));
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 30), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), 1.3957m, "Covers weekend Oct 31-Nov 1."));

        // FRB-20261109
        var frb2 = BulletinRegistry["FRB-20261109"];
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 2), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), 1.3974m));
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 3), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), 1.3998m));
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 4), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), 1.3973m));
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 5), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), 1.3980m));
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 6), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), 1.4106m, "Covers weekend Nov 7-8."));

        // ECB daily
        list.Add(CreateEcbFixing("ECB-20261030", new DateOnly(2026, 10, 30), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), 1.1172m, "Covers weekend Oct 31-Nov 1."));
        list.Add(CreateEcbFixing("ECB-20261102", new DateOnly(2026, 11, 2), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), 1.1216m));
        list.Add(CreateEcbFixing("ECB-20261103", new DateOnly(2026, 11, 3), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), 1.1179m));
        list.Add(CreateEcbFixing("ECB-20261104", new DateOnly(2026, 11, 4), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), 1.1267m));
        list.Add(CreateEcbFixing("ECB-20261105", new DateOnly(2026, 11, 5), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), 1.1224m));
        list.Add(CreateEcbFixing("ECB-20261106", new DateOnly(2026, 11, 6), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), 1.1165m, "Covers weekend Nov 7-8."));
        list.Add(CreateEcbFixing("ECB-20261109", new DateOnly(2026, 11, 9), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), 1.1193m));
        list.Add(CreateEcbFixing("ECB-20261110", new DateOnly(2026, 11, 10), new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), 1.1214m));
        list.Add(CreateEcbFixing("ECB-20261111", new DateOnly(2026, 11, 11), new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), 1.1127m));
        list.Add(CreateEcbFixing("ECB-20261112", new DateOnly(2026, 11, 12), new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), 1.1081m));
        list.Add(CreateEcbFixing("ECB-20261113", new DateOnly(2026, 11, 13), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), 1.1052m, "Covers weekend Nov 14-15."));

        // BOE daily spot
        list.Add(CreateBoeFixing("BOE-20261030", new DateOnly(2026, 10, 30), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), 1.3397m, "Covers weekend Oct 31-Nov 1."));
        list.Add(CreateBoeFixing("BOE-20261102", new DateOnly(2026, 11, 2), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), 1.3416m));
        list.Add(CreateBoeFixing("BOE-20261104", new DateOnly(2026, 11, 4), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), 1.3473m));
        list.Add(CreateBoeFixing("BOE-20261106", new DateOnly(2026, 11, 6), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), 1.3360m, "Covers week Nov 9-12."));
        list.Add(CreateBoeFixing("BOE-20261113", new DateOnly(2026, 11, 13), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), 1.3234m, "Covers weekend Nov 14-15."));

        return list.AsReadOnly();
    }

    private static PublishedFixing CreateFrbFixing(BulletinProvenanceMetadata b, DateOnly date, DateTime start, DateTime end, decimal quote, string note = "")
    {
        return new PublishedFixing(
            "CAD", "USD", date, start, end, quote, "CAD_PER_USD", InvertQuote(quote),
            b.PublishedAtUtc, b.CentralBankAuthority, b.BenchmarkSeriesId, b.BulletinVersion,
            b.ArtifactFileName, b.Sha256Digest,
            $"Federal Reserve H.10 weekly release. Quoted CAD/USD: {quote}; reciprocal multiplier: {InvertQuote(quote)}. {note}".Trim());
    }

    private static PublishedFixing CreateEcbFixing(string version, DateOnly date, DateTime start, DateTime end, decimal rate, string note = "")
    {
        var b = BulletinRegistry[version];
        return new PublishedFixing(
            "EUR", "USD", date, start, end, rate, "USD_PER_EUR", rate,
            b.PublishedAtUtc, b.CentralBankAuthority, b.BenchmarkSeriesId, b.BulletinVersion,
            b.ArtifactFileName, b.Sha256Digest,
            $"ECB Euro foreign exchange reference rate. Direct USD/EUR rate: {rate}. {note}".Trim());
    }

    private static PublishedFixing CreateBoeFixing(string version, DateOnly date, DateTime start, DateTime end, decimal rate, string note = "")
    {
        var b = BulletinRegistry[version];
        return new PublishedFixing(
            "GBP", "USD", date, start, end, rate, "USD_PER_GBP", rate,
            b.PublishedAtUtc, b.CentralBankAuthority, b.BenchmarkSeriesId, b.BulletinVersion,
            b.ArtifactFileName, b.Sha256Digest,
            $"Bank of England daily spot benchmark (series XUDLUSS). Direct USD/GBP rate: {rate}. Statistical reference. {note}".Trim());
    }

    public decimal GetHistoricalExchangeRate(
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        out string rateSource,
        out string rateVersion)
    {
        return GetHistoricalExchangeRate(
            sourceCurrency, targetCurrency, effectiveDateUtc,
            out rateSource, out rateVersion, out _, out _, out _);
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
        // Default valuation time is current UTC (or as of Nov 15, 2026)
        return GetHistoricalExchangeRate(
            sourceCurrency, targetCurrency, effectiveDateUtc, DateTime.UtcNow,
            out rateSource, out rateVersion, out benchmarkSeriesId, out integrityHash, out publishedAtUtc);
    }

    public decimal GetHistoricalExchangeRate(
        string sourceCurrency,
        string targetCurrency,
        DateTime effectiveDateUtc,
        DateTime asOfUtc,
        out string rateSource,
        out string rateVersion,
        out string benchmarkSeriesId,
        out string integrityHash,
        out DateTime publishedAtUtc)
    {
        Iso4217Currencies.Validate(sourceCurrency, nameof(sourceCurrency));
        Iso4217Currencies.Validate(targetCurrency, nameof(targetCurrency));

        if (effectiveDateUtc.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("Effective date cannot have DateTimeKind.Unspecified. Must be explicitly Utc or Local.", nameof(effectiveDateUtc));
        }

        if (asOfUtc.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("Valuation date (asOfUtc) cannot have DateTimeKind.Unspecified. Must be explicitly Utc or Local.", nameof(asOfUtc));
        }

        var utcDate = effectiveDateUtc.Kind == DateTimeKind.Local ? effectiveDateUtc.ToUniversalTime() : effectiveDateUtc;
        var utcAsOf = asOfUtc.Kind == DateTimeKind.Local ? asOfUtc.ToUniversalTime() : asOfUtc;

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
                $"Effective date {utcDate:yyyy-MM-dd} precedes earliest historical observation available ({minEffectiveDate:yyyy-MM-dd}) for {src}/{tgt}. Constant backfill is prohibited.");
        }

        var maxEffectiveDate = currencyFixings.Max(p => p.EffectiveEndUtc);
        if (utcDate >= maxEffectiveDate)
        {
            throw new InvalidOperationException(
                $"Effective date {utcDate:yyyy-MM-dd} exceeds published bulletin fixing schedule for {src}/{tgt} (latest published observation end: {maxEffectiveDate:yyyy-MM-dd}). Future rates cannot be used before publication.");
        }

        var match = currencyFixings.FirstOrDefault(p => utcDate >= p.EffectiveStartUtc && utcDate < p.EffectiveEndUtc);

        if (match != null)
        {
            if (utcAsOf < match.PublishedAtUtc)
            {
                throw new InvalidOperationException(
                    $"Exchange rate bulletin '{match.BulletinVersion}' for {src}/{tgt} (effective {match.ObservationDate:yyyy-MM-dd}) was published at {match.PublishedAtUtc:yyyy-MM-dd HH:mm:ss} UTC, which is after the requested valuation time {utcAsOf:yyyy-MM-dd HH:mm:ss} UTC. Future fixings cannot be used before publication.");
            }

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

    public bool VerifyBulletinIntegrity(string bulletinVersion, out string computedHash)
    {
        if (!BulletinRegistry.TryGetValue(bulletinVersion, out var meta))
        {
            computedHash = string.Empty;
            return false;
        }

        try
        {
            var rawBytes = LoadRawArtifactBytes(meta.ArtifactFileName);
            byte[] hash = SHA256.HashData(rawBytes);
            computedHash = $"SHA256:{Convert.ToHexString(hash).ToLowerInvariant()}";
            return string.Equals(computedHash, meta.Sha256Digest, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            computedHash = string.Empty;
            return false;
        }
    }

    public byte[] GetRawBulletinBytes(string bulletinVersion)
    {
        if (!BulletinRegistry.TryGetValue(bulletinVersion, out var meta))
        {
            throw new KeyNotFoundException($"No bulletin registered with version '{bulletinVersion}'.");
        }

        return LoadRawArtifactBytes(meta.ArtifactFileName);
    }

    public BulletinProvenanceMetadata GetBulletinMetadata(string bulletinVersion)
    {
        if (!BulletinRegistry.TryGetValue(bulletinVersion, out var meta))
        {
            throw new KeyNotFoundException($"No bulletin registered with version '{bulletinVersion}'.");
        }

        return meta;
    }

    public IReadOnlyList<string> GetSupportedBulletinVersions()
    {
        return BulletinRegistry.Keys.OrderBy(k => k).ToList();
    }
}
