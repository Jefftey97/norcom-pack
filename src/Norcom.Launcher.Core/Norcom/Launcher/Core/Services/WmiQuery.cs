#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Norcom.Launcher.Core.Services;

// Core does not reference System.Management directly; the shipped launcher does.
// ManagementObjectSearcher.Get has two public overloads (Get() and
// Get(ManagementOperationObserver)), so the lookup must name the empty
// signature. An unqualified GetMethod("Get") throws AmbiguousMatchException.
public static class WmiQuery
{
	public const string SearcherTypeName = "System.Management.ManagementObjectSearcher, System.Management";

	public static MethodInfo? ParameterlessGet(Type searcherType)
	{
		return searcherType.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
	}

	// Returns null when WMI is not available on this platform/build.
	public static List<Dictionary<string, object?>>? Rows(string query, params string[] fields)
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}
		Type? searcherType = Type.GetType(SearcherTypeName);
		if (searcherType == null)
		{
			return null;
		}
		MethodInfo get = ParameterlessGet(searcherType) ?? throw new MissingMethodException(searcherType.FullName, "Get");
		using IDisposable searcher = (IDisposable)Activator.CreateInstance(searcherType, query)!;
		using IDisposable collection = (IDisposable)get.Invoke(searcher, null)!;
		List<Dictionary<string, object?>> rows = new List<Dictionary<string, object?>>();
		foreach (object item in (IEnumerable)collection)
		{
			try
			{
				PropertyInfo? indexer = item.GetType().GetProperty("Item", typeof(object), new Type[] { typeof(string) });
				Dictionary<string, object?> row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
				foreach (string field in fields)
				{
					row[field] = indexer?.GetValue(item, new object[] { field });
				}
				rows.Add(row);
			}
			finally
			{
				(item as IDisposable)?.Dispose();
			}
		}
		return rows;
	}

	public static string Text(Dictionary<string, object?> row, string field)
	{
		return row.TryGetValue(field, out object? value) ? value?.ToString() ?? "" : "";
	}

	public static long Number(Dictionary<string, object?> row, string field)
	{
		if (!row.TryGetValue(field, out object? value) || value is not IConvertible convertible)
		{
			return 0L;
		}
		try
		{
			return convertible.ToInt64(System.Globalization.CultureInfo.InvariantCulture);
		}
		catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
		{
			return 0L;
		}
	}
}
