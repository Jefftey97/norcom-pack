using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using Norcom.Launcher.ViewModels;

namespace Norcom.Launcher.Views;

public class SettingsView : UserControl, IComponentConnector
{
	internal PasswordBox CrashChannel;

	private bool _contentLoaded;

	public SettingsView()
	{
		InitializeComponent();
	}

	private void SaveCrashChannel(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)this).DataContext is ShellViewModel shellViewModel)
		{
			shellViewModel.SetCrashWebhook(CrashChannel.Password);
		}
		CrashChannel.Clear();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri uri = new Uri("/NorcomLauncher;V2.0.1.0;component/views/settingsview.xaml", UriKind.Relative);
			Application.LoadComponent((object)this, uri);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected O, but got Unknown
		switch (connectionId)
		{
		case 1:
			CrashChannel = (PasswordBox)target;
			break;
		case 2:
			((ButtonBase)(Button)target).Click += new RoutedEventHandler(SaveCrashChannel);
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
