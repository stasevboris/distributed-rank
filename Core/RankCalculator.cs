namespace DistributedRank;

/// <summary>Computes the numerical rank of a matrix using Gaussian elimination with partial pivoting.</summary>
internal static class RankCalculator
{
    /// <summary>
    /// Compute rank by Gaussian elimination with column-wise partial pivoting.
    /// Elements with |value| &lt;= epsilon are treated as zero.
    /// </summary>
    internal static int ComputeRank(double[][] source, double epsilon)
    {
        double[][] matrix = source.Select(row => row.ToArray()).ToArray();
        int n = matrix.Length;
        int[] rowIds = Enumerable.Range(0, n).ToArray();
        int rank = 0;
        for (int column = 0; column < n && rank < n; column++)
        {
            int pivot = rank;
            for (int row = rank + 1; row < n; row++)
                if (Math.Abs(matrix[row][column]) > Math.Abs(matrix[pivot][column])
                    || (Math.Abs(matrix[row][column]) == Math.Abs(matrix[pivot][column])
                        && rowIds[row] < rowIds[pivot])) pivot = row;
            if (Math.Abs(matrix[pivot][column]) <= epsilon) continue;

            (matrix[rank], matrix[pivot]) = (matrix[pivot], matrix[rank]);
            (rowIds[rank], rowIds[pivot]) = (rowIds[pivot], rowIds[rank]);
            double pivotValue = matrix[rank][column];
            for (int row = rank + 1; row < n; row++)
            {
                double factor = matrix[row][column] / pivotValue;
                if (factor == 0) continue;
                matrix[row][column] = 0;
                for (int j = column + 1; j < n; j++) matrix[row][j] -= factor * matrix[rank][j];
            }
            rank++;
        }
        return rank;
    }
}
