using System;
using System.ComponentModel;
using System.Windows;

namespace Norcom.Launcher.Views;

public partial class PromptWindow : Window
{
	private bool _answered;

	public PromptWindow(string title, string message, bool confirm)
	{
		InitializeComponent();
		Title = string.IsNullOrWhiteSpace(title) ? "NORCOM" : title;
		MessageText.Text = message ?? "";
		if (!confirm)
		{
			YesButton.Visibility = Visibility.Collapsed;
			NoButton.Content = "OK";
			NoButton.IsDefault = true;
			NoButton.IsCancel = true;
		}
		Closing += OnClosing;
	}

	public void Decline()
	{
		if (_answered)
		{
			return;
		}
		_answered = true;
		try
		{
			DialogResult = false;
		}
		catch (InvalidOperationException)
		{
			Close();
		}
	}

	private void YesClick(object sender, RoutedEventArgs args)
	{
		_answered = true;
		DialogResult = true;
	}

	private void NoClick(object sender, RoutedEventArgs args)
	{
		Decline();
	}

	private void OnClosing(object sender, CancelEventArgs args)
	{
		if (_answered)
		{
			return;
		}
		_answered = true;
		try
		{
			DialogResult = false;
		}
		catch (InvalidOperationException)
		{
		}
	}
}
