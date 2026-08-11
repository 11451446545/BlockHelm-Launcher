/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Recognizes stable Minecraft initialization milestones from captured process output.
/// This is a fallback for window configurations where Win32 window discovery cannot
/// observe the game window even though the client is already usable.
/// </summary>
internal sealed class GameStartupLogReadinessDetector
{
    private readonly TaskCompletionSource ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => ready.Task;

    public void Observe(string line)
    {
        if (IsReadyLine(line))
            ready.TrySetResult();
    }

    internal static bool IsReadyLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (line.Contains("OpenAL initialized", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Starting up SoundSystem", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Sound engine started", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Found animation info", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return line.Contains("Created", StringComparison.OrdinalIgnoreCase)
               && line.Contains("textures", StringComparison.OrdinalIgnoreCase)
               && line.Contains("-atlas", StringComparison.OrdinalIgnoreCase);
    }
}
