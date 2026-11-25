using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;
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
/// Cryptographic integrity is verified against an independent trust anchor manifest (FxBulletinManifest).
/// Source observations are parsed directly from raw embedded artifacts and cross-validated against the manifest.
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

    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IApplicationDbContext? _directDb;

    public FxRateProvider()
    {
    }

    [ActivatorUtilitiesConstructor]
    public FxRateProvider(IServiceScopeFactory? scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    private FxRateProvider(IApplicationDbContext directDb)
    {
        _directDb = directDb;
    }

    public static FxRateProvider ForDbContext(IApplicationDbContext directDb) => new(directDb);

    static FxRateProvider()
    {
        // 1. Load, parse, verify, and register each bulletin from the independent trust anchor manifest
        foreach (var (version, manifestEntry) in FxBulletinManifest.Entries)
        {
            var rawBytes = LoadRawArtifactBytes(manifestEntry.ArtifactFileName);

            // Compute dynamic SHA-256 over raw artifact bytes
            byte[] hash = SHA256.HashData(rawBytes);
            var computedDigest = $"SHA256:{Convert.ToHexString(hash).ToLowerInvariant()}";

            // Verify against independently pinned expected trust anchor digest
            if (!string.Equals(computedDigest, manifestEntry.ExpectedSha256Digest, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException(
                    $"Cryptographic integrity failure for bulletin '{version}'. " +
                    $"Raw artifact '{manifestEntry.ArtifactFileName}' computed digest '{computedDigest}' does not match " +
                    $"independently pinned manifest digest '{manifestEntry.ExpectedSha256Digest}'.");
            }

            // Parse raw artifact payload directly according to its authority format
            var (parsedSeries, parsedObservations) = ParseArtifact(manifestEntry.ArtifactFileName, rawBytes);

            // Cross-validate parsed series identifier
            if (!string.Equals(parsedSeries, manifestEntry.BenchmarkSeriesId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Series identity mismatch in artifact '{manifestEntry.ArtifactFileName}'. " +
                    $"Parsed '{parsedSeries}', expected benchmark '{manifestEntry.BenchmarkSeriesId}'.");
            }

            // Cross-validate parsed observations against manifest expectations
            if (parsedObservations.Count != manifestEntry.ExpectedObservations.Count)
            {
                throw new InvalidOperationException(
                    $"Observation count mismatch in artifact '{manifestEntry.ArtifactFileName}'. " +
                    $"Parsed {parsedObservations.Count} observations, expected {manifestEntry.ExpectedObservations.Count}.");
            }

            foreach (var (expectedDate, expectedRate) in manifestEntry.ExpectedObservations)
            {
                if (!parsedObservations.TryGetValue(expectedDate, out var actualRate))
                {
                    throw new InvalidOperationException(
                        $"Missing observation date '{expectedDate:yyyy-MM-dd}' in parsed artifact '{manifestEntry.ArtifactFileName}'.");
                }

                if (actualRate != expectedRate)
                {
                    throw new InvalidOperationException(
                        $"Rate mismatch for date '{expectedDate:yyyy-MM-dd}' in parsed artifact '{manifestEntry.ArtifactFileName}'. " +
                        $"Parsed {actualRate}, expected {expectedRate}.");
                }
            }

            // Register verified bulletin metadata with parsed observations and pinned digest
            BulletinRegistry[version] = new BulletinProvenanceMetadata(
                BulletinVersion: version,
                ArtifactFileName: manifestEntry.ArtifactFileName,
                SourceUrl: manifestEntry.SourceUrl,
                CentralBankAuthority: manifestEntry.CentralBankAuthority,
                BenchmarkSeriesId: manifestEntry.BenchmarkSeriesId,
                RetrievedAtUtc: manifestEntry.RetrievedAtUtc,
                PublishedAtUtc: manifestEntry.PublishedAtUtc,
                HttpETag: manifestEntry.HttpETag,
                Sha256Digest: computedDigest,
                ByteCount: rawBytes.Length,
                QuotationConvention: manifestEntry.QuotationConvention,
                Observations: parsedObservations);
        }

        // 2. Build continuous effective schedule directly from verified and parsed bulletin records
        PublishedFixingSchedule = BuildPublishedSchedule();
    }

    private static (string Series, IReadOnlyDictionary<DateOnly, decimal> Observations) ParseArtifact(string fileName, byte[] rawBytes)
    {
        if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var text = Encoding.UTF8.GetString(rawBytes);
            return ParseFrbTextArtifact(text);
        }
        else if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return ParseEcbXmlArtifact(rawBytes);
        }
        else if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var text = Encoding.UTF8.GetString(rawBytes);
            return ParseBoeCsvArtifact(text);
        }

        throw new NotSupportedException($"Unsupported artifact file extension for '{fileName}'.");
    }

    private static (string Series, IReadOnlyDictionary<DateOnly, decimal> Observations) ParseFrbTextArtifact(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string series = string.Empty;
        var obs = new Dictionary<DateOnly, decimal>();
        bool readingData = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("SERIES:", StringComparison.OrdinalIgnoreCase))
            {
                series = line.Substring("SERIES:".Length).Trim();
            }
            else if (line.StartsWith("WEEK ENDING", StringComparison.OrdinalIgnoreCase))
            {
                readingData = true;
            }
            else if (readingData)
            {
                if (line.StartsWith("SOURCE:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("HTTP_ETAG:", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                if (line.Contains('|'))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2 &&
                        DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) &&
                        decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                    {
                        obs[d] = r;
                    }
                }
            }
        }

        return (series, obs);
    }

    private static (string Series, IReadOnlyDictionary<DateOnly, decimal> Observations) ParseEcbXmlArtifact(byte[] xmlBytes)
    {
        using var stream = new MemoryStream(xmlBytes);
        var doc = XDocument.Load(stream);
        var obs = new Dictionary<DateOnly, decimal>();
        string series = string.Empty;

        var seriesEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Series");
        if (seriesEl != null)
        {
            series = seriesEl.Attribute("KEY")?.Value ?? string.Empty;
            foreach (var obsEl in seriesEl.Elements().Where(e => e.Name.LocalName == "Obs"))
            {
                var dateStr = obsEl.Attribute("OBS_DATE")?.Value;
                var valStr = obsEl.Attribute("OBS_VALUE")?.Value;
                if (DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) &&
                    decimal.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                {
                    obs[d] = r;
                }
            }
        }

        return (series, obs);
    }

    private static (string Series, IReadOnlyDictionary<DateOnly, decimal> Observations) ParseBoeCsvArtifact(string csvText)
    {
        var lines = csvText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var obs = new Dictionary<DateOnly, decimal>();
        string series = string.Empty;

        // Skip CSV header
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length >= 3)
            {
                if (DateOnly.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                {
                    series = parts[1].Trim();
                    if (decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                    {
                        obs[d] = r;
                    }
                }
            }
        }

        return (series, obs);
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

        // --------------------------------------------------------------------------------------------------
        // FRB-20261102 (Week Oct 26 - Oct 30)
        // --------------------------------------------------------------------------------------------------
        var frb1 = BulletinRegistry["FRB-20261102"];
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 26), new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 27), new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 28), new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 29), new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateFrbFixing(frb1, new DateOnly(2026, 10, 30), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), "Covers weekend Oct 31-Nov 1."));

        // --------------------------------------------------------------------------------------------------
        // FRB-20261109 (Week Nov 2 - Nov 6)
        // --------------------------------------------------------------------------------------------------
        var frb2 = BulletinRegistry["FRB-20261109"];
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 2), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 3), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 4), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 5), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateFrbFixing(frb2, new DateOnly(2026, 11, 6), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), "Covers weekend Nov 7-8."));

        // --------------------------------------------------------------------------------------------------
        // ECB daily reference rates (series EXR.D.EUR.USD.SP00.A)
        // --------------------------------------------------------------------------------------------------
        list.Add(CreateEcbFixing("ECB-20261030", new DateOnly(2026, 10, 30), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), "Covers weekend Oct 31-Nov 1."));
        list.Add(CreateEcbFixing("ECB-20261102", new DateOnly(2026, 11, 2), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateEcbFixing("ECB-20261103", new DateOnly(2026, 11, 3), new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateEcbFixing("ECB-20261104", new DateOnly(2026, 11, 4), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateEcbFixing("ECB-20261105", new DateOnly(2026, 11, 5), new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateEcbFixing("ECB-20261106", new DateOnly(2026, 11, 6), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), "Covers weekend Nov 7-8."));
        list.Add(CreateEcbFixing("ECB-20261109", new DateOnly(2026, 11, 9), new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateEcbFixing("ECB-20261110", new DateOnly(2026, 11, 10), new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateEcbFixing("ECB-20261111", new DateOnly(2026, 11, 11), new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateEcbFixing("ECB-20261112", new DateOnly(2026, 11, 12), new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateEcbFixing("ECB-20261113", new DateOnly(2026, 11, 13), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), "Covers weekend Nov 14-15."));

        // --------------------------------------------------------------------------------------------------
        // Bank of England daily spot benchmarks (series XUDLUSS)
        // --------------------------------------------------------------------------------------------------
        list.Add(CreateBoeFixing("BOE-20261030", new DateOnly(2026, 10, 30), new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), "Covers weekend Oct 31-Nov 1."));
        list.Add(CreateBoeFixing("BOE-20261102", new DateOnly(2026, 11, 2), new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateBoeFixing("BOE-20261104", new DateOnly(2026, 11, 4), new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)));
        list.Add(CreateBoeFixing("BOE-20261106", new DateOnly(2026, 11, 6), new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), "Covers week Nov 9-12."));
        list.Add(CreateBoeFixing("BOE-20261113", new DateOnly(2026, 11, 13), new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), "Covers weekend Nov 14-15."));

        return list.AsReadOnly();
    }

    private static PublishedFixing CreateFrbFixing(BulletinProvenanceMetadata b, DateOnly date, DateTime start, DateTime end, string note = "")
    {
        if (!b.Observations.TryGetValue(date, out var quote))
        {
            throw new InvalidOperationException($"Observation date '{date:yyyy-MM-dd}' not found in bulletin '{b.BulletinVersion}'.");
        }

        var multiplier = InvertQuote(quote);

        return new PublishedFixing(
            "CAD", "USD", date, start, end, quote, "CAD_PER_USD", multiplier,
            b.PublishedAtUtc, b.CentralBankAuthority, b.BenchmarkSeriesId, b.BulletinVersion,
            b.ArtifactFileName, b.Sha256Digest,
            $"Federal Reserve H.10 weekly release. Quoted CAD/USD: {quote}; reciprocal multiplier: {multiplier}. {note}".Trim());
    }

    private static PublishedFixing CreateEcbFixing(string version, DateOnly date, DateTime start, DateTime end, string note = "")
    {
        var b = BulletinRegistry[version];
        if (!b.Observations.TryGetValue(date, out var rate))
        {
            throw new InvalidOperationException($"Observation date '{date:yyyy-MM-dd}' not found in bulletin '{version}'.");
        }

        return new PublishedFixing(
            "EUR", "USD", date, start, end, rate, "USD_PER_EUR", rate,
            b.PublishedAtUtc, b.CentralBankAuthority, b.BenchmarkSeriesId, b.BulletinVersion,
            b.ArtifactFileName, b.Sha256Digest,
            $"ECB Euro foreign exchange reference rate. Direct USD/EUR rate: {rate}. {note}".Trim());
    }

    private static PublishedFixing CreateBoeFixing(string version, DateOnly date, DateTime start, DateTime end, string note = "")
    {
        var b = BulletinRegistry[version];
        if (!b.Observations.TryGetValue(date, out var rate))
        {
            throw new InvalidOperationException($"Observation date '{date:yyyy-MM-dd}' not found in bulletin '{version}'.");
        }

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

        // MDM-1.3: Consult database-authoritative effective-dated FxRateRecord rows first
        // (honoring UnitMultiplier such as NRB 100 INR -> NPR and PublishedAtUtc cutoffs)
        // before falling back to verified embedded central bank historical bulletins.
        if (TryResolveDatabaseRate(
                src, tgt, utcDate, utcAsOf,
                out var dbRate, out rateSource, out rateVersion, out benchmarkSeriesId, out integrityHash, out publishedAtUtc))
        {
            return dbRate;
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

    private bool TryResolveDatabaseRate(
        string src,
        string tgt,
        DateTime utcDate,
        DateTime utcAsOf,
        out decimal unitRate,
        out string rateSource,
        out string rateVersion,
        out string benchmarkSeriesId,
        out string integrityHash,
        out DateTime publishedAtUtc)
    {
        unitRate = 0m;
        rateSource = string.Empty;
        rateVersion = string.Empty;
        benchmarkSeriesId = string.Empty;
        integrityHash = string.Empty;
        publishedAtUtc = default;

        IApplicationDbContext? db = _directDb;
        IServiceScope? scope = null;
        if (db == null && _scopeFactory != null)
        {
            scope = _scopeFactory.CreateScope();
            db = scope.ServiceProvider.GetService<IApplicationDbContext>();
        }

        if (db == null)
        {
            scope?.Dispose();
            return false;
        }

        try
        {
            var match = db.FxRateRecords
                .AsNoTracking()
                .Where(r => r.BaseCurrencyCode == src &&
                            r.QuoteCurrencyCode == tgt &&
                            r.ApprovalStatus == FxRateApprovalStatus.VerifiedApproved &&
                            r.IsActive &&
                            r.EffectiveFromUtc <= utcDate &&
                            (r.EffectiveToUtc == null || r.EffectiveToUtc > utcDate))
                .OrderByDescending(r => r.EffectiveFromUtc)
                .ThenByDescending(r => r.PublishedAtUtc)
                .ThenByDescending(r => r.ApprovedAtUtc)
                .FirstOrDefault();

            if (match == null)
            {
                return false;
            }

            if (utcAsOf < match.PublishedAtUtc)
            {
                throw new InvalidOperationException(
                    $"Exchange rate bulletin '{match.BulletinVersion}' for {src}/{tgt} was published at {match.PublishedAtUtc:yyyy-MM-dd HH:mm:ss} UTC, which is after the requested valuation time {utcAsOf:yyyy-MM-dd HH:mm:ss} UTC. Future fixings cannot be used before publication.");
            }

            unitRate = match.ComputeUnitRate(FxRateType.Mid);
            rateSource = match.SourceAuthority.ToString();
            rateVersion = match.BulletinVersion;
            benchmarkSeriesId = match.BenchmarkSeriesId ?? $"{src}/{tgt}";
            integrityHash = match.IntegrityHash ?? "DB-VERIFIED";
            publishedAtUtc = match.PublishedAtUtc;
            return true;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    public bool VerifyBulletinIntegrity(string bulletinVersion, out string computedHash)
    {
        return VerifyBulletinIntegrity(bulletinVersion, null, out computedHash);
    }

    public bool VerifyBulletinIntegrity(string bulletinVersion, byte[]? rawBytesToVerify, out string computedHash)
    {
        if (!FxBulletinManifest.TryGetEntry(bulletinVersion, out var manifestEntry))
        {
            computedHash = string.Empty;
            return false;
        }

        try
        {
            byte[] bytes = rawBytesToVerify ?? LoadRawArtifactBytes(manifestEntry.ArtifactFileName);
            byte[] hash = SHA256.HashData(bytes);
            computedHash = $"SHA256:{Convert.ToHexString(hash).ToLowerInvariant()}";
            return string.Equals(computedHash, manifestEntry.ExpectedSha256Digest, StringComparison.OrdinalIgnoreCase);
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
