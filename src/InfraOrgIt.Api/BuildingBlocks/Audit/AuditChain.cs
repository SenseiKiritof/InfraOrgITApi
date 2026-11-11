using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;

namespace InfraOrgIt.Api.BuildingBlocks.Audit;

/// <summary>
/// Hash chain for audit rows. Each row hashes its own fields plus the previous
/// row's hash, so changing any stored field breaks the chain from that row on.
///
/// Caveats:
/// - This is tamper-evident, not WORM. A database writer can still rewrite the
///   whole suffix by recomputing every hash; an external anchor/SIEM export is
///   required for stronger guarantees.
/// - The retention service deletes old rows. Verification treats the oldest
///   remaining hashed row as the chain start, so deleting a prefix cannot be
///   detected; deleting a suffix leaves a valid-looking shorter chain.
/// - Rows written before the migration have no hash and are skipped. The first
///   new row chains from <see cref="Genesis"/>.
/// - Writes are serialized in-process (see <c>AuditService</c>). A multi-instance
///   deployment needs a database lock or a single-writer queue, otherwise two
///   processes can chain from the same predecessor.
/// </summary>
public static class AuditChain
{
    public const string Genesis = "genesis";

    private static readonly Encoding Utf8 = Encoding.UTF8;

    /// <summary>
    /// MySQL stores <c>datetime(6)</c>: truncate the 100 ns tick so the value
    /// hashed before saving is exactly the value read back for verification.
    /// </summary>
    public static DateTime NormalizeTimestamp(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, value.Kind);

    public static string Compute(AuditEvent row, string previousHash) =>
        Convert.ToHexString(
                SHA256.HashData(Utf8.GetBytes(Canonical(row, previousHash))))
            .ToLowerInvariant();

    private static string Canonical(AuditEvent row, string previousHash)
    {
        var canonical = new StringBuilder(512);
        canonical.Append("v1|");
        Append(canonical, row.OccurredAt.Ticks);
        Append(canonical, row.Kind);
        Append(canonical, row.ActorUserId);
        Append(canonical, row.ActorUsername);
        Append(canonical, row.ClientId);
        Append(canonical, row.Action);
        Append(canonical, row.TargetType);
        Append(canonical, row.TargetId);
        Append(canonical, row.ServerId);
        Append(canonical, row.TargetHost);
        Append(canonical, row.TargetPort);
        Append(canonical, row.LocalPort);
        Append(canonical, row.Outcome);
        Append(canonical, row.ErrorCode);
        Append(canonical, row.DurationMs);
        Append(canonical, row.CorrelationId);
        Append(canonical, row.DetailJson);
        Append(canonical, previousHash);
        return canonical.ToString();
    }

    // Length prefixes keep values that contain separators unambiguous.
    private static void Append(StringBuilder canonical, string? value)
    {
        if (value is null)
            canonical.Append('~');
        else
            canonical.Append(value.Length).Append(':').Append(value);
        canonical.Append('|');
    }

    private static void Append(StringBuilder canonical, long value) =>
        canonical.Append(value.ToString(CultureInfo.InvariantCulture)).Append('|');

    private static void Append(StringBuilder canonical, int value) =>
        canonical.Append(value.ToString(CultureInfo.InvariantCulture)).Append('|');

    private static void Append(StringBuilder canonical, int? value) =>
        canonical.Append(
            value?.ToString(CultureInfo.InvariantCulture) ?? "~").Append('|');
}
