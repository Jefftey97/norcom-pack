using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Norcom.Launcher.Views;

public static class LauncherDialogs
{
	private const int WmClose = 0x0010;

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

	internal static event Action<Window> OwnerReady;

	internal static bool OwnerAcceptsClose(Window owner)
	{
		IntPtr handle = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
		return handle != IntPtr.Zero && IsWindowEnabled(handle);
	}

	internal static bool PostOwnerClose(Window owner)
	{
		IntPtr handle = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
		if (handle == IntPtr.Zero || !IsWindowEnabled(handle))
		{
			return false;
		}
		return PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
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
			KeyEventHandler blockKeys = (_, args) =>
			{
				if (args.Key == Key.System && args.SystemKey == Key.F4)
				{
					return;
				}
				args.Handled = true;
			};
			MouseButtonEventHandler blockMouse = (_, args) =>
			{
				if (!IsCloseButton(args.OriginalSource as DependencyObject))
				{
					args.Handled = true;
				}
			};
			owner.Closing += closing;
			owner.PreviewKeyDown += blockKeys;
			owner.PreviewMouseDown += blockMouse;
			// ShowDialog disables every window on the thread before the prompt is shown.
			// CloseMainWindow and the title-bar X then never arrive. Re-enable the owner
			// once the prompt is up so that close still reaches it, and Decline the prompt.
			dialog.ContentRendered += (_, _) => EnableOwner(owner, dialog);
			try
			{
				return dialog.ShowDialog();
			}
			finally
			{
				owner.Closing -= closing;
				owner.PreviewKeyDown -= blockKeys;
				owner.PreviewMouseDown -= blockMouse;
			}
		}
		dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		return dialog.ShowDialog();
	}

	private static void EnableOwner(Window owner, PromptWindow dialog)
	{
		IntPtr handle = new WindowInteropHelper(owner).Handle;
		if (handle != IntPtr.Zero)
		{
			EnableWindow(handle, true);
		}
		try
		{
			if (dialog.IsVisible)
			{
				dialog.Activate();
			}
		}
		catch (InvalidOperationException)
		{
		}
		OwnerReady?.Invoke(owner);
	}

	private static bool IsCloseButton(DependencyObject source)
	{
		while (source != null)
		{
			if (source is Button button && AutomationProperties.GetName(button) == "Close launcher")
			{
				return true;
			}
			source = VisualTreeHelper.GetParent(source);
		}
		return false;
	}

	[DllImport("user32.dll")]
	private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

	[DllImport("user32.dll")]
	private static extern bool IsWindowEnabled(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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
