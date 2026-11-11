using System.Windows.Threading;
using Norcom.Launcher.Core.Games.Minecraft;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.ViewModels;
using Xunit;

namespace Norcom.Launcher.UiTests;

public class PlayThreadTests
{
	[Fact]
	public void PlayAsyncOffTheUiThreadNotifiesOnTheDispatcher()
	{
		UiTestHost.Run(() =>
		{
			int ui = Environment.CurrentManagedThreadId;
			bool onDispatcher = false;
			bool offDispatcher = false;
			ShellViewModel vm = new ShellViewModel(new ControlledGameLookup());
			vm.SelectGameCommand.Execute("minecraft");
			vm.PlayCommand.CanExecuteChanged += (_, _) =>
			{
				if (Dispatcher.CurrentDispatcher.CheckAccess() && Environment.CurrentManagedThreadId == ui)
				{
					onDispatcher = true;
				}
				else
				{
					offDispatcher = true;
				}
			};
			MinecraftGame.PlayProbe = async context =>
			{
				await Task.Delay(40).ConfigureAwait(false);
				Assert.NotEqual(ui, Environment.CurrentManagedThreadId);
				context.SetBusy(true);
				context.ProgressLabel = "Opening Prism";
				context.StatusLine = "Starting";
				context.AccountStatus = "Starting";
				context.NotifyCommands();
				context.Apply(PlayOutcome.Blocked("Play didn't finish", "stopped"));
				context.SetBusy(false);
				context.RefreshLaunched();
			};
			try
			{
				Task play = vm.PlayActiveAsync();
				DispatcherFrame frame = new DispatcherFrame();
				play.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
				Dispatcher.PushFrame(frame);
				play.GetAwaiter().GetResult();
				Assert.True(onDispatcher);
				Assert.False(offDispatcher);
				Assert.False(vm.IsBusy);
			}
			finally
			{
				MinecraftGame.PlayProbe = null;
			}
		});
	}

	private sealed class ControlledGameLookup : IGameProcessLookup
	{
		public int? TryGameProcessId(int? parent)
		{
			return null;
		}
	}
}
