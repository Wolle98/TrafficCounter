namespace TrafficCounter.Tracking.Tests;

public class LinearAssignmentTests
{
    [Fact]
    public void Finds_minimum_total_cost_instead_of_greedy_choice()
    {
        // Greedy would take (0,0)=1 and then be forced to (1,1)=10; optimal is (0,1)+(1,0) = 2+2.
        double[,] cost = { { 1, 2 }, { 2, 10 } };

        AssignmentResult result = LinearAssignment.Solve(cost, threshold: 100);

        Assert.Equal([(0, 1), (1, 0)], result.Matches);
    }

    [Fact]
    public void Pairs_above_threshold_are_not_matched()
    {
        double[,] cost = { { 0.1, 0.9 }, { 0.95, 0.99 } };

        AssignmentResult result = LinearAssignment.Solve(cost, threshold: 0.8);

        Assert.Equal([(0, 0)], result.Matches);
        Assert.Equal([1], result.UnmatchedRows);
        Assert.Equal([1], result.UnmatchedColumns);
    }

    [Fact]
    public void Works_with_more_rows_than_columns()
    {
        double[,] cost = { { 5 }, { 1 }, { 3 } };

        AssignmentResult result = LinearAssignment.Solve(cost, threshold: 10);

        Assert.Equal([(1, 0)], result.Matches);
        Assert.Equal([0, 2], result.UnmatchedRows);
        Assert.Empty(result.UnmatchedColumns);
    }

    [Fact]
    public void Empty_input_leaves_everything_unmatched()
    {
        AssignmentResult result = LinearAssignment.Solve(new double[3, 0], 1);
        Assert.Empty(result.Matches);
        Assert.Equal([0, 1, 2], result.UnmatchedRows);
    }

    [Fact]
    public void Random_matrices_match_brute_force_optimum()
    {
        var random = new Random(42);
        for (int round = 0; round < 200; round++)
        {
            int rows = random.Next(1, 5);
            int columns = random.Next(1, 5);
            var cost = new double[rows, columns];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    cost[r, c] = random.Next(0, 20);
                }
            }

            AssignmentResult result = LinearAssignment.Solve(cost, threshold: 100);
            double total = result.Matches.Sum(m => cost[m.Row, m.Column]);

            Assert.Equal(Math.Min(rows, columns), result.Matches.Count);
            Assert.Equal(BruteForce(cost), total);
        }
    }

    private static double BruteForce(double[,] cost)
    {
        int rows = cost.GetLength(0);
        int columns = cost.GetLength(1);
        double best = double.MaxValue;
        void Search(int row, bool[] used, double sum, int assigned)
        {
            if (row == rows)
            {
                if (assigned == Math.Min(rows, columns))
                {
                    best = Math.Min(best, sum);
                }

                return;
            }

            Search(row + 1, used, sum, assigned); // leave row unassigned (only valid if rows > columns)
            for (int c = 0; c < columns; c++)
            {
                if (!used[c])
                {
                    used[c] = true;
                    Search(row + 1, used, sum + cost[row, c], assigned + 1);
                    used[c] = false;
                }
            }
        }

        Search(0, new bool[columns], 0, 0);
        return best;
    }
}
