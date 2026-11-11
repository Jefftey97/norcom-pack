using System;
using System.Collections.Generic;
using Norcom.Launcher.Core.Games.Minecraft;
using Norcom.Launcher.Core.Games.Zomboid;
using Norcom.Launcher.Core.Services;

namespace Norcom.Launcher.Core.Games;

public static class GameHost
{
	private static readonly Dictionary<string, Func<string, bool>> Sessions = new Dictionary<string, Func<string, bool>>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, IGame> Games = new Dictionary<string, IGame>(StringComparer.OrdinalIgnoreCase);

	private static bool _ready;

	public static IGame Minecraft { get; } = new MinecraftGame();

	public static IGame Zomboid { get; } = new ZomboidGame();

	public static void Ensure()
	{
		if (_ready)
		{
			return;
		}
		_ready = true;
		Games[Minecraft.Id] = Minecraft;
		Games[Zomboid.Id] = Zomboid;
		Sessions[Minecraft.Id] = MinecraftRunGate.Running;
		Sessions[Zomboid.Id] = ZomboidSession.Running;
		GameGraphicsCatalog.Register(Minecraft.Board.Definition);
		GameGraphicsCatalog.Register(Zomboid.Board.Definition);
	}

	public static IGame Require(string id)
	{
		Ensure();
		if (Games.TryGetValue(id ?? "", out IGame game))
		{
			return game;
		}
		throw new InvalidOperationException("Unknown game profile: " + id);
	}

	public static bool SessionRunning(string id, string lockPath)
	{
		Ensure();
		if (Sessions.TryGetValue(id ?? "", out Func<string, bool> probe))
		{
			return probe(lockPath);
		}
		return false;
	}

	public static string Apply(GameDefinition game, SavedGraphicsProfile profile, string gameDir, string userProfile)
	{
		return Require(game.Id).Board.Apply(profile, gameDir, userProfile);
	}
}
