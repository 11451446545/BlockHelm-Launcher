/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Accounts;

public sealed class MicrosoftAccountSessionExpiredException : Exception
{
    public MicrosoftAccountSessionExpiredException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
