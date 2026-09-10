using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace china_ip_list;

/// <summary>
/// Pulls CN-mainland IP allocations from APNIC and writes four text files
/// (IPv4/IPv6 × CIDR/range). Designed to run a few times per day via GitHub Actions;
/// keeps the runtime minimal so it works on the free runner tier.
/// </summary>
internal static class Program
{
    private const string ApnicUrl =
        "https://ftp.apnic.net/apnic/stats/apnic/delegated-apnic-latest";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
        DefaultRequestHeaders =
        {
            { "User-Agent", "china-ip-list/1.1 (+https://github.com/mayaxcn/china-ip-list)" },
        },
    };

    private static async Task<int> Main()
    {
        var outDir = AppContext.BaseDirectory;
        Directory.CreateDirectory(outDir);

        var apnic = await FetchWithRetryAsync(ApnicUrl, retries: 3);
        if (string.IsNullOrWhiteSpace(apnic))
        {
            Console.Error.WriteLine("[FATAL] APNIC returned empty body after retries.");
            return 2;
        }

        var sbRouteV4 = new StringBuilder(capacity: 200_000);
        var sbRangeV4 = new StringBuilder(capacity: 400_000);
        var sbRouteV6 = new StringBuilder(capacity: 50_000);
        var sbRangeV6 = new StringBuilder(capacity: 100_000);

        int v4Count = 0, v6Count = 0;

        foreach (var line in apnic.Split('\n'))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("apnic|CN|ipv4|", StringComparison.Ordinal))
            {
                ParseV4(line, sbRouteV4, sbRangeV4);
                v4Count++;
            }
            else if (line.StartsWith("apnic|CN|ipv6|", StringComparison.Ordinal))
            {
                ParseV6(line, sbRouteV6, sbRangeV6);
                v6Count++;
            }
        }

        await Task.WhenAll(
            WriteAsync(Path.Combine(outDir, "chnroute.txt"),    sbRouteV4),
            WriteAsync(Path.Combine(outDir, "chn_ip.txt"),      sbRangeV4),
            WriteAsync(Path.Combine(outDir, "chnroute_v6.txt"), sbRouteV6),
            WriteAsync(Path.Combine(outDir, "chn_ip_v6.txt"),   sbRangeV6));

        Console.WriteLine($"v4: {v4Count}  v6: {v6Count}  -> {outDir}");
        return 0;
    }

    // apnic|CN|ipv4|<startIp>|<hostCount>|<date>|<status>
    private static void ParseV4(string line, StringBuilder routeSb, StringBuilder rangeSb)
    {
        var p = line.Split('|');
        var startIp   = p[3];
        var hostCount = int.Parse(p[4]);

        var startInt  = IpToUint(startIp);
        var endInt    = startInt + (uint)hostCount - 1;
        var prefixLen = 32 - BitOperations.TrailingZeroCount(hostCount);

        routeSb.Append(startIp).Append('/').Append(prefixLen).Append('\n');
        rangeSb.Append(startIp).Append(' ').Append(UintToIp(endInt)).Append('\n');
    }

    // apnic|CN|ipv6|<startIp>|<prefixLen>|<date>|<status>
    private static void ParseV6(string line, StringBuilder routeSb, StringBuilder rangeSb)
    {
        var p = line.Split('|');
        var startIp   = p[3];
        var prefixLen = int.Parse(p[4]);

        var startInt = IPv6ToBigInt(startIp);
        var hostBits = 128 - prefixLen;
        var endInt   = startInt + (BigInteger.One << hostBits) - BigInteger.One;

        routeSb.Append(startIp).Append('/').Append(prefixLen).Append('\n');
        rangeSb.Append(startIp).Append(' ').Append(BigIntToIPv6(endInt)).Append('\n');
    }

    private static async Task<string?> FetchWithRetryAsync(string url, int retries)
    {
        for (int i = 1; i <= retries; i++)
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Console.Error.WriteLine($"[WARN] attempt {i}/{retries}: {ex.Message}");
                if (i < retries) await Task.Delay(TimeSpan.FromSeconds(15 * i));
            }
        }
        return null;
    }

    private static async Task WriteAsync(string path, StringBuilder sb)
        => await File.WriteAllTextAsync(path, sb.ToString());

    private static uint IpToUint(string ip)
        => IPAddress.Parse(ip).GetAddressBytes()
              .Aggregate<byte, uint>(0, (acc, b) => (acc << 8) | b);

    private static string UintToIp(uint v) => new IPAddress(v).ToString();

    private static BigInteger IPv6ToBigInt(string ip)
    {
        var bytes = IPAddress.Parse(ip).GetAddressBytes();
        Array.Reverse(bytes);
        var padded = new byte[bytes.Length + 1]; // +1 zero byte keeps BigInteger unsigned
        Array.Copy(bytes, padded, bytes.Length);
        return new BigInteger(padded);
    }

    private static string BigIntToIPv6(BigInteger v)
    {
        var bytes = v.ToByteArray();
        Array.Reverse(bytes);
        // BigInteger may emit a sign byte when MSB is set (e.g. ::ffff:ffff:ffff:ffff).
        if (bytes.Length == 17) bytes = bytes.Take(16).ToArray();
        else if (bytes.Length < 16)
        {
            var padded = new byte[16];
            Array.Copy(bytes, 0, padded, 16 - bytes.Length, bytes.Length);
            bytes = padded;
        }
        return new IPAddress(bytes).ToString();
    }
}