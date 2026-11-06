using System.Windows;

namespace Norcom.Admin.Services;

public sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private Thread? _thread;
    private volatile bool _run = true;

    public bool Acquire()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        if (!TryOwn(out _mutex))
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting("Global\\NORCOM.Admin.Activate");
                signal.Set();
            }
            catch
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting("NORCOM.Admin.Activate");
                    signal.Set();
                }
                catch
                {
                }
            }

            return false;
        }

        try
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Global\\NORCOM.Admin.Activate");
        }
        catch
        {
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, "NORCOM.Admin.Activate");
        }

        _thread = new Thread(Listen) { IsBackground = true, Name = "NORCOM.Admin.Activate" };
        _thread.Start();
        return true;
    }

    public void Dispose()
    {
        _run = false;
        try
        {
            _activate?.Set();
        }
        catch
        {
        }

        _mutex?.Dispose();
        _activate?.Dispose();
    }

    private void Listen()
    {
        while (_run && _activate != null)
        {
            if (!_activate.WaitOne(500) || !_run)
            {
                continue;
            }

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var window = Application.Current?.MainWindow;
                if (window == null)
                {
                    return;
                }

                window.Show();
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Activate();
            });
        }
    }

    private static bool TryOwn(out Mutex? mutex)
    {
        foreach (var name in new[] { "Global\\NORCOM.Admin.SingleInstance", "NORCOM.Admin.SingleInstance" })
        {
            try
            {
                mutex = new Mutex(false, name);
                try
                {
                    if (mutex.WaitOne(0))
                    {
                        return true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    return true;
                }

                mutex.Dispose();
            }
            catch
            {
            }
        }

        mutex = null;
        return false;
    }
}
