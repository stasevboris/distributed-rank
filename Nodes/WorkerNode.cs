using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DistributedRank;

/// <summary>
/// Worker node: connects to the master over TCP, receives a partition of rows,
/// finds local pivot candidates, and performs elimination steps on command.
/// </summary>
internal static class WorkerNode
{
    internal static async Task<int> RunAsync(string[] args)
    {
        string host = CommandLine.GetOption(args, "--host", "127.0.0.1");
        string workerId = CommandLine.GetOption(args, "--id", $"worker-{Environment.MachineName}-{Environment.ProcessId}");
        int port = CommandLine.GetIntOption(args, "--port", 5000);
        using TcpClient client = await Protocol.ConnectWithRetryAsync(host, port);
        client.NoDelay = true;
        NetworkStream stream = client.GetStream();
        using var reader = Protocol.CreateReader(stream);
        using var writer = Protocol.CreateWriter(stream);

        await Protocol.SendMessageAsync(writer, new WireMessage
        {
            Type = "hello",
            WorkerId = workerId,
            Platform = RuntimeInformation.OSDescription
        });
        WorkingRow[] rows = [];
        var pivotedRows = new HashSet<int>();

        while (true)
        {
            WireMessage request = await Protocol.ReadMessageAsync(reader);
            if (request.Type == "stop") break;
            if (request.Type == "init")
            {
                if (request.Rows is null) throw new IOException("Initialization has no rows.");
                rows = request.Rows.Select(row => new WorkingRow(row.RowId, row.Values)).ToArray();
                pivotedRows.Clear();
                Console.WriteLine($"{workerId}: initialized with {rows.Length} row(s); connected to {host}:{port}.");
                await Protocol.SendMessageAsync(writer, new WireMessage { Type = "ready" });
                continue;
            }
            if (request.Type == "findPivot")
            {
                int column = request.Column ?? throw new IOException("Pivot request has no column.");
                WorkingRow? bestRow = null;
                double bestValue = 0;
                foreach (WorkingRow row in rows)
                {
                    if (pivotedRows.Contains(row.Id)) continue;
                    double value = row.Values[column];
                    if (Math.Abs(value) > Math.Abs(bestValue))
                    {
                        bestValue = value;
                        bestRow = row;
                    }
                }

                await Protocol.SendMessageAsync(writer, bestRow is null
                    ? new WireMessage { Type = "candidate", RowId = -1, Value = 0 }
                    : new WireMessage { Type = "candidate", RowId = bestRow.Id, Value = bestValue, Values = bestRow.Values });
                continue;
            }

            if (request.Type == "eliminate")
            {
                int column = request.Column ?? throw new IOException("Elimination request has no column.");
                int pivotId = request.RowId ?? throw new IOException("Elimination request has no pivot row.");
                double[] pivotValues = request.Values ?? throw new IOException("Elimination request has no pivot row values.");
                double pivotValue = pivotValues[column];
                if (pivotValue == 0) throw new IOException("Master selected a zero pivot.");

                foreach (WorkingRow row in rows)
                {
                    if (row.Id == pivotId)
                    {
                        pivotedRows.Add(pivotId);
                        continue;
                    }
                    if (pivotedRows.Contains(row.Id)) continue;

                    double factor = row.Values[column] / pivotValue;
                    if (factor == 0) continue;
                    for (int j = column + 1; j < row.Values.Length; j++)
                        row.Values[j] -= factor * pivotValues[j];
                    row.Values[column] = 0;
                }
                pivotedRows.Add(pivotId);
                await Protocol.SendMessageAsync(writer, new WireMessage { Type = "ack" });
                continue;
            }

            throw new IOException($"Unknown request type '{request.Type}'.");
        }

        Console.WriteLine($"{workerId}: completed.");
        return 0;
    }
}
