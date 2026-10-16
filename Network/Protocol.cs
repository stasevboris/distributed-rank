using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DistributedRank;

/// <summary>Low-level helpers for the JSON-over-TCP wire protocol.</summary>
internal static class Protocol
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Serialize and send a single JSON line.</summary>
    internal static async Task SendMessageAsync(StreamWriter writer, WireMessage message)
    {
        string json = JsonSerializer.Serialize(message, JsonOptions);
        await writer.WriteLineAsync(json);
        await writer.FlushAsync();
    }

    /// <summary>Read and deserialize one JSON line (120-second timeout).</summary>
    internal static async Task<WireMessage> ReadMessageAsync(StreamReader reader)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        string? line = await reader.ReadLineAsync(timeout.Token);
        if (line is null) throw new EndOfStreamException("Connection closed before the next protocol message.");
        return JsonSerializer.Deserialize<WireMessage>(line, JsonOptions)
            ?? throw new IOException("Received an empty protocol message.");
    }

    /// <summary>Try to connect to master up to 60 times with 1-second intervals.</summary>
    internal static async Task<TcpClient> ConnectWithRetryAsync(string host, int port)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(2));
                return client;
            }
            catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
            {
                lastError = ex;
                client.Dispose();
                await Task.Delay(1000);
            }
        }
        throw new IOException($"Could not connect to master at {host}:{port} after 60 attempts.", lastError);
    }

    /// <summary>Create a UTF-8 StreamReader for a network stream.</summary>
    internal static StreamReader CreateReader(NetworkStream stream) =>
        new(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);

    /// <summary>Create an auto-flushing UTF-8 StreamWriter for a network stream.</summary>
    internal static StreamWriter CreateWriter(NetworkStream stream) =>
        new(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
}
