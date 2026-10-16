using System.Globalization;
using System.Text.RegularExpressions;

namespace DistributedRank;

/// <summary>Loads a square matrix from a text file and prints it to the console.</summary>
internal static class MatrixLoader
{
    /// <summary>
    /// Parse a text file into a square double[][] matrix.
    /// Supports space, comma and semicolon delimiters; ignores blank lines and # comments.
    /// </summary>
    internal static double[][] Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Matrix input file was not found.", path);
        string[] lines = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        if (lines.Length == 0) throw new InvalidDataException("Matrix input is empty.");

        double[][] matrix = lines.Select((line, rowIndex) =>
        {
            string[] fields = Regex.Split(line, @"[\s,;]+", RegexOptions.CultureInvariant);
            double[] values = fields
                .Select(field => double.Parse(field, NumberStyles.Float, CultureInfo.InvariantCulture))
                .ToArray();
            if (values.Any(value => !double.IsFinite(value)))
                throw new InvalidDataException($"Row {rowIndex + 1}: values must be finite.");
            if (values.Length != lines.Length)
                throw new InvalidDataException(
                    $"Matrix must be square: row {rowIndex + 1} has {values.Length} values; expected {lines.Length}.");
            return values;
        }).ToArray();
        return matrix;
    }

    /// <summary>Print up to 6×6 top-left corner of the matrix.</summary>
    internal static void Print(double[][] matrix)
    {
        int shownRows = Math.Min(matrix.Length, 6);
        int shownColumns = Math.Min(matrix.Length, 6);
        Console.WriteLine(shownRows == matrix.Length
            ? "Matrix:"
            : $"Matrix (top-left {shownRows} x {shownColumns}):");
        for (int i = 0; i < shownRows; i++)
        {
            string values = string.Join("  ", matrix[i].Take(shownColumns)
                .Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));
            if (shownColumns < matrix[i].Length) values += "  …";
            Console.WriteLine("  " + values);
        }
        if (shownRows < matrix.Length) Console.WriteLine("  …");
    }
}
