using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games.Minecraft;

public static class MinecraftGraphics
{
	public const string Id = "minecraft";

	public static GameDefinition Definition { get; } = new GameDefinition
	{
		Id = Id,
		Name = "Minecraft",
		Presets = new string[4] { "Low", "Medium", "Medium-High", "Ultra" },
		Floors = GameGraphicsCatalog.SharedFloors(),
		Settings = new GraphicsSetting[11]
		{
			GameGraphicsCatalog.Slider("renderDistance", "Render distance", "How many chunks Minecraft draws around you.", "options", 2, 32, 1, "8", "12", "16", "24"),
			GameGraphicsCatalog.Slider("simulationDistance", "Simulation distance", "How far mobs, crops, and redstone stay active.", "options", 2, 32, 1, "6", "8", "10", "12"),
			GameGraphicsCatalog.Choice("graphicsMode", "Graphics mode", "Fast, Fancy, or Fabulous. Fabulous is the heaviest.", "options", new string[3] { "0", "1", "2" }, "0", "1", "2", "2", new string[3] { "Fast", "Fancy", "Fabulous" }),
			GameGraphicsCatalog.Choice("particles", "Particles", "All shows every particle. Minimal is the lightest.", "options", new string[3] { "0", "1", "2" }, "2", "1", "1", "0", new string[3] { "All", "Decreased", "Minimal" }),
			GameGraphicsCatalog.Slider("entityDistanceScaling", "Entity distance", "How far entities are drawn. 1 is the usual distance.", "options", 0.25, 2, 0.25, "0.5", "0.75", "1", "1.25"),
			GameGraphicsCatalog.Slider("maxFps", "Max FPS", "Caps the frame rate. 260 leaves Minecraft uncapped for this pack.", "options", 30, 260, 10, "60", "120", "144", "260"),
			GameGraphicsCatalog.Toggle("enableVsync", "VSync", "Locks frames to the display. Off lets the FPS cap do the work.", "options", "false", "false", "false", "false"),
			GameGraphicsCatalog.Slider("mipmapLevels", "Mipmaps", "Sharper distant textures at the cost of memory.", "options", 0, 4, 1, "2", "4", "4", "4"),
			GameGraphicsCatalog.Slider("biomeBlendRadius", "Biome blend", "How smoothly grass and water colors blend. Higher is heavier.", "options", 0, 7, 1, "1", "2", "3", "5"),
			GameGraphicsCatalog.Slider("performance.chunk_builder_threads", "Sodium chunk threads", "0 lets Sodium choose. Written only when Sodium is installed.", "sodium", 0, 16, 1, "1", "2", "0", "0"),
			GameGraphicsCatalog.Slider("maxShadowRenderDistance", "Iris shadow distance", "Shadow chunks. Written only when Iris is installed.", "iris", 4, 32, 1, "8", "12", "16", "32")
		}
	};
}
