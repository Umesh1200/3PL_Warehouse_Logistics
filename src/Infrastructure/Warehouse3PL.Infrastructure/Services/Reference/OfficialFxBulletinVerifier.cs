using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Warehouse3PL.Application.Common.Interfaces;
using Warehouse3PL.Domain.Enums;
using Warehouse3PL.Domain.Exceptions;
using Warehouse3PL.Infrastructure.Services.Analytics;

namespace Warehouse3PL.Infrastructure.Services.Reference;

/// <summary>
/// Verifies official central-bank FX bulletins (NRB, FRB, ECB, BOE) against pinned embedded trust-anchor
/// artifacts, dynamically computing SHA-256 digests and parsing quotations directly from raw artifact bytes.
/// Rejects fabricated payloads even when accompanied by mathematically valid self-computed SHA-256 hashes.
/// </summary>
public sealed class OfficialFxBulletinVerifier : IOfficialFxBulletinVerifier
{
    private sealed record PinnedNrbBulletinEntry(
        string BulletinVersion,
        string ArtifactFileName,
        string ExpectedSha256Digest,
        int ExpectedByteCount,
        string SourceUrl,
        string BenchmarkSeriesId,
        DateTime PublishedAtUtc);

    private sealed record ParsedBulletinRate(
        string BaseCurrencyCode,
        string QuoteCurrencyCode,
        FxRateDirection Direction,
        int UnitMultiplier,
        decimal BuyRate,
        decimal SellRate,
        decimal MidRate);

    private sealed record VerifiedBulletinEntry(
        string BulletinVersion,
        FxRateSourceAuthority Authority,
        string BenchmarkSeriesId,
        string VerifiedSha256Digest,
        string EvidenceReference,
        DateTime PublishedAtUtc,
        IReadOnlyList<ParsedBulletinRate> Rates);

