using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Norcom.Launcher.Views;

public static class LauncherDialogs
{
	public static bool Confirm(string message, string title)
	{
		return Show(message, title, confirm: true) == true;
	}

	public static void Inform(string message, string title)
	{
		Show(message, title, confirm: false);
	}

	public static void DismissOwned(Window owner)
	{
		if (owner == null || Application.Current == null)
		{
			return;
		}
		foreach (Window window in Application.Current.Windows.OfType<Window>().ToList())
		{
			if (window is PromptWindow prompt && prompt.Owner == owner)
			{
				prompt.Decline();
			}
		}
	}

	private static bool? Show(string message, string title, bool confirm)
	{
		PromptWindow dialog = new PromptWindow(title, message, confirm);
		Window owner = OwnerWindow();
		if (owner != null)
		{
			dialog.Owner = owner;
			dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
			CancelEventHandler closing = (_, _) => dialog.Decline();
			owner.Closing += closing;
			try
			{
				return dialog.ShowDialog();
			}
			finally
			{
				owner.Closing -= closing;
			}
		}
		dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		return dialog.ShowDialog();
	}

	private static Window OwnerWindow()
	{
		if (Application.Current == null)
		{
			return null;
		}
		Window active = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive && window.IsVisible);
		return active ?? Application.Current.MainWindow;
	}
}
