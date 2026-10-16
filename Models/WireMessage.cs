namespace DistributedRank;

/// <summary>JSON message exchanged between master and worker over TCP.</summary>
internal sealed class WireMessage
{
    public string Type { get; set; } = string.Empty;
    public string? WorkerId { get; set; }
    public string? Platform { get; set; }
    public int? Column { get; set; }
    public int? RowId { get; set; }
    public double Value { get; set; }
    public double[]? Values { get; set; }
    public List<RowMessage>? Rows { get; set; }
}
