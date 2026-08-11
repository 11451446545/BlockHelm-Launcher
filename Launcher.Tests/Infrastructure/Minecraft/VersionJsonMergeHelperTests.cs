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

using System.Text.Json.Nodes;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class VersionJsonMergeHelperTests : TestTempDirectory
{
    [Fact]
    public void MergeLibrariesPreservesSameNameNativeVariantAndSourceOrder()
    {
        var baseLibraries = CreateBaseLibraries();
        var derivedLibraries = new JsonArray
        {
            new JsonObject { ["name"] = "net.minecraftforge:forge:1.16.5-36.2.42" }
        };

        var merged = VersionJsonMergeHelper.MergeLibraries(baseLibraries, derivedLibraries);

        Assert.Equal(
            [
                "org.lwjgl:lwjgl:3.2.2",
                "org.lwjgl:lwjgl:3.2.2",
                "net.minecraftforge:forge:1.16.5-36.2.42"
            ],
            merged.Select(library => library!["name"]!.GetValue<string>()));
        Assert.Null(merged[0]!["natives"]);
        Assert.Equal("natives-windows", merged[1]!["natives"]!["windows"]!.GetValue<string>());
    }

    [Fact]
    public void MergeLibrariesRemovesOnlyStructurallyIdenticalDuplicates()
    {
        var duplicate = new JsonObject
        {
            ["name"] = "com.mojang:patchy:2.2.10",
            ["downloads"] = new JsonObject
            {
                ["artifact"] = new JsonObject
                {
                    ["path"] = "com/mojang/patchy/2.2.10/patchy-2.2.10.jar"
                }
            }
        };
        var baseLibraries = new JsonArray
        {
            duplicate.DeepClone(),
            new JsonObject { ["name"] = "org.ow2.asm:asm:9.7" }
        };
        var derivedLibraries = new JsonArray
        {
            duplicate.DeepClone(),
            new JsonObject { ["name"] = "net.fabricmc:fabric-loader:0.16.14" }
        };

        var merged = VersionJsonMergeHelper.MergeLibraries(baseLibraries, derivedLibraries);

        Assert.Equal(
            [
                "com.mojang:patchy:2.2.10",
                "org.ow2.asm:asm:9.7",
                "net.fabricmc:fabric-loader:0.16.14"
            ],
            merged.Select(library => library!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ForgeStyleFlatteningPreservesNativeClassifierForFilePlanning()
    {
        const string baseVersionName = "1.16.5";
        const string derivedVersionName = "forge-1.16.5-36.2.42";
        const string finalVersionName = "1.16.5-forge-36.2.42";
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionsDirectory = Path.Combine(minecraftDirectory, "versions");
        var baseDirectory = Path.Combine(versionsDirectory, baseVersionName);
        var derivedDirectory = Path.Combine(versionsDirectory, derivedVersionName);
        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(derivedDirectory);

        var baseVersion = new JsonObject
        {
            ["id"] = baseVersionName,
            ["mainClass"] = "net.minecraft.client.main.Main",
            ["libraries"] = CreateBaseLibraries()
        };
        var derivedVersion = new JsonObject
        {
            ["id"] = derivedVersionName,
            ["inheritsFrom"] = baseVersionName,
            ["mainClass"] = "cpw.mods.modlauncher.Launcher",
            ["libraries"] = new JsonArray
            {
                new JsonObject { ["name"] = "net.minecraftforge:forge:1.16.5-36.2.42" }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(baseDirectory, $"{baseVersionName}.json"),
            baseVersion.ToJsonString());
        await File.WriteAllTextAsync(
            Path.Combine(baseDirectory, $"{baseVersionName}.jar"),
            "client");
        await File.WriteAllTextAsync(
            Path.Combine(derivedDirectory, $"{derivedVersionName}.json"),
            derivedVersion.ToJsonString());

        var installedName = await VanillaVersionIsolator.CreateFlattenedDerivedVersionAsync(
            baseVersionName,
            derivedVersionName,
            finalVersionName,
            minecraftDirectory);

        var finalJsonPath = Path.Combine(versionsDirectory, finalVersionName, $"{finalVersionName}.json");
        var finalVersion = JsonNode.Parse(await File.ReadAllTextAsync(finalJsonPath))!.AsObject();
        var nativeLibrary = Assert.Single(
            finalVersion["libraries"]!.AsArray().OfType<JsonObject>(),
            library => library["natives"] is JsonObject);
        var downloads = ManagedLibraryArtifactResolver.EnumerateDownloads(nativeLibrary).ToList();

        Assert.Equal(finalVersionName, installedName);
        Assert.False(finalVersion.ContainsKey("inheritsFrom"));
        Assert.Equal("natives-windows", nativeLibrary["natives"]!["windows"]!.GetValue<string>());
        Assert.Contains(
            downloads,
            artifact => artifact.RelativePath ==
                "org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2-natives-windows.jar");
    }

    internal static JsonArray CreateBaseLibraries()
    {
        return JsonNode.Parse(
            """
            [
              {
                "name": "org.lwjgl:lwjgl:3.2.2",
                "downloads": {
                  "artifact": {
                    "path": "org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2.jar",
                    "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2.jar"
                  }
                }
              },
              {
                "name": "org.lwjgl:lwjgl:3.2.2",
                "downloads": {
                  "artifact": {
                    "path": "org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2.jar",
                    "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2.jar"
                  },
                  "classifiers": {
                    "natives-windows": {
                      "path": "org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2-natives-windows.jar",
                      "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.2.2/lwjgl-3.2.2-natives-windows.jar"
                    }
                  }
                },
                "natives": {
                  "windows": "natives-windows"
                }
              }
            ]
            """)!.AsArray();
    }
}
