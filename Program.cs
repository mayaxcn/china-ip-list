using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace china_ip_list;

/// <summary>
/// Builds mainland-China IP ranges from APNIC delegated statistics and the
/// more-specific APNIC WHOIS records. The latter is required for resources
/// such as Alibaba's 8.128.0.0/10, where the parent allocation is registered
/// to SG but a nested inetnum is registered to CN.
/// </summary>
internal static class Program
{
    private const string DelegatedApnicUrl =
        "https://ftp.apnic.net/apnic/stats/apnic/delegated-apnic-latest";
    private const string ApnicWhoisIpv4Url =
        "https://ftp.apnic.net/apnic/whois/apnic.db.inetnum.gz";
    private const string ApnicWhoisIpv6Url =
        "https://ftp.apnic.net/apnic/whois/apnic.db.inet6num.gz";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
        DefaultRequestHeaders =
        {
            { "User-Agent", "china-ip-list/1.2 (+https://github.com/mayaxcn/china-ip-list)" },
        },
    };

    private static async Task<int> Main()
    {
        try
        {
            var outDir = AppContext.BaseDirectory;
            Directory.CreateDirectory(outDir);

            var ipv4 = new List<AddressRange>();
            var ipv6 = new List<AddressRange>();

            var delegated = await DownloadTextAsync(DelegatedApnicUrl);
            var delegatedCounts = ParseDelegated(delegated, ipv4, ipv6);

            // Delegated statistics describe top-level allocations. APNIC WHOIS
            // also contains current more-specific inetnum/inet6num records;
            // those records capture reassigned CN blocks inside a non-CN
            // parent allocation.
            var whoisIpv4Count = await ParseWhoisAsync(
                ApnicWhoisIpv4Url, "inetnum", 32, ipv4);
            var whoisIpv6Count = await ParseWhoisAsync(
                ApnicWhoisIpv6Url, "inet6num", 128, ipv6);

            var routesV4 = BuildCidrs(ipv4, 32);
            var routesV6 = BuildCidrs(ipv6, 128);
            ValidateCidrs(routesV4, 32);
            ValidateCidrs(routesV6, 128);

            await Task.WhenAll(
                WriteAsync(Path.Combine(outDir, "chnroute.txt"), routesV4),
                WriteAsync(Path.Combine(outDir, "chn_ip.txt"), BuildRanges(routesV4)),
                WriteAsync(Path.Combine(outDir, "chnroute_v6.txt"), routesV6),
                WriteAsync(Path.Combine(outDir, "chn_ip_v6.txt"), BuildRanges(routesV6)));

            Console.WriteLine(
                $"delegated v4: {delegatedCounts.v4}, v6: {delegatedCounts.v6}; " +
                $"WHOIS fallback v4: {whoisIpv4Count}, v6: {whoisIpv6Count}; " +
                $"output v4: {routesV4.Count}, v6: {routesV6.Count} -> {outDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] {ex.Message}");
            return 2;
        }
    }

    private static (int v4, int v6) ParseDelegated(
        string content, List<AddressRange> ipv4, List<AddressRange> ipv6)
    {
        var v4Count = 0;
        var v6Count = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;

            var fields = line.Split('|');
            if (fields.Length < 7 || fields[0] != "apnic" || fields[1] != "CN")
                continue;
            if (!IsAllocatedOrAssigned(fields[6])) continue;

            if (fields[2] == "ipv4")
            {
                if (!BigInteger.TryParse(fields[4], out var count) || count <= 0 ||
                    count > (BigInteger.One << 32))
                    throw new InvalidDataException($"Invalid APNIC IPv4 record: {line}");

                var start = ToBigInteger(IPAddress.Parse(fields[3]));
                var end = start + count - 1;
                if (start < 0 || end >= (BigInteger.One << 32))
                    throw new InvalidDataException($"APNIC IPv4 record is outside address space: {line}");

                ipv4.Add(new AddressRange(start, end));
                v4Count++;
            }
            else if (fields[2] == "ipv6")
            {
                if (!int.TryParse(fields[4], out var prefix) || prefix < 0 || prefix > 128)
                    throw new InvalidDataException($"Invalid APNIC IPv6 record: {line}");

                var start = ToBigInteger(IPAddress.Parse(fields[3]));
                var end = start + (BigInteger.One << (128 - prefix)) - 1;
                if (start < 0 || end >= (BigInteger.One << 128))
                    throw new InvalidDataException($"APNIC IPv6 record is outside address space: {line}");

                ipv6.Add(new AddressRange(start, end));
                v6Count++;
            }
        }

        return (v4Count, v6Count);
    }

    private static async Task<int> ParseWhoisAsync(
        string url, string addressField, int addressBits, List<AddressRange> output)
    {
        using var response = await GetResponseAsync(url);
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        await using var gzip = new GZipStream(responseStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        string? rangeText = null;
        string? country = null;
        string? status = null;
        var count = 0;

        void FlushRecord()
        {
            if (rangeText is not null &&
                string.Equals(country, "CN", StringComparison.OrdinalIgnoreCase) &&
                IsAllocatedOrAssigned(status) &&
                TryParseWhoisRange(rangeText, addressBits, out var range))
            {
                output.Add(range);
                count++;
            }

            rangeText = null;
            country = null;
            status = null;
        }

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0)
            {
                FlushRecord();
                continue;
            }

            if (line.StartsWith(addressField + ":", StringComparison.Ordinal))
                rangeText = line[(addressField.Length + 1)..].Trim();
            else if (line.StartsWith("country:", StringComparison.Ordinal))
                country = line[8..].Trim();
            else if (line.StartsWith("status:", StringComparison.Ordinal))
                status = line[7..].Trim();
        }

        FlushRecord();
        return count;
    }

    private static bool TryParseWhoisRange(
        string text, int addressBits, out AddressRange range)
    {
        range = default;
        try
        {
            if (addressBits == 32)
            {
                var parts = text.Split('-', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) return false;

                range = new AddressRange(
                    ToBigInteger(IPAddress.Parse(parts[0])),
                    ToBigInteger(IPAddress.Parse(parts[1])));
                return range.Start <= range.End;
            }

            var slash = text.LastIndexOf('/');
            if (slash <= 0 || !int.TryParse(text[(slash + 1)..], out var prefix) ||
                prefix < 0 || prefix > 128)
                return false;

            var address = ToBigInteger(IPAddress.Parse(text[..slash]));
            var blockSize = BigInteger.One << (128 - prefix);
            var start = address / blockSize * blockSize;
            range = new AddressRange(start, start + blockSize - 1);
            if (start < 0 || range.End >= (BigInteger.One << 128)) return false;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsAllocatedOrAssigned(string? status)
        => status is not null &&
           (status.StartsWith("ALLOCATED", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("ASSIGNED", StringComparison.OrdinalIgnoreCase));

    private static List<string> BuildCidrs(List<AddressRange> input, int addressBits)
    {
        var result = new List<string>();
        ValidateRanges(input, addressBits);
        foreach (var range in MergeRanges(input))
        {
            var start = range.Start;
            while (start <= range.End)
            {
                var alignment = start.IsZero
                    ? BigInteger.One << addressBits
                    : start & -start;
                var remaining = range.End - start + 1;
                var blockSize = BigInteger.Min(alignment, HighestPowerOfTwo(remaining));
                var prefix = addressBits - PowerOfTwoExponent(blockSize);
                result.Add($"{FormatAddress(start, addressBits)}/{prefix}");
                start += blockSize;
            }
        }
        return result;
    }

    private static void ValidateRanges(List<AddressRange> ranges, int addressBits)
    {
        var maximum = (BigInteger.One << addressBits) - 1;
        foreach (var range in ranges)
        {
            if (range.Start < 0 || range.Start > range.End || range.End > maximum)
                throw new InvalidDataException(
                    $"Invalid {addressBits}-bit range: {range.Start}-{range.End}");
        }
    }

    private static List<AddressRange> MergeRanges(List<AddressRange> input)
    {
        var result = new List<AddressRange>();
        foreach (var range in input.OrderBy(x => x.Start).ThenBy(x => x.End))
        {
            if (result.Count > 0 && range.Start <= result[^1].End + 1)
            {
                var previous = result[^1];
                result[^1] = new AddressRange(
                    previous.Start, BigInteger.Max(previous.End, range.End));
            }
            else
            {
                result.Add(range);
            }
        }
        return result;
    }

    private static List<string> BuildRanges(List<string> cidrs)
    {
        var result = new List<string>(cidrs.Count);
        foreach (var cidr in cidrs)
        {
            var slash = cidr.LastIndexOf('/');
            var address = IPAddress.Parse(cidr[..slash]);
            var prefix = int.Parse(cidr[(slash + 1)..]);
            var bits = address.AddressFamily ==
                System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            var start = ToBigInteger(address);
            var end = start + (BigInteger.One << (bits - prefix)) - 1;
            result.Add($"{FormatAddress(start, bits)} {FormatAddress(end, bits)}");
        }
        return result;
    }

    private static void ValidateCidrs(List<string> cidrs, int addressBits)
    {
        BigInteger? previousEnd = null;
        foreach (var cidr in cidrs)
        {
            var slash = cidr.LastIndexOf('/');
            if (slash <= 0 || !int.TryParse(cidr[(slash + 1)..], out var prefix) ||
                prefix < 0 || prefix > addressBits)
                throw new InvalidDataException($"Invalid generated CIDR: {cidr}");

            var start = ToBigInteger(IPAddress.Parse(cidr[..slash]));
            var end = start + (BigInteger.One << (addressBits - prefix)) - 1;
            if (previousEnd.HasValue && start <= previousEnd.Value)
                throw new InvalidDataException($"Overlapping generated CIDRs near {cidr}");
            previousEnd = end;
        }
    }

    private static async Task<HttpResponseMessage> GetResponseAsync(string url)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Console.Error.WriteLine($"[WARN] attempt {attempt}/3: {ex.Message}");
                if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(15 * attempt));
            }
        }

        throw new HttpRequestException($"Unable to download {url}");
    }

    private static async Task<string> DownloadTextAsync(string url)
    {
        using var response = await GetResponseAsync(url);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task WriteAsync(string path, IEnumerable<string> lines)
        => await File.WriteAllLinesAsync(path, lines);

    private static BigInteger HighestPowerOfTwo(BigInteger value)
    {
        var result = BigInteger.One;
        while ((result << 1) <= value) result <<= 1;
        return result;
    }

    private static int PowerOfTwoExponent(BigInteger value)
    {
        var exponent = 0;
        while (value > 1)
        {
            value >>= 1;
            exponent++;
        }
        return exponent;
    }

    private static BigInteger ToBigInteger(IPAddress address)
    {
        var result = BigInteger.Zero;
        foreach (var value in address.GetAddressBytes()) result = (result << 8) | value;
        return result;
    }

    private static string FormatAddress(BigInteger value, int addressBits)
    {
        var bytes = new byte[addressBits / 8];
        for (var i = bytes.Length - 1; i >= 0; i--)
        {
            bytes[i] = (byte)(value & 255);
            value >>= 8;
        }
        return new IPAddress(bytes).ToString();
    }

    private readonly record struct AddressRange(BigInteger Start, BigInteger End);
}
