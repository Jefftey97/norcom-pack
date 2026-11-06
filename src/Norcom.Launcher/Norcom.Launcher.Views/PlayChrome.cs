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
		AttachPinStatus(panel, index, vm);
		return true;
	}

	private static void AttachPinStatus(Panel panel, int index, ShellViewModel vm)
	{
		for (int i = 0; i < panel.Children.Count; i++)
		{
			if (panel.Children[i] is FrameworkElement existing && (existing.Tag as string) == "norcom-pin-status")
			{
				return;
			}
		}
		TextBlock word = new TextBlock
		{
			FontFamily = new FontFamily("Segoe UI"),
			FontWeight = FontWeights.SemiBold,
			FontSize = 13.0,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		AutomationProperties.SetAutomationId(word, "PinStatus");
		TextBlock caption = new TextBlock
		{
			Text = "PIN",
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = 11.0,
			Foreground = Brush(138, 160, 194),
			VerticalAlignment = VerticalAlignment.Center
		};
		Button fix = new Button
		{
			Content = "Fix",
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			FontFamily = new FontFamily("Segoe UI"),
			FontWeight = FontWeights.SemiBold,
			FontSize = 12.0,
			Foreground = Brush(61, 240, 255),
			Cursor = Cursors.Hand,
			Focusable = false,
			ToolTip = "Heal pack jars that lag the live pin and record it",
			Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'><Border Background='#0C1324' BorderBrush='#3DF0FF' BorderThickness='1' CornerRadius='6' Padding='10,4'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>")
		};
		AutomationProperties.SetAutomationId(fix, "FixPin");
		AutomationProperties.SetName(fix, "Fix pin");
		fix.Click += async delegate
		{
			try
			{
				await vm.FixPinAsync().ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				LauncherLog.Write("pin fix " + ex.GetType().Name);
			}
		};
		StackPanel row = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		row.Children.Add(caption);
		row.Children.Add(word);
		row.Children.Add(fix);
		Border card = new Border
		{
			Background = Brush(12, 19, 36),
			BorderBrush = Brush(61, 240, 255),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(10.0, 6.0, 8.0, 6.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			Tag = "norcom-pin-status",
			Child = row
		};
		AutomationProperties.SetName(card, "Pin status");
		void Paint()
		{
			string state = vm.PinStatus ?? "";
			word.Text = state;
			word.ToolTip = string.IsNullOrWhiteSpace(vm.PinDetail) ? null : vm.PinDetail;
			if (state == "correct")
			{
				word.Foreground = Brush(61, 255, 176);
			}
			else if (state == "fetching")
			{
				word.Foreground = Brush(61, 240, 255);
			}
			else
			{
				word.Foreground = Brush(255, 93, 122);
			}
		}
		vm.PropertyChanged += delegate(object _, System.ComponentModel.PropertyChangedEventArgs args)
		{
			if (args.PropertyName == "PinStatus" || args.PropertyName == "PinDetail")
			{
				Paint();
			}
		};
		Paint();
		panel.Children.Insert(index, card);
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
