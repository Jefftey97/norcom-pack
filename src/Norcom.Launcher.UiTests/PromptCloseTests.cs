using System.Windows;
using System.Windows.Threading;
using Norcom.Launcher.Views;
using Xunit;

namespace Norcom.Launcher.UiTests;

public class PromptCloseTests
{
	[Fact]
	public void ClosingTheOwnerDeclinesThePromptAndDoesNotConfirm()
	{
		UiTestHost.Run(() =>
		{
			Window owner = new Window
			{
				Title = "NORCOM prompt close",
				Width = 480,
				Height = 320,
				WindowStartupLocation = WindowStartupLocation.Manual,
				Left = 40,
				Top = 40,
				ShowInTaskbar = false
			};
			bool sent = false;
			bool stopped = false;
			bool posted = false;
			Action<Window> ready = window =>
			{
				Assert.True(LauncherDialogs.OwnerAcceptsClose(window));
				posted = LauncherDialogs.PostOwnerClose(window);
				Assert.True(posted);
			};
			LauncherDialogs.OwnerReady += ready;
			DispatcherTimer unblock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
			unblock.Tick += (_, _) =>
			{
				unblock.Stop();
				LauncherDialogs.DismissOwned(owner);
			};
			try
			{
				owner.Show();
				unblock.Start();
				bool confirmed = LauncherDialogs.Confirm("Send a redacted launcher report?", "Send diagnostics");
				if (confirmed)
				{
					sent = true;
				}
				if (confirmed)
				{
					stopped = true;
				}
				Assert.True(posted);
				Assert.False(confirmed);
				Assert.False(sent);
				Assert.False(stopped);
				Assert.False(owner.IsLoaded);
				Assert.False(Application.Current.Windows.OfType<PromptWindow>().Any());
			}
			finally
			{
				unblock.Stop();
				LauncherDialogs.OwnerReady -= ready;
				if (owner.IsLoaded)
				{
					owner.Close();
				}
			}
		});
	}
}
