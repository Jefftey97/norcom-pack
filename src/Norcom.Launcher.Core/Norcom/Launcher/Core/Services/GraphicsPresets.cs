using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Norcom.Launcher.Core.Models;

namespace Norcom.Launcher.Core.Services;

public static class GraphicsPresets
{
	public const int UncappedFps = 260;

	public const string ResourcePacks = "[\"vanilla\",\"continuity:default\",\"continuity:glass_pane_culling_fix\",\"file/Patrix_26.3_32x_basic.zip\",\"file/Patrix_26.3_32x_addon.zip\",\"file/Patrix_26.3_models.zip\"]";

	public static GraphicsPreset For(GraphicsTier tier)
	{
		return tier switch
		{
			GraphicsTier.Low => new GraphicsPreset(tier, 6, 6, 260, "0.5", 6, "64.0", 0, 0, 0, 1, 0, 0, 0, 0, Pom: false, 16, "0.50", 16, 0, "64", "LOW", 1), 
			GraphicsTier.Medium => new GraphicsPreset(tier, 10, 8, 260, "0.75", 8, "96.0", 1, 2, 1, 2, 1, 1, 1, 64, Pom: true, 16, "0.50", 24, 1, "96", "LOW", 2), 
			GraphicsTier.MediumHigh => new GraphicsPreset(tier, 12, 10, 260, "1.0", 12, "128.0", 2, 2, 2, 2, 2, 2, 2, 128, Pom: true, 32, "0.75", 32, 1, "112", "MEDIUM", 2), 
			GraphicsTier.Ultra => new GraphicsPreset(tier, 20, 12, 260, "1.25", 16, "256.0", 3, 3, 3, 3, 3, 3, 3, 256, Pom: true, 128, "1.00", 48, 1, "192", "HIGH", 2), 
			_ => new GraphicsPreset(GraphicsTier.High, 14, 10, 260, "1.0", 14, "160.0", 2, 2, 2, 3, 2, 3, 2, 192, Pom: true, 64, "1.00", 32, 1, "128", "MEDIUM", 2), 
		};
	}

	public static string ShaderOptions(GraphicsPreset p)
	{
		string value = p.Tier switch
		{
			GraphicsTier.Low => "LOW", 
			GraphicsTier.Medium => "MEDIUM", 
			GraphicsTier.MediumHigh => "MEDIUM_HIGH", 
			GraphicsTier.Ultra => "ULTRA", 
			_ => "HIGH", 
		};
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("# storm-safe: WAVING_RAIN off, RAIN_PUDDLES=0, PUDDLE_VOXELIZATION off, IMPROVED_RAIN on");
		stringBuilder.Append("# NORCOM ").Append(value).AppendLine(" - Complementary Unbound r5.9.3");
		stringBuilder.Append("# Aligned: RD=").Append(p.RenderDistance).Append(", irisShadow=")
			.Append(p.IrisShadowChunks)
			.Append(", shadowDistance=")
			.Append(p.ShadowDistance)
			.Append(", DH=")
			.Append(p.DhRadius)
			.Append(' ')
			.Append(value)
			.AppendLine();
		stringBuilder.AppendLine("# Patrix POM + labPBR. No WSR. No motion blur. Particle Rain handles rain.");
		stringBuilder.Append("profile=").AppendLine(value);
		stringBuilder.AppendLine("RP_MODE=3");
		stringBuilder.Append("SHADOW_QUALITY=").Append(p.ShadowQuality).AppendLine();
		stringBuilder.Append("shadowDistance=").AppendLine(p.ShadowDistance);
		stringBuilder.Append("WATER_REFLECT_QUALITY=").Append(p.Water).AppendLine();
		stringBuilder.Append("BLOCK_REFLECT_QUALITY=").Append(p.BlockReflect).AppendLine();
		stringBuilder.Append("LIGHTSHAFT_QUALI_DEFINE=").Append(p.LightShaft).AppendLine();
		stringBuilder.Append("SSAO_QUALI_DEFINE=").Append(p.Ssao).AppendLine();
		stringBuilder.AppendLine((p.Tier == GraphicsTier.Low) ? "FXAA_DEFINE=0" : "FXAA_DEFINE=1");
		stringBuilder.Append("DETAIL_QUALITY=").Append(p.Detail).AppendLine();
		stringBuilder.Append("CLOUD_QUALITY=").Append(p.Cloud).AppendLine();
		stringBuilder.AppendLine("ANISOTROPIC_FILTER=0");
		stringBuilder.Append("COLORED_LIGHTING=").Append(p.ColoredLighting).AppendLine();
		stringBuilder.AppendLine("WORLD_SPACE_REFLECTIONS=-1");
		stringBuilder.AppendLine((p.Tier == GraphicsTier.Low) ? "ENTITY_SHADOW=0" : "ENTITY_SHADOW=1");
		stringBuilder.AppendLine("RAIN_PUDDLES=0");
		stringBuilder.Append("POM=").AppendLine(p.Pom ? "true" : "false");
		stringBuilder.Append("POM_DEPTH=").AppendLine(p.PomDepth);
		stringBuilder.Append("POM_QUALITY=").Append(p.PomQuality).AppendLine();
		stringBuilder.Append("POM_DISTANCE=").Append(p.PomDistance).AppendLine();
		stringBuilder.AppendLine("POM_LIGHTING_MODE=2");
		stringBuilder.AppendLine("NORMAL_MAP_STRENGTH=120");
		stringBuilder.AppendLine((p.Tier == GraphicsTier.Low) ? "DIRECTIONAL_BLOCKLIGHT=0" : "DIRECTIONAL_BLOCKLIGHT=3");
		stringBuilder.AppendLine("CUSTOM_EMISSION_INTENSITY=100");
		stringBuilder.AppendLine((p.Tier == GraphicsTier.Low) ? "BLOOM_ENABLED=0" : "BLOOM_ENABLED=1");
		stringBuilder.Append("TAA_DEFINE=").Append(p.Taa).AppendLine();
		stringBuilder.AppendLine("TAA_SMOOTHING=3");
		stringBuilder.AppendLine("TAA_JITTER=2");
		stringBuilder.AppendLine("WAVING_FOLIAGE=true");
		stringBuilder.AppendLine("WAVING_LEAVES=true");
		stringBuilder.AppendLine("WAVING_WATER_VERTEX=true");
		stringBuilder.AppendLine("WAVING_RAIN=false");
		stringBuilder.AppendLine("WATER_BUMPINESS=1.25");
		stringBuilder.AppendLine("MOTION_BLUR_EFFECT=0");
		stringBuilder.AppendLine("PUDDLE_VOXELIZATION=false");
		stringBuilder.AppendLine("IMPROVED_RAIN_DEFINE=1");
		return stringBuilder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	public static string TierLabel(GraphicsTier tier)
	{
		if (tier == GraphicsTier.MediumHigh)
		{
			return "Medium High";
		}
		return tier.ToString();
	}

	public static string Hint(GraphicsPreset p)
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(20, 2, invariantCulture);
		handler.AppendLiteral("RD ");
		handler.AppendFormatted(p.RenderDistance);
		handler.AppendLiteral(" · DH ");
		handler.AppendFormatted(p.DhRadius);
		handler.AppendLiteral(" · uncapped");
		return string.Create(invariantCulture, ref handler);
	}
}
