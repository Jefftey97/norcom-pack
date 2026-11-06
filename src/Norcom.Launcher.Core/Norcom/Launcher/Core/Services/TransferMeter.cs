using System;
using System.Collections.Generic;
using System.Linq;

namespace Norcom.Launcher.Core.Services;

public sealed class TransferMeter
{
	private readonly object _gate = new object();

	private long _received;

	private long? _total;

	private string _label = "";

	private double _base;

	private double _span = 1.0;

	private readonly Queue<(long ticks, long bytes)> _window = new Queue<(long, long)>();

	public void SetPhase(string label, double baseFraction, double span)
	{
		lock (_gate)
		{
			_label = label;
			_base = baseFraction;
			_span = span;
			_received = 0L;
			_total = null;
			_window.Clear();
		}
	}

	public void Add(int count, long? total)
	{
		lock (_gate)
		{
			_received += count;
			if (total.HasValue && total.GetValueOrDefault() > 0)
			{
				_total = total;
			}
			long tickCount = Environment.TickCount64;
			_window.Enqueue((tickCount, _received));
			while (_window.Count > 0 && tickCount - _window.Peek().ticks > 1500)
			{
				_window.Dequeue();
			}
		}
	}

	public TransferSnapshot Snapshot()
	{
		lock (_gate)
		{
			double num = 0.0;
			if (_window.Count >= 2)
			{
				(long, long) tuple = _window.Peek();
				(long, long) tuple2 = _window.Last();
				double num2 = (double)(tuple2.Item1 - tuple.Item1) / 1000.0;
				if (num2 > 0.05)
				{
					num = (double)(tuple2.Item2 - tuple.Item2) / num2;
				}
			}
			double value = _base;
			TimeSpan? eta = null;
			long? total = _total;
			if (total.HasValue && total.GetValueOrDefault() > 0)
			{
				value = _base + _span * Math.Clamp((double)_received / (double)_total.Value, 0.0, 1.0);
				if (num > 1.0)
				{
					eta = TimeSpan.FromSeconds((double)Math.Max(0L, _total.Value - _received) / num);
				}
			}
			return new TransferSnapshot(_label, Math.Clamp(value, 0.0, 1.0), _received, _total, num, eta);
		}
	}

	public static string Format(TransferSnapshot s)
	{
		List<string> list = new List<string>();
		if (s.BytesPerSecond > 1.0)
		{
			list.Add(FormatBytes(s.BytesPerSecond) + "/s");
		}
		TimeSpan? eta = s.Eta;
		if (eta.HasValue)
		{
			TimeSpan valueOrDefault = eta.GetValueOrDefault();
			if (valueOrDefault.TotalSeconds >= 1.0 && valueOrDefault.TotalHours < 12.0)
			{
				list.Add((valueOrDefault.TotalMinutes >= 1.0) ? $"{(int)valueOrDefault.TotalMinutes}m {valueOrDefault.Seconds:00}s left" : $"{Math.Max(1, (int)valueOrDefault.TotalSeconds)}s left");
				goto IL_0158;
			}
		}
		bool flag = s.Received > 0;
		long? total;
		if (flag)
		{
			total = s.Total;
			bool flag2 = ((!total.HasValue || total.GetValueOrDefault() == 0L) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			list.Add("estimating time left");
		}
		goto IL_0158;
		IL_0158:
		total = s.Total;
		if (total.HasValue && total.GetValueOrDefault() > 0)
		{
			list.Add(FormatBytes(s.Received) + " / " + FormatBytes(s.Total.Value));
		}
		else if (s.Received > 0)
		{
			list.Add(FormatBytes(s.Received) + " downloaded");
		}
		if (list.Count != 0)
		{
			return s.Label + "  ·  " + string.Join("  ·  ", list);
		}
		return s.Label;
	}

	public static string FormatBytes(double n)
	{
		string[] array = new string[4] { "B", "KB", "MB", "GB" };
		int num = 0;
		while (n >= 1024.0 && num < array.Length - 1)
		{
			n /= 1024.0;
			num++;
		}
		if (num == 0)
		{
			return $"{n:0} {array[num]}";
		}
		return $"{n:0.0} {array[num]}";
	}
}
