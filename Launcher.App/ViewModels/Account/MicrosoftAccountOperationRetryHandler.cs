/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.Application.Accounts;

namespace Launcher.App.ViewModels.Account;

internal sealed class MicrosoftAccountOperationRetryHandler
{
    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountReauthenticationDialogService dialogService;

    public MicrosoftAccountOperationRetryHandler(
        AccountListViewModel accountList,
        IMicrosoftAccountReauthenticationDialogService dialogService)
    {
        this.accountList = accountList;
        this.dialogService = dialogService;
    }

    public async Task<MicrosoftAccountOperationResult<T>> ExecuteAsync<T>(
        LauncherAccount account,
        Func<LauncherAccount, Task<T>> operation)
    {
        try
        {
            return new MicrosoftAccountOperationResult<T>(
                account,
                await operation(account));
        }
        catch (MicrosoftAccountSessionExpiredException exception)
        {
            if (!await dialogService.ShowMicrosoftReauthenticationDialogAsync(account))
            {
                throw new OperationCanceledException(
                    "Microsoft account reauthentication was canceled.",
                    exception);
            }

            var refreshedAccount = accountList.FindAccount(account.Id);
            if (refreshedAccount is null || !refreshedAccount.IsMicrosoft)
            {
                throw new MicrosoftAccountSessionExpiredException(
                    "The reauthenticated Microsoft account is no longer available.",
                    exception);
            }

            return new MicrosoftAccountOperationResult<T>(
                refreshedAccount,
                await operation(refreshedAccount));
        }
    }

    public async Task<LauncherAccount> ExecuteAsync(
        LauncherAccount account,
        Func<LauncherAccount, Task> operation)
    {
        var result = await ExecuteAsync(
            account,
            async current =>
            {
                await operation(current);
                return true;
            });
        return result.Account;
    }
}

internal readonly record struct MicrosoftAccountOperationResult<T>(
    LauncherAccount Account,
    T Value);
