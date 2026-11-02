using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutodeskMcp.Bridge;

/// <summary>
/// Length-prefixed UTF-8 framing: [int32 little-endian byte length][payload].
/// Used instead of newline framing so payloads (e.g. compiled script assemblies) can be large.
/// </summary>
public static class Framing
{
    public const int MaxMessageBytes = 256 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, string json, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <returns>The message, or null if the peer closed the stream before sending a header.</returns>
    public static async Task<string?> ReadAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4];
        int read = 0;
        while (read < 4)
        {
            int n = await stream.ReadAsync(header.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0) return null;
                throw new EndOfStreamException("Pipe closed in the middle of a message header.");
            }
            read += n;
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxMessageBytes)
            throw new InvalidDataException($"Invalid message length {length}.");

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(payload);
    }
}
