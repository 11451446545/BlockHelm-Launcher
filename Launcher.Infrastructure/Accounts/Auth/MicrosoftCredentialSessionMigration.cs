/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Infrastructure.Accounts.Credentials;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.XboxAuth;

namespace Launcher.Infrastructure.Accounts;

internal static class MicrosoftCredentialSessionMigration
{
    private const string MicrosoftOAuthSessionKey = "MicrosoftOAuth";
    private const string ClientIdentityMarkerFileName = "authentication-client-id";

    public static void EnsureClientIdentity(
        DpapiMicrosoftJsonStorage credentialStorage,
        LauncherPathProvider pathProvider,
        string clientId)
    {
        ArgumentNullException.ThrowIfNull(credentialStorage);
        ArgumentNullException.ThrowIfNull(pathProvider);

        var microsoftDirectory = Path.Combine(pathProvider.DefaultAccountDataDirectory, "microsoft");
        var markerPath = Path.Combine(microsoftDirectory, ClientIdentityMarkerFileName);
        try
        {
            if (File.Exists(markerPath)
                && string.Equals(
                    File.ReadAllText(markerPath).Trim(),
                    clientId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var root = credentialStorage.ReadAsJsonNode() as JsonObject;
            var changed = false;
            if (root is not null)
            {
                foreach (var accountNode in root.Select(property => property.Value).OfType<JsonObject>())
                {
                    changed |= accountNode.Remove(MicrosoftOAuthSessionKey);
                    changed |= accountNode.Remove(XboxSessionSource.KeyName);
                    changed |= accountNode.Remove(JETokenSource.KeyName);
                }

                if (changed)
                {
                    credentialStorage.Write(
                        root,
                        JsonXboxGameAccountManager.DefaultSerializerOption);
                }
            }

            WriteMarkerAtomically(markerPath, clientId);
        }
        catch (MicrosoftCredentialStorageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MicrosoftCredentialStorageException(
                "Microsoft authentication identity migration could not be completed.",
                exception);
        }
    }

    private static void WriteMarkerAtomically(string markerPath, string clientId)
    {
        var directory = Path.GetDirectoryName(markerPath)
            ?? throw new InvalidOperationException("Microsoft authentication marker path must have a parent directory.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(markerPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, clientId, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, markerPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
