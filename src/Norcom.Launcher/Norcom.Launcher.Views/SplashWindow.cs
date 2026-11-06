using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Norcom.Launcher.Views;

public class SplashWindow : Window, IComponentConnector
{
	internal TextBlock Status;

	internal ProgressBar Bar;

	private bool _contentLoaded;

	public SplashWindow()
	{
		InitializeComponent();
	}

	public void Report(string text, double fraction)
	{
		((DispatcherObject)this).Dispatcher.Invoke((Action)delegate
		{
			Status.Text = text;
			((RangeBase)Bar).Value = Math.Clamp(fraction, 0.0, 1.0);
		});
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri uri = new Uri("/NorcomLauncher;V2.0.1.0;component/views/splashwindow.xaml", UriKind.Relative);
			Application.LoadComponent((object)this, uri);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	internal Delegate _CreateDelegate(Type delegateType, string handler)
	{
		return Delegate.CreateDelegate(delegateType, this, handler);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		switch (connectionId)
		{
		case 1:
			Status = (TextBlock)target;
			break;
		case 2:
			Bar = (ProgressBar)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
