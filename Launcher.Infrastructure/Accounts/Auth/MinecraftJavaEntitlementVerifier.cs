/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Launcher.Application.Accounts;

namespace Launcher.Infrastructure.Accounts;

internal interface IMinecraftJavaEntitlementVerifier
{
    Task EnsureOwnedAsync(string accessToken, CancellationToken cancellationToken);
}

internal sealed class MinecraftJavaEntitlementVerifier(HttpClient httpClient)
    : IMinecraftJavaEntitlementVerifier
{
    private const string EntitlementsEndpoint =
        "https://api.minecraftservices.com/entitlements/mcstore";

    private static readonly HashSet<string> JavaEditionEntitlements =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "product_minecraft",
            "game_minecraft"
        };

    public async Task EnsureOwnedAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.ReauthenticationRequired,
                "Microsoft account access token is missing.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, EntitlementsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.ReauthenticationRequired,
                "Minecraft account credentials were rejected.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.AuthenticationApplicationNotAuthorized,
                "The Microsoft application is not authorized by Minecraft services.");
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.AuthenticationServerUnavailable,
                "Minecraft authentication services are unavailable.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MicrosoftAccountAuthenticationException(
                LaunchAccountSessionFailureReason.InvalidAuthenticationResponse,
                $"Minecraft entitlements returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Minecraft entitlements response does not contain an items array.");
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && JavaEditionEntitlements.Contains(name.GetString() ?? string.Empty))
            {
                return;
            }
        }

        throw new MicrosoftAccountAuthenticationException(
            LaunchAccountSessionFailureReason.GameOwnershipRequired,
            "The Microsoft account does not own Minecraft: Java Edition.");
    }
}
