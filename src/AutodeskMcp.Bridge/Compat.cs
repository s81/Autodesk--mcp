using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutodeskMcp.Bridge;

/// <summary>
/// Small helpers that behave the same on .NET Framework 4.8 (Revit/AutoCAD 2022-2024) and .NET 8/10
/// (2025-2027), so the shared code does not need conditional compilation at every call site.
/// </summary>
public static class Compat
{
    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    public static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

    public static bool ContainsIgnoreCase(string haystack, string needle) =>
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    public static int CurrentProcessId
    {
        get
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            return p.Id;
        }
    }

    public static string RandomHex(int bytes)
    {
        var buffer = new byte[bytes];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buffer);
        var sb = new StringBuilder(bytes * 2);
        foreach (byte b in buffer) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    /// <summary>Constant-time comparison, so timing does not reveal how much of a token matched.</summary>
    public static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    /// <summary>Atomically-enough replaces <paramref name="destination"/> with <paramref name="source"/>.</summary>
    public static void MoveOverwrite(string source, string destination)
    {
#if NETFRAMEWORK
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(source, destination);
#else
        File.Move(source, destination, overwrite: true);
#endif
    }

    public static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer, read, buffer.Length - read, ct).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Pipe closed in the middle of a message.");
            read += n;
        }
    }
}
