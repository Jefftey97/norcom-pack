using System.Collections.Generic;
using System.IO;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games.Minecraft;

public static class MinecraftSettings
{
	public static string Apply(SavedGraphicsProfile profile, string gameDir, string userProfile)
	{
		_ = userProfile;
		GameDefinition game = MinecraftGraphics.Definition;
		Dictionary<string, string> values = GameGraphicsCatalog.EffectiveValues(game, profile);
		List<string> wrote = new List<string>();
		string optionsError = GameSettingsWriter.WriteOptions(gameDir, GameSettingsWriter.ValuesFor(game, values, "options"), wrote);
		if (optionsError.Length > 0)
		{
			return optionsError;
		}
		if (GameSettingsWriter.ModInstalled(gameDir, "sodium"))
		{
			string sodiumError = GameSettingsWriter.WriteJson(gameDir, "config/sodium-options.json", GameSettingsWriter.ValuesFor(game, values, "sodium"), wrote);
			if (sodiumError.Length > 0)
			{
				return sodiumError;
			}
		}
		if (GameSettingsWriter.ModInstalled(gameDir, "iris"))
		{
			string irisError = GameSettingsWriter.WriteProperties(gameDir, "config/iris.properties", GameSettingsWriter.ValuesFor(game, values, "iris"), wrote);
			if (irisError.Length > 0)
			{
				return irisError;
			}
		}
		return GameSettingsWriter.Finish(game, profile, wrote);
	}
}
