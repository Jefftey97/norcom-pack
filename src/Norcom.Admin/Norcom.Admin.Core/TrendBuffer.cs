using System.Globalization;

namespace Norcom.Admin.Core;

public sealed class TrendBuffer
{
    public const int MaxSamples = 48;

    private readonly Queue<double> _samples = new();

    public int Count => _samples.Count;

    public void Add(double value)
    {
        _samples.Enqueue(value);
        while (_samples.Count > MaxSamples)
        {
            _samples.Dequeue();
        }
    }

    public string Strip()
    {
        if (_samples.Count == 0)
        {
            return "";
        }

        var values = _samples.ToArray();
        var min = values.Min();
        var max = values.Max();
        var span = max - min;
        const string bars = "▁▂▃▄▅▆▇█";
        var chars = new char[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var index = span <= 0.0000001
                ? bars.Length - 1
                : (int)Math.Round((values[i] - min) / span * (bars.Length - 1));
            chars[i] = bars[Math.Clamp(index, 0, bars.Length - 1)];
        }

        return new string(chars) + "  " + values[^1].ToString("0.0", CultureInfo.InvariantCulture);
    }
}
