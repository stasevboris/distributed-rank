using System.Net.Sockets;

namespace DistributedRank;

/// <summary>Wraps a TCP connection to a single worker node.</summary>
internal sealed class WorkerConnection(TcpClient client, StreamReader reader, StreamWriter writer) : IDisposable
{
    public TcpClient Client { get; } = client;
    public StreamReader Reader { get; } = reader;
    public StreamWriter Writer { get; } = writer;
    public string Id { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;

    public void Dispose()
    {
        Writer.Dispose();
        Reader.Dispose();
        Client.Dispose();
    }
}
