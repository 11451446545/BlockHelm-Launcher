/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Launcher.Infrastructure.Minecraft;

internal enum GameStartupReadinessResult
{
    WindowVisible,
    GameOutputReady,
    ProcessExited
}

internal interface IGameStartupReadinessWaiter
{
    Task<GameStartupReadinessResult> WaitAsync(
        Process process,
        Task gameOutputReady,
        CancellationToken cancellationToken);
}

/// <summary>
/// Waits until the launched Java process owns any visible, non-empty top-level window
/// or its output proves that Minecraft reached a strong initialization milestone.
/// </summary>
internal sealed class GameStartupReadinessWaiter : IGameStartupReadinessWaiter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private readonly IGameWindowReadinessProbe windowProbe;

    public GameStartupReadinessWaiter(IGameWindowReadinessProbe? windowProbe = null)
    {
        this.windowProbe = windowProbe ?? new WindowsGameWindowReadinessProbe();
    }

    public async Task<GameStartupReadinessResult> WaitAsync(
        Process process,
        Task gameOutputReady,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(gameOutputReady);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (HasExited(process))
                return GameStartupReadinessResult.ProcessExited;

            if (HasVisibleWindow(process))
            {
                // Prefer an exit observed during the same polling turn over a
                // transient window that disappeared while startup was failing.
                return HasExited(process)
                    ? GameStartupReadinessResult.ProcessExited
                    : GameStartupReadinessResult.WindowVisible;
            }

            if (gameOutputReady.IsCompletedSuccessfully)
            {
                return HasExited(process)
                    ? GameStartupReadinessResult.ProcessExited
                    : GameStartupReadinessResult.GameOutputReady;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private bool HasVisibleWindow(Process process)
    {
        try
        {
            return windowProbe.HasVisibleTopLevelWindow(process.Id);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

internal interface IGameWindowReadinessProbe
{
    bool HasVisibleTopLevelWindow(int processId);
}

internal sealed class WindowsGameWindowReadinessProbe : IGameWindowReadinessProbe
{
    public bool HasVisibleTopLevelWindow(int processId)
    {
        var found = false;
        EnumWindows(
            (window, _) =>
            {
                GetWindowThreadProcessId(window, out var ownerProcessId);
                if (ownerProcessId != unchecked((uint)processId)
                    || !IsWindowVisible(window)
                    || !GetWindowRect(window, out var bounds)
                    || bounds.Right <= bounds.Left
                    || bounds.Bottom <= bounds.Top)
                {
                    return true;
                }

                found = true;
                return false;
            },
            IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out WindowBounds bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
