namespace DistributedRank;

/// <summary>A matrix row with its original index, used for init transfer.</summary>
internal sealed class RowMessage(int rowId, double[] values)
{
    public int RowId { get; } = rowId;
    public double[] Values { get; } = values;
}
