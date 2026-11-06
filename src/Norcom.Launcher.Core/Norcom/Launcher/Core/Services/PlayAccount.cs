using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class PlayAccount
{
	private static object? _view;

	public static void Install(object view)
	{
		_view = view;
	}

	public static void EnsureForPlay()
	{
		object? view = _view;
		if (view == null)
		{
			return;
		}
		try
		{
			FieldInfo? field = view.GetType().GetField("_signIn", BindingFlags.Instance | BindingFlags.NonPublic);
			object? signIn = field?.GetValue(view);
			MethodInfo? method = signIn?.GetType().GetMethod("ForPlayAsync");
			if (method == null)
			{
				return;
			}
			Task task = (Task)method.Invoke(signIn, new object[] { CancellationToken.None })!;
			task.ConfigureAwait(false).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play account " + ex.GetType().Name);
		}
	}
}
