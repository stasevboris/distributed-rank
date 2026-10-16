namespace DistributedRank;

/// <summary>A matrix row held by a worker, mutated during elimination.</summary>
internal sealed class WorkingRow(int id, double[] values)
{
    public int Id { get; } = id;
    public double[] Values { get; } = values;
}
