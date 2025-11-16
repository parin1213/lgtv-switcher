using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LGTVSwitcher.Experimental.DisplayDetection.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsMessagePump : IAsyncDisposable, IDisposable
{
    private Task? _pumpTask;
    private TaskCompletionSource<bool>? _readyTcs;
    private TaskCompletionSource<bool>? _completionTcs;
    private CancellationTokenSource? _stopCts;
    private NativeMethods.WndProc? _wndProcDelegate;
    private string? _windowClassName;
    private IntPtr _hwnd;
    private IntPtr _instanceHandle;
    private bool _disposed;

    public event EventHandler<WindowMessageEventArgs>? WindowMessageReceived;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WindowsMessagePump only runs on Windows.");
        }

        if (_pumpTask != null)
        {
            await _readyTcs!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopCts = new CancellationTokenSource();

        _pumpTask = Task.Factory.StartNew(
            () => RunMessageLoop(_stopCts.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            await _readyTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopInternalAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_pumpTask == null)
        {
            return Task.CompletedTask;
        }

        return StopInternalAsync(cancellationToken);
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken = default)
    {
        if (_pumpTask == null)
        {
            return;
        }

        _stopCts?.Cancel();

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_CLOSE, 0, IntPtr.Zero);
        }

        var completionTask = _completionTcs?.Task ?? Task.CompletedTask;

        try
        {
            await completionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _stopCts?.Dispose();
            _stopCts = null;
            _pumpTask = null;
            _readyTcs = null;
            _completionTcs = null;
            _windowClassName = null;
            _hwnd = IntPtr.Zero;
            _instanceHandle = IntPtr.Zero;
            _wndProcDelegate = null;
        }
    }

    private void RunMessageLoop(CancellationToken token)
    {
        try
        {
            InitializeWindow();
            _readyTcs!.TrySetResult(true);

            while (!token.IsCancellationRequested)
            {
                var result = NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0);

                if (result == 0)
                {
                    break;
                }

                if (result == -1)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to pump window messages.");
                }

                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _readyTcs?.TrySetException(ex);
            _completionTcs?.TrySetException(ex);
        }
        finally
        {
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            if (!string.IsNullOrEmpty(_windowClassName))
            {
                NativeMethods.UnregisterClass(_windowClassName, _instanceHandle);
                _windowClassName = null;
            }

            _completionTcs?.TrySetResult(true);
        }
    }

    private void InitializeWindow()
    {
        _windowClassName = $"LGTVSwitcherHiddenWindow_{Environment.ProcessId}_{Guid.NewGuid():N}";
        _instanceHandle = NativeMethods.GetModuleHandle(null);
        _wndProcDelegate = new NativeMethods.WndProc(WndProc);

        var wndClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = _instanceHandle,
            lpszClassName = _windowClassName,
        };

        if (NativeMethods.RegisterClassEx(ref wndClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register hidden window class.");
        }

        _hwnd = NativeMethods.CreateWindowEx(
            0,
            _windowClassName,
            "LGTVSwitcherHidden",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            _instanceHandle,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create hidden window.");
        }
    }

    private nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == NativeMethods.WM_DESTROY)
        {
            NativeMethods.PostQuitMessage(0);
        }

        var messageKind = msg switch
        {
            NativeMethods.WM_DISPLAYCHANGE => WindowMessageKind.DisplayChanged,
            NativeMethods.WM_DEVICECHANGE => WindowMessageKind.DeviceChanged,
            _ => WindowMessageKind.Other,
        };

        WindowMessageReceived?.Invoke(this, new WindowMessageEventArgs(messageKind, msg, wParam, lParam));

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopInternalAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsMessagePump));
        }
    }
}
