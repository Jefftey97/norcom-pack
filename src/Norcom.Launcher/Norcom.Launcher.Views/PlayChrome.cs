using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Norcom.Launcher.Converters;
using Norcom.Launcher.Core.Text;
using Norcom.Launcher.ViewModels;

namespace Norcom.Launcher.Views;

internal static class PlayChrome
{
	private const string PlayRow = "norcom-play-row";

	public static void Install(Window window, Grid root, ShellViewModel vm)
	{
		if (window == null || root == null || vm == null)
		{
			return;
		}
		EventHandler rendered = null;
		rendered = delegate
		{
			window.ContentRendered -= rendered;
			AddWindowBorder(root);
			AttachPlay(root, vm);
		};
		window.ContentRendered += rendered;
	}

	private static void AddWindowBorder(Grid root)
	{
		AdornerLayer layer = AdornerLayer.GetAdornerLayer(root);
		if (layer == null)
		{
			AddFallbackBorder(root);
			return;
		}
		Adorner[] existing = layer.GetAdorners(root);
		if (existing != null)
		{
			for (int i = 0; i < existing.Length; i++)
			{
				if (existing[i] is NeonFrameAdorner)
				{
					return;
				}
			}
		}
		layer.Add(new NeonFrameAdorner(root));
	}

	private static void AddFallbackBorder(Grid root)
	{
		for (int i = 0; i < root.Children.Count; i++)
		{
			if (root.Children[i] is Border existing && (existing.Tag as string) == "norcom-neon-frame")
			{
				return;
			}
		}
		Border frame = new Border
		{
			BorderThickness = new Thickness(2.0),
			IsHitTestVisible = false,
			Background = Brushes.Transparent,
			Tag = "norcom-neon-frame",
			BorderBrush = EdgeBrush()
		};
		Grid.SetRowSpan(frame, Math.Max(root.RowDefinitions.Count, 1));
		Grid.SetColumnSpan(frame, Math.Max(root.ColumnDefinitions.Count, 1));
		Panel.SetZIndex(frame, 80);
		root.Children.Add(frame);
	}

	private static LinearGradientBrush EdgeBrush()
	{
		LinearGradientBrush brush = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(1.0, 1.0)
		};
		brush.GradientStops.Add(new GradientStop(Color.FromRgb(61, 240, 255), 0.0));
		brush.GradientStops.Add(new GradientStop(Color.FromRgb(122, 92, 255), 0.5));
		brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 79, 216), 1.0));
		brush.Freeze();
		return brush;
	}

	private static bool AttachPlay(DependencyObject root, ShellViewModel vm)
	{
		Button play = FindPlay(root, vm);
		if (play == null)
		{
			LauncherLog.Write("play chrome button missing");
			return false;
		}
		if (play.Parent is StackPanel existing && (existing.Tag as string) == PlayRow)
		{
			return true;
		}
		play.SetBinding(ContentControl.ContentProperty, new Binding("PlayLabel"));
		play.Effect = new DropShadowEffect
		{
			Color = Color.FromRgb(61, 240, 255),
			BlurRadius = 14.0,
			ShadowDepth = 0.0,
			Opacity = 0.38
		};
		if (play.Parent is not Panel panel)
		{
			LauncherLog.Write("play chrome parent " + (play.Parent == null ? "none" : play.Parent.GetType().Name));
			return false;
		}
		int index = panel.Children.IndexOf(play);
		StackPanel host = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = play.VerticalAlignment,
			HorizontalAlignment = play.HorizontalAlignment,
			Margin = play.Margin,
			Tag = PlayRow
		};
		Grid.SetRow(host, Grid.GetRow(play));
		Grid.SetColumn(host, Grid.GetColumn(play));
		Grid.SetRowSpan(host, Grid.GetRowSpan(play));
		Grid.SetColumnSpan(host, Grid.GetColumnSpan(play));
		play.Margin = new Thickness(0.0);
		play.VerticalAlignment = VerticalAlignment.Center;
		panel.Children.Remove(play);
		host.Children.Add(play);
		host.Children.Add(StopButton(vm));
		panel.Children.Insert(index, host);
		return true;
	}

	private static Button StopButton(ShellViewModel vm)
	{
		Button stop = new Button
		{
			Content = "X",
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			FontFamily = new FontFamily("Segoe UI"),
			FontWeight = FontWeights.Bold,
			FontSize = 15.0,
			Foreground = Brush(255, 93, 122),
			Cursor = Cursors.Hand,
			ToolTip = "Stop Minecraft",
			Focusable = false,
			Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'><Border Background='#2A121C' BorderBrush='#FF5D7A' BorderThickness='1.5' CornerRadius='8' Width='36' Height='36'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>")
		};
		AutomationProperties.SetAutomationId(stop, "StopGame");
		AutomationProperties.SetName(stop, "Stop Minecraft");
		stop.SetBinding(UIElement.VisibilityProperty, new Binding("GameRunning")
		{
			Converter = new BoolToVisibilityConverter()
		});
		stop.Click += delegate
		{
			vm.StopLaunchedGame();
		};
		return stop;
	}

	private static Button FindPlay(DependencyObject node, ShellViewModel vm)
	{
		int count = VisualTreeHelper.GetChildrenCount(node);
		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(node, i);
			if (child is Button button && IsPlay(button, vm))
			{
				return button;
			}
			Button nested = FindPlay(child, vm);
			if (nested != null)
			{
				return nested;
			}
		}
		return null;
	}

	private static bool IsPlay(Button button, ShellViewModel vm)
	{
		if (button.Command == vm.PlayCommand)
		{
			return true;
		}
		return button.Content as string == "Play";
	}

	private static SolidColorBrush Brush(byte r, byte g, byte b)
	{
		SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
		brush.Freeze();
		return brush;
	}

	private sealed class NeonFrameAdorner : Adorner
	{
		private readonly Pen _glow;

		private readonly Pen _edge;

		public NeonFrameAdorner(UIElement adorned)
			: base(adorned)
		{
			base.IsHitTestVisible = false;
			_edge = new Pen(EdgeBrush(), 2.0);
			_edge.Freeze();
			SolidColorBrush glow = new SolidColorBrush(Color.FromArgb(90, 61, 240, 255));
			glow.Freeze();
			_glow = new Pen(glow, 6.0);
			_glow.Freeze();
		}

		protected override void OnRender(DrawingContext drawingContext)
		{
			Rect rect = new Rect(base.AdornedElement.RenderSize);
			rect.Inflate(-3.0, -3.0);
			if (rect.Width <= 0.0 || rect.Height <= 0.0)
			{
				return;
			}
			drawingContext.DrawRectangle(null, _glow, rect);
			rect.Inflate(-1.0, -1.0);
			drawingContext.DrawRectangle(null, _edge, rect);
		}
	}
}
