using System.Windows;

namespace Norcom.Launcher;

public static class NavChoice
{
	public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.RegisterAttached("IsCurrent", typeof(bool), typeof(NavChoice), (PropertyMetadata)new FrameworkPropertyMetadata((object)false));

	public static void SetIsCurrent(DependencyObject element, bool value)
	{
		element.SetValue(IsCurrentProperty, (object)value);
	}

	public static bool GetIsCurrent(DependencyObject element)
	{
		return (bool)element.GetValue(IsCurrentProperty);
	}
}
