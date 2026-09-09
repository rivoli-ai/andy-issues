namespace Andy.Issues.Infrastructure.Estimation;

/// <summary>Ridge fit in log space, calibrated with empirical residual quantiles.</summary>
public sealed record QuantileModel(double[] Cost, double[] Hours, double CostP50, double CostP90, double HoursP50, double HoursP90)
{
    public static QuantileModel Train(IReadOnlyList<(double[] X, double Cost, double Hours)> rows)
    {
        var cost = Fit(rows.Select(r => (r.X, Math.Log(1 + r.Cost))).ToArray());
        var hours = Fit(rows.Select(r => (r.X, Math.Log(1 + r.Hours))).ToArray());
        var cr = rows.Select(r => Math.Log(1 + r.Cost) - Dot(cost, r.X)).Order().ToArray();
        var hr = rows.Select(r => Math.Log(1 + r.Hours) - Dot(hours, r.X)).Order().ToArray();
        return new(cost, hours, Quantile(cr, .5), Quantile(cr, .9), Quantile(hr, .5), Quantile(hr, .9));
    }
    public (double Cost50, double Cost90, double Hours50, double Hours90) Predict(double[] x) =>
        (Value(Dot(Cost, x) + CostP50), Value(Dot(Cost, x) + CostP90),
         Value(Dot(Hours, x) + HoursP50), Value(Dot(Hours, x) + HoursP90));
    private static double Value(double log) => Math.Max(0, Math.Exp(Math.Clamp(log, 0, 20)) - 1);
    private static double Quantile(double[] values, double q) => values[(int)Math.Ceiling(q * values.Length) - 1];
    private static double Dot(double[] a, double[] b) => a.Zip(b, (x, y) => x * y).Sum();
    private static double[] Fit((double[] X, double Y)[] rows)
    {
        const int n = EstimatorFeatures.Count;
        var a = new double[n, n + 1];
        foreach (var (x, y) in rows)
            for (var i = 0; i < n; i++)
            {
                a[i, n] += x[i] * y;
                for (var j = 0; j < n; j++) a[i, j] += x[i] * x[j];
            }
        for (var i = 0; i < n; i++) a[i, i] += i == 0 ? 1e-6 : 1;
        for (var k = 0; k < n; k++)
        {
            var pivot = k;
            for (var i = k + 1; i < n; i++) if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;
            for (var j = k; j <= n; j++) (a[k, j], a[pivot, j]) = (a[pivot, j], a[k, j]);
            var divisor = a[k, k];
            for (var j = k; j <= n; j++) a[k, j] /= divisor;
            for (var i = 0; i < n; i++)
            {
                if (i == k) continue;
                var factor = a[i, k];
                for (var j = k; j <= n; j++) a[i, j] -= factor * a[k, j];
            }
        }
        return Enumerable.Range(0, n).Select(i => a[i, n]).ToArray();
    }
}
