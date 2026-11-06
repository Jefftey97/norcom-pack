using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using System.Windows.Resources;
using System.Windows.Threading;

namespace Norcom.Launcher.Services;

public sealed class TrayHost : IDisposable
{
	private readonly NotifyIcon _icon;

	private bool _shown;

	public bool ForceExit { get; private set; }

	public TrayHost(Window window)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Expected O, but got Unknown
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Expected O, but got Unknown
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected O, but got Unknown
		TrayHost trayHost = this;
		_icon = new NotifyIcon
		{
			Text = "NORCOM Launcher",
			Visible = false
		};
		try
		{
			StreamResourceInfo resourceStream = WpfApplication.GetResourceStream(new Uri("pack://application:,,,/Assets/norcom.ico"));
			if (resourceStream != null)
			{
				_icon.Icon = new Icon(resourceStream.Stream);
			}
		}
		catch
		{
			_icon.Icon = SystemIcons.Application;
		}
		ContextMenuStrip val = new ContextMenuStrip();
		((ToolStrip)val).Items.Add("Open NORCOM", (Image)null, (EventHandler)delegate
		{
			((DispatcherObject)WpfApplication.Current).Dispatcher.Invoke((Action)Show);
		});
		((ToolStrip)val).Items.Add("Exit", (Image)null, (EventHandler)delegate
		{
			((DispatcherObject)WpfApplication.Current).Dispatcher.Invoke((Action)delegate
			{
				trayHost.ForceExit = true;
				trayHost._icon.Visible = false;
				window.Close();
			});
		});
		_icon.ContextMenuStrip = val;
		_icon.DoubleClick += delegate
		{
			((DispatcherObject)WpfApplication.Current).Dispatcher.Invoke((Action)Show);
		};
		void Show()
		{
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Invalid comparison between Unknown and I4
			window.Show();
			if ((int)window.WindowState == 1)
			{
				window.WindowState = (WindowState)0;
			}
			window.Activate();
			trayHost._icon.Visible = false;
		}
	}

	public void HideWindowToTray(Window window)
	{
		if (!_shown)
		{
			_icon.Visible = true;
			_shown = true;
		}
		window.Hide();
	}

	public void Dispose()
	{
		_icon.Visible = false;
		((Component)(object)_icon).Dispose();
	}
}
