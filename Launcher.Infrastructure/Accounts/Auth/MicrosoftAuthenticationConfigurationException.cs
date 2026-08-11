/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Accounts;

internal sealed class MicrosoftAuthenticationConfigurationException : Exception
{
    public MicrosoftAuthenticationConfigurationException(string message)
        : base(message)
    {
    }
}