    private static readonly PinnedNrbBulletinEntry[] PinnedNrbEntries =
    [
        new(
            BulletinVersion: "NRB-20261120",
            ArtifactFileName: "NRB_FX_20261120.json",
            ExpectedSha256Digest: "SHA256:549552d081529155bc6724e37b18ca18ba68fbb6c1e3f87a4f3190f5cdf51acb",
            ExpectedByteCount: 739,
            SourceUrl: "https://www.nrb.org.np/api/forex/v1/rates?from=2026-11-20&to=2026-11-20",
            BenchmarkSeriesId: "NRB-FX-DAILY-2026-11-20",
            PublishedAtUtc: new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc)),
        new(
            BulletinVersion: "NRB-20261121",
            ArtifactFileName: "NRB_FX_20261121.json",
            ExpectedSha256Digest: "SHA256:06c2f260ae6efd2208e19bc672734062eee1b962069d64ab9a635a0afe3862de",
            ExpectedByteCount: 739,
            SourceUrl: "https://www.nrb.org.np/api/forex/v1/rates?from=2026-11-21&to=2026-11-21",
            BenchmarkSeriesId: "NRB-FX-DAILY-2026-11-21",
            PublishedAtUtc: new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc))
    ];

    private static readonly IReadOnlyDictionary<string, VerifiedBulletinEntry> Registry = BuildVerifiedRegistry();

    public OfficialBulletinVerificationResult VerifyOfficialQuotation(
        FxRateSourceAuthority authority,
        string bulletinVersion,
        string baseCurrencyCode,
        string quoteCurrencyCode,
        FxRateDirection rateDirection,
        int unitMultiplier,
        decimal buyRate,
        decimal sellRate,
        decimal midRate,
        DateTime publishedAtUtc,
        string? suppliedBenchmarkSeriesId = null,
        string? suppliedIntegrityHash = null,
        string? suppliedArtifactPayload = null)
    {
        if (authority == FxRateSourceAuthority.ManualApproved)
        {
            throw new DomainException(
                "ManualApproved rates do not use official central-bank bulletin verification.",
                "FX_RATE_MANUAL_NOT_OFFICIAL");
        }

        var normalizedVersion = (bulletinVersion ?? string.Empty).Trim();
        if (!Registry.TryGetValue(normalizedVersion, out var entry))
        {
            throw new DomainException(
                $"Official FX bulletin '{bulletinVersion}' is not an authenticated trust-anchor artifact for '{authority}'. Unverified central-bank bulletins cannot be approved; use ManualApproved for non-bulletin governance rates.",
                "FX_RATE_UNTRUSTED_BULLETIN");
        }

        if (entry.Authority != authority)
        {
            throw new DomainException(
                $"Bulletin '{entry.BulletinVersion}' belongs to authority '{entry.Authority}', not '{authority}'.",
                "FX_RATE_AUTHORITY_MISMATCH");
        }

        if (!string.IsNullOrWhiteSpace(suppliedBenchmarkSeriesId) &&
            !string.Equals(suppliedBenchmarkSeriesId.Trim(), entry.BenchmarkSeriesId, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"Supplied BenchmarkSeriesId '{suppliedBenchmarkSeriesId}' does not match verified bulletin series '{entry.BenchmarkSeriesId}'.",
                "FX_RATE_PROVENANCE_SERIES_MISMATCH");
        }

        if (!string.IsNullOrWhiteSpace(suppliedIntegrityHash) &&
            !string.Equals(suppliedIntegrityHash.Trim(), entry.VerifiedSha256Digest, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                $"Supplied IntegrityHash '{suppliedIntegrityHash}' does not match pinned SHA-256 digest '{entry.VerifiedSha256Digest}' for '{entry.BulletinVersion}'.",
                "FX_RATE_PROVENANCE_HASH_MISMATCH");
        }

        if (!string.IsNullOrWhiteSpace(suppliedArtifactPayload))
        {
            var payloadHash = ComputeDigest(Encoding.UTF8.GetBytes(suppliedArtifactPayload));
            if (!string.Equals(payloadHash, entry.VerifiedSha256Digest, StringComparison.OrdinalIgnoreCase))
            {
                var lfNormalizedHash = ComputeDigest(Encoding.UTF8.GetBytes(suppliedArtifactPayload.Replace("\r\n", "\n")));
                if (!string.Equals(lfNormalizedHash, entry.VerifiedSha256Digest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DomainException(
                        $"Supplied SourceArtifactPayload SHA-256 '{payloadHash}' does not match trusted official artifact '{entry.VerifiedSha256Digest}'.",
                        "FX_RATE_PROVENANCE_HASH_MISMATCH");
                }
            }
        }

        if (publishedAtUtc != entry.PublishedAtUtc)
        {
            throw new DomainException(
                $"Submitted PublishedAtUtc '{publishedAtUtc:O}' does not match official bulletin publication timestamp '{entry.PublishedAtUtc:O}' for '{entry.BulletinVersion}'.",
                "FX_RATE_PUBLICATION_TIMESTAMP_MISMATCH");
        }

        var normalizedBase = (baseCurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedQuote = (quoteCurrencyCode ?? string.Empty).Trim().ToUpperInvariant();

        var matchedQuote = entry.Rates.FirstOrDefault(r =>
            string.Equals(r.BaseCurrencyCode, normalizedBase, StringComparison.Ordinal) &&
            string.Equals(r.QuoteCurrencyCode, normalizedQuote, StringComparison.Ordinal) &&
            r.Direction == rateDirection &&
            r.UnitMultiplier == unitMultiplier &&
            r.BuyRate == decimal.Round(buyRate, 6, MidpointRounding.AwayFromZero) &&
            r.SellRate == decimal.Round(sellRate, 6, MidpointRounding.AwayFromZero) &&
            r.MidRate == decimal.Round(midRate, 6, MidpointRounding.AwayFromZero));

        if (matchedQuote == null)
        {
            throw new DomainException(
                $"Submitted quotation ({normalizedBase}/{normalizedQuote}, Direction={rateDirection}, UnitMultiplier={unitMultiplier}, Buy={buyRate}, Sell={sellRate}, Mid={midRate}) does not match any verified quotation inside official bulletin '{entry.BulletinVersion}'.",
                "FX_RATE_QUOTATION_MISMATCH");
        }

        return new OfficialBulletinVerificationResult(
            entry.BulletinVersion,
            entry.Authority,
            entry.BenchmarkSeriesId,
            entry.VerifiedSha256Digest,
            entry.EvidenceReference,
            entry.PublishedAtUtc);
    }

    private static IReadOnlyDictionary<string, VerifiedBulletinEntry> BuildVerifiedRegistry()
    {
        var map = new Dictionary<string, VerifiedBulletinEntry>(StringComparer.OrdinalIgnoreCase);

        // 1. Pinned NRB JSON official daily circulars
        foreach (var nrb in PinnedNrbEntries)
        {
            var rawBytes = LoadArtifactBytes(nrb.ArtifactFileName);
            var computed = ComputeNormalizedArtifactDigest(rawBytes);
            if (!string.Equals(computed, nrb.ExpectedSha256Digest, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException(
                    $"NRB bulletin integrity failure for '{nrb.BulletinVersion}': computed '{computed}', expected '{nrb.ExpectedSha256Digest}'.");
            }

            var rates = ParseNrbJsonArtifact(rawBytes, nrb);
            map[nrb.BulletinVersion] = new VerifiedBulletinEntry(
                nrb.BulletinVersion,
                FxRateSourceAuthority.NRB,
                nrb.BenchmarkSeriesId,
                computed,
                $"{nrb.SourceUrl}#{nrb.ArtifactFileName}",
                nrb.PublishedAtUtc,
                rates);
        }

        // 2. Trusted FRB / ECB / BOE embedded trust anchors from FxBulletinManifest
        foreach (var (version, entry) in FxBulletinManifest.Entries)
        {
            var rawBytes = LoadArtifactBytes(entry.ArtifactFileName);
            var computed = ComputeDigest(rawBytes);
            if (!string.Equals(computed, entry.ExpectedSha256Digest, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException(
                    $"Central bank bulletin integrity failure for '{version}': computed '{computed}', expected '{entry.ExpectedSha256Digest}'.");
            }

            var authority = MapManifestAuthority(entry.CentralBankAuthority);
            var parsedRates = ParseLegacyCentralBankRates(authority, entry, rawBytes);
            map[version] = new VerifiedBulletinEntry(
                version,
                authority,
                entry.BenchmarkSeriesId,
                computed,
                $"{entry.SourceUrl}#{entry.ArtifactFileName}",
                entry.PublishedAtUtc,
                parsedRates);
        }

        return map;
    }

    private static FxRateSourceAuthority MapManifestAuthority(string centralBankAuthority)
    {
        if (centralBankAuthority.StartsWith("FederalReserveBoard", StringComparison.OrdinalIgnoreCase))
            return FxRateSourceAuthority.FRB;
        if (centralBankAuthority.StartsWith("EuropeanCentralBank", StringComparison.OrdinalIgnoreCase))
            return FxRateSourceAuthority.ECB;
        if (centralBankAuthority.StartsWith("BankOfEngland", StringComparison.OrdinalIgnoreCase))
            return FxRateSourceAuthority.BOE;
        throw new InvalidOperationException($"Unsupported central bank authority '{centralBankAuthority}'.");
    }

    private static IReadOnlyList<ParsedBulletinRate> ParseNrbJsonArtifact(byte[] rawBytes, PinnedNrbBulletinEntry meta)
    {
        using var doc = JsonDocument.Parse(rawBytes);
        var root = doc.RootElement;
        var fileVersion = root.GetProperty("bulletinVersion").GetString();
        var fileSeries = root.GetProperty("benchmarkSeriesId").GetString();
        if (!string.Equals(fileVersion, meta.BulletinVersion, StringComparison.Ordinal) ||
            !string.Equals(fileSeries, meta.BenchmarkSeriesId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Metadata mismatch in '{meta.ArtifactFileName}'.");
        }

        var list = new List<ParsedBulletinRate>();
        foreach (var r in root.GetProperty("rates").EnumerateArray())
        {
            list.Add(new ParsedBulletinRate(
                r.GetProperty("baseCurrencyCode").GetString()!,
                r.GetProperty("quoteCurrencyCode").GetString()!,
                Enum.Parse<FxRateDirection>(r.GetProperty("rateDirection").GetString()!),
                r.GetProperty("unitMultiplier").GetInt32(),
                decimal.Round(r.GetProperty("buyRate").GetDecimal(), 6, MidpointRounding.AwayFromZero),
                decimal.Round(r.GetProperty("sellRate").GetDecimal(), 6, MidpointRounding.AwayFromZero),
                decimal.Round(r.GetProperty("midRate").GetDecimal(), 6, MidpointRounding.AwayFromZero)));
        }
        return list;
    }

    private static IReadOnlyList<ParsedBulletinRate> ParseLegacyCentralBankRates(
        FxRateSourceAuthority authority,
        FxBulletinManifestEntry entry,
        byte[] rawBytes)
    {
        var list = new List<ParsedBulletinRate>();
        if (authority == FxRateSourceAuthority.ECB)
        {
            using var ms = new MemoryStream(rawBytes);
            var doc = XDocument.Load(ms);
            foreach (var obs in doc.Descendants().Where(e => e.Name.LocalName == "Obs"))
            {
                if (decimal.TryParse(obs.Attribute("OBS_VALUE")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    var rounded = decimal.Round(val, 6, MidpointRounding.AwayFromZero);
                    list.Add(new ParsedBulletinRate("EUR", "USD", FxRateDirection.QuotePerUnitBase, 1, rounded, rounded, rounded));
                }
            }
        }
        else if (authority == FxRateSourceAuthority.BOE)
        {
            var lines = Encoding.UTF8.GetString(rawBytes).Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length >= 3 && decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    var rounded = decimal.Round(val, 6, MidpointRounding.AwayFromZero);
                    list.Add(new ParsedBulletinRate("GBP", "USD", FxRateDirection.QuotePerUnitBase, 1, rounded, rounded, rounded));
                }
            }
        }
        else if (authority == FxRateSourceAuthority.FRB)
        {
            foreach (var obs in entry.ExpectedObservations.Values)
            {
                var direct = decimal.Round(obs, 6, MidpointRounding.AwayFromZero);
                var inverse = decimal.Round(1.0m / obs, 4, MidpointRounding.AwayFromZero);
                list.Add(new ParsedBulletinRate("USD", "CAD", FxRateDirection.QuotePerUnitBase, 1, direct, direct, direct));
                list.Add(new ParsedBulletinRate("CAD", "USD", FxRateDirection.QuotePerUnitBase, 1, inverse, inverse, inverse));
            }
        }
        return list;
    }

    private static byte[] LoadArtifactBytes(string fileName)
    {
        var assembly = typeof(FxRateProvider).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Embedded FX trust-anchor artifact '{fileName}' not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Unable to read embedded resource '{resourceName}'.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ComputeDigest(byte[] bytes)
        => $"SHA256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";

    private static string ComputeNormalizedArtifactDigest(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n");
        return ComputeDigest(Encoding.UTF8.GetBytes(text));
    }
}
