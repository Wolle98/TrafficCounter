namespace TrafficCounter.Tracking;

/// <summary>Result of an assignment: matched pairs plus the rows and columns left over.</summary>
public sealed record AssignmentResult(IReadOnlyList<(int Row, int Column)> Matches, IReadOnlyList<int> UnmatchedRows, IReadOnlyList<int> UnmatchedColumns);

/// <summary>
/// Minimum-cost bipartite matching (Hungarian algorithm, O(n²m)). Pairs whose cost exceeds the threshold
/// are never matched.
/// </summary>
public static class LinearAssignment
{
    public static AssignmentResult Solve(double[,] cost, double threshold)
    {
        int rows = cost.GetLength(0);
        int columns = cost.GetLength(1);
        if (rows == 0 || columns == 0)
        {
            return new AssignmentResult([], [.. Enumerable.Range(0, rows)], [.. Enumerable.Range(0, columns)]);
        }

        // Forbidden pairs get a cost that no allowed assignment can reach, then are filtered out afterwards.
        double forbidden = (threshold + 1) * Math.Max(rows, columns) + 1;
        bool transpose = rows > columns;
        int n = transpose ? columns : rows;
        int m = transpose ? rows : columns;
        var matrix = new double[n, m];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                double value = transpose ? cost[j, i] : cost[i, j];
                matrix[i, j] = value > threshold ? forbidden : value;
            }
        }

        int[] rowToColumn = Hungarian(matrix);

        List<(int Row, int Column)> matches = [];
        bool[] rowMatched = new bool[rows];
        bool[] columnMatched = new bool[columns];
        for (int i = 0; i < n; i++)
        {
            int j = rowToColumn[i];
            (int row, int column) = transpose ? (j, i) : (i, j);
            if (cost[row, column] <= threshold)
            {
                matches.Add((row, column));
                rowMatched[row] = columnMatched[column] = true;
            }
        }

        return new AssignmentResult(
            [.. matches.OrderBy(p => p.Row)],
            [.. Enumerable.Range(0, rows).Where(r => !rowMatched[r])],
            [.. Enumerable.Range(0, columns).Where(c => !columnMatched[c])]);
    }

    /// <summary>Classic potentials-based Hungarian algorithm for n ≤ m; returns the column of each row.</summary>
    private static int[] Hungarian(double[,] a)
    {
        int n = a.GetLength(0);
        int m = a.GetLength(1);
        var u = new double[n + 1];
        var v = new double[m + 1];
        var p = new int[m + 1];
        var way = new int[m + 1];

        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            var minValue = new double[m + 1];
            var used = new bool[m + 1];
            Array.Fill(minValue, double.PositiveInfinity);
            do
            {
                used[j0] = true;
                int i0 = p[j0];
                double delta = double.PositiveInfinity;
                int j1 = 0;
                for (int j = 1; j <= m; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    double current = a[i0 - 1, j - 1] - u[i0] - v[j];
                    if (current < minValue[j])
                    {
                        minValue[j] = current;
                        way[j] = j0;
                    }

                    if (minValue[j] < delta)
                    {
                        delta = minValue[j];
                        j1 = j;
                    }
                }

                for (int j = 0; j <= m; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minValue[j] -= delta;
                    }
                }

                j0 = j1;
            }
            while (p[j0] != 0);

            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            }
            while (j0 != 0);
        }

        var result = new int[n];
        for (int j = 1; j <= m; j++)
        {
            if (p[j] != 0)
            {
                result[p[j] - 1] = j - 1;
            }
        }

        return result;
    }
}
