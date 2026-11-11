using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games.Zomboid;

public static class ZomboidGraphics
{
	public const string Id = "zomboid";

	public static GameDefinition Definition { get; } = new GameDefinition
	{
		Id = Id,
		Name = "Project Zomboid",
		Presets = new string[4] { "Low", "Medium", "Medium-High", "Ultra" },
		Floors = GameGraphicsCatalog.SharedFloors(),
		Settings = new GraphicsSetting[7]
		{
			GameGraphicsCatalog.Slider("width", "Width", "Window or fullscreen width in pixels.", "zomboid", 800, 3840, 1, "1280", "1920", "1920", "2560"),
			GameGraphicsCatalog.Slider("height", "Height", "Window or fullscreen height in pixels.", "zomboid", 600, 2160, 1, "720", "1080", "1080", "1440"),
			GameGraphicsCatalog.Toggle("fullscreen", "Fullscreen", "Fills the display.", "zomboid", "false", "false", "true", "true"),
			GameGraphicsCatalog.Toggle("vsync", "VSync", "Locks frames to the display refresh.", "zomboid", "true", "true", "false", "false"),
			GameGraphicsCatalog.Slider("frameRate", "FPS cap", "Highest frame rate Zomboid will chase.", "zomboid", 30, 240, 1, "30", "60", "120", "144"),
			GameGraphicsCatalog.Slider("maxTextureSize", "Texture quality", "Higher uses more video memory.", "zomboid", 1, 4, 1, "1", "2", "3", "4"),
			GameGraphicsCatalog.Slider("lighting", "Lighting", "0 is simpler lighting. 3 is the heaviest.", "zomboid", 0, 3, 1, "0", "1", "2", "3")
		}
	};
}
