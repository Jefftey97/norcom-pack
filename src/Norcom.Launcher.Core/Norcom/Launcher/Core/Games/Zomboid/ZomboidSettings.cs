using System.Collections.Generic;
using System.IO;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games.Zomboid;

public static class ZomboidSettings
{
	public static string Apply(SavedGraphicsProfile profile, string gameDir, string userProfile)
	{
		_ = gameDir;
		GameDefinition game = ZomboidGraphics.Definition;
		Dictionary<string, string> values = GameGraphicsCatalog.EffectiveValues(game, profile);
		List<string> wrote = new List<string>();
		string folder = Path.Combine(userProfile, "Zomboid");
		if (!Directory.Exists(folder))
		{
			return "Project Zomboid settings were not written because " + folder + " does not exist.";
		}
		string error = GameSettingsWriter.WriteProperties(folder, "options.ini", GameSettingsWriter.ValuesFor(game, values, "zomboid"), wrote);
		if (error.Length > 0)
		{
			return error;
		}
		return GameSettingsWriter.Finish(game, profile, wrote);
	}
}
