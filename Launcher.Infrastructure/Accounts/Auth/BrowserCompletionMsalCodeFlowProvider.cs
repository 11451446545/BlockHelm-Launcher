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

using Launcher.Application.Accounts;
using Microsoft.Identity.Client;
using XboxAuthNet.Game;
using XboxAuthNet.Game.Authenticators;
using XboxAuthNet.Game.Msal;

namespace Launcher.Infrastructure.Accounts;

internal sealed class BrowserCompletionMsalCodeFlowProvider : IAuthenticationProvider
{
    private readonly MsalOAuthBuilder builder;
    private readonly IMicrosoftLoginBrowserPageProvider browserPageProvider;

    public BrowserCompletionMsalCodeFlowProvider(
        IPublicClientApplication application,
        IMicrosoftLoginBrowserPageProvider browserPageProvider)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.browserPageProvider = browserPageProvider
            ?? throw new ArgumentNullException(nameof(browserPageProvider));
        builder = new MsalOAuthBuilder(application);
    }

    public IAuthenticator Authenticate()
    {
        var authenticator = new FallbackAuthenticator();
        authenticator.AddAuthenticatorWithoutValidator(builder.Silent());
        authenticator.AddAuthenticator(
            builder.LoginHintValidator(throwWhenInvalid: true),
            CreateInteractiveAuthenticator(singleAccount: true));
        authenticator.AddAuthenticatorWithoutValidator(
            CreateInteractiveAuthenticator(singleAccount: false));
        return authenticator;
    }

    public IAuthenticator AuthenticateInteractively() =>
        CreateInteractiveAuthenticator(singleAccount: false);

    public IAuthenticator AuthenticateSilently() => builder.Silent();

    public ISessionValidator CreateSessionValidator() => StaticValidator.Invalid;

    public IAuthenticator ClearSession() => builder.ClearSession();

    public IAuthenticator Signout() => builder.ClearSession();

    internal SystemWebViewOptions CreateSystemWebViewOptions() => new()
    {
        HtmlMessageSuccess = browserPageProvider.GetAuthorizationCompletedHtml()
    };

    private IAuthenticator CreateInteractiveAuthenticator(bool singleAccount) =>
        builder.Interactive(tokenBuilder =>
        {
            if (singleAccount)
                tokenBuilder.WithPrompt(Prompt.NoPrompt);

            tokenBuilder
                .WithUseEmbeddedWebView(false)
                .WithSystemWebViewOptions(CreateSystemWebViewOptions());
        });
}
