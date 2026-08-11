/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;
using System.Net;
using Launcher.App.Resources;
using Launcher.Application.Accounts;

namespace Launcher.App.Services;

internal sealed class MicrosoftLoginBrowserPageProvider : IMicrosoftLoginBrowserPageProvider
{
    public string GetAuthorizationCompletedHtml()
    {
        var culture = CultureInfo.CurrentUICulture;
        var language = WebUtility.HtmlEncode(culture.Name);
        var heading = WebUtility.HtmlEncode(Strings.MicrosoftLogin_BrowserCompletionHeading);
        var message = WebUtility.HtmlEncode(string.Format(
            culture,
            Strings.MicrosoftLogin_BrowserCompletionMessageFormat,
            Strings.App_Title));

        return $"""
            <!doctype html>
            <html lang="{language}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{heading}</title>
            </head>
            <body>
              <h2>{heading}</h2>
              <p>{message}</p>
            </body>
            </html>
            """;
    }
}
