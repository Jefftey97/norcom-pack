using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Shapes;

namespace Norcom.Launcher.Controls;

public class NeonMark : UserControl, IComponentConnector
{
	public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register("Phase", typeof(double), typeof(NeonMark), new PropertyMetadata((object)0.0, new PropertyChangedCallback(OnPhase)));

	internal NeonMark Root;

	internal Path Glyph;

	private bool _contentLoaded;

	public double Phase
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(PhaseProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(PhaseProperty, (object)value);
		}
	}

	public NeonMark()
	{
		InitializeComponent();
	}

	private static void OnPhase(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is NeonMark neonMark)
		{
			((Shape)neonMark.Glyph).StrokeDashOffset = (double)e.NewValue;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri uri = new Uri("/NorcomLauncher;V2.0.1.0;component/controls/neonmark.xaml", UriKind.Relative);
			Application.LoadComponent((object)this, uri);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.33.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		switch (connectionId)
		{
		case 1:
			Root = (NeonMark)target;
			break;
		case 2:
			Glyph = (Path)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
