using Norcom.Launcher.Core.Models;

namespace Norcom.Launcher.Core.Services;

public sealed record GraphicsPreset(GraphicsTier Tier, int RenderDistance, int SimulationDistance, int MaxFps, string EntityDistance, int IrisShadowChunks, string ShadowDistance, int ShadowQuality, int Ssao, int Cloud, int Detail, int Water, int BlockReflect, int LightShaft, int ColoredLighting, bool Pom, int PomQuality, string PomDepth, int PomDistance, int Taa, string DhRadius, string DhQuality, int Particles);
