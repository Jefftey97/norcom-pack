using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Norcom.Launcher.Core.Models;

namespace Norcom.Launcher.Core.Services;

public static class ShaderScale
{
	private static readonly Dictionary<string, string> SliderDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["WATERCOLOR_R"] = "100",
		["COLORED_LIGHT_FOG_I"] = "0.65",
		["NETHER_STORM_LOWER_ALT"] = "28",
		["CLOUD_ALT1"] = "192",
		["MOON_PHASE_FULL"] = "1.00",
		["LIGHTSHAFT_SUNSET_SATURATION"] = "0.25"
	};

	public static string ReflectionRes(GraphicsTier tier)
	{
		if (tier == GraphicsTier.Ultra)
		{
			return "1.0";
		}
		return "0.5";
	}

	public static string Coerce(string shader, GraphicsTier tier)
	{
		if (string.IsNullOrEmpty(shader))
		{
			return shader ?? "";
		}
		string reflection = ReflectionRes(tier);
		string[] lines = shader.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
		StringBuilder builder = new StringBuilder(shader.Length + 32);
		for (int i = 0; i < lines.Length; i++)
		{
			if (i > 0)
			{
				builder.Append('\n');
			}
			builder.Append(CoerceLine(lines[i], reflection));
		}
		if ((shader.EndsWith("\n", StringComparison.Ordinal) || shader.EndsWith("\r", StringComparison.Ordinal)) && (builder.Length == 0 || builder[builder.Length - 1] != '\n'))
		{
			builder.Append('\n');
		}
		return builder.ToString();
	}

	private static string CoerceLine(string line, string reflection)
	{
		string trimmed = line.Trim();
		if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
		{
			return line;
		}
		int split = trimmed.IndexOf('=');
		if (split <= 0)
		{
			return line;
		}
		string key = trimmed.Substring(0, split).Trim();
		string value = trimmed.Substring(split + 1).Trim();
		if (key == "REFLECTION_RES")
		{
			if (!IsRelativeScale(value))
			{
				return "REFLECTION_RES=" + reflection;
			}
			return line;
		}
		if (SliderDefaults.TryGetValue(key, out string fallback) && !IsNumber(value))
		{
			return key + "=" + fallback;
		}
		return line;
	}

	public static bool IsRelativeScale(string value)
	{
		return value.Contains('.') && IsNumber(value);
	}

	public static bool IsNumber(string value)
	{
		return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
	}
}
