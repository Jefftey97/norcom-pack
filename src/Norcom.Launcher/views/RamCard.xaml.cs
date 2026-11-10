using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Norcom.Launcher.ViewModels;

namespace Norcom.Launcher.Views;

public partial class RamCard : UserControl
{
	private ShellViewModel _hooked;

	public RamCard()
	{
		InitializeComponent();
		DataContextChanged += delegate
		{
			if (_hooked != null)
			{
				_hooked.PropertyChanged -= OnViewModelChanged;
			}
			_hooked = DataContext as ShellViewModel;
			if (_hooked != null)
			{
				_hooked.PropertyChanged += OnViewModelChanged;
			}
			Draw();
		};
		Loaded += delegate
		{
			Draw();
		};
	}

	private void OnViewModelChanged(object sender, PropertyChangedEventArgs args)
	{
		if (args.PropertyName == nameof(ShellViewModel.MemoryMb) || args.PropertyName == nameof(ShellViewModel.RamSafeMaxMb) || args.PropertyName == nameof(ShellViewModel.RamTotalMb) || args.PropertyName == nameof(ShellViewModel.SelectedGameId))
		{
			Draw();
		}
	}

	private void Draw()
	{
		if (GreenCol == null || DataContext is not ShellViewModel vm)
		{
			return;
		}
		int total = vm.RamTotalMb;
		int safe = vm.RamSafeMaxMb;
		int chosen = (int)vm.MemoryMb;
		int scale = total > 0 ? total : Math.Max(safe, 1);
		int red = total > 0 ? Math.Max(0, total - safe) : 0;
		GreenCol.Width = new GridLength(Math.Max(safe, 0), GridUnitType.Star);
		RedCol.Width = new GridLength(red, GridUnitType.Star);
		BeforeMarker.Width = new GridLength(Math.Max(chosen, 0), GridUnitType.Star);
		AfterMarker.Width = new GridLength(Math.Max(scale - chosen, 0), GridUnitType.Star);
	}
}
