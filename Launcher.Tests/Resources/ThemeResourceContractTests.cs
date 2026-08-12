/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Resources;

public sealed class ThemeResourceContractTests
{
    [Fact]
    public void DarkAndLightThemesExposeTheSameCoreResources()
    {
        var dark = LoadKeys("Dark.xaml");
        var light = LoadKeys("Light.xaml");

        Assert.Equal(dark.Order(), light.Order());
        Assert.Contains("Brush.Text.Primary", dark);
        Assert.Contains("Brush.Surface.Window", dark);
        Assert.Contains("Brush.Control.Border", dark);
        Assert.Contains("Color.Surface.Popup", dark);
        Assert.Contains("Color.Page.Background", dark);
    }

    [Fact]
    public void LightThemeUsesRequiredPageAndCardBackgroundColors()
    {
        var document = Load("Light.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pageBackground = Assert.Single(document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value == "Color.Page.Background"));
        var cardSurface = Assert.Single(document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value == "Color.Card.Surface"));

        Assert.Equal("#F5F5F7", pageBackground.Value);
        Assert.Equal("#F7FFFFFF", cardSurface.Value);
    }

    [Theory]
    [InlineData("Dark.xaml", "#4018191D", "#D128292D")]
    [InlineData("Light.xaml", "#38FFFFFF", "#DCFDFDFF")]
    public void ThemeUsesTranslucentMaterialAndNeutralWindowBackdropFallback(
        string fileName,
        string expectedChrome,
        string expectedFloating)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var values = document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value is not null)
            .ToDictionary(
                element => element.Attribute(xaml + "Key")!.Value,
                element => element.Value);

        Assert.Equal(expectedChrome, values["Color.Material.Chrome.Fill"]);
        Assert.Contains("Color.Material.GlassEdgeHighlight", values.Keys);
        Assert.Contains("Brush.Material.GlassEdgeHighlight", values.Keys);
        Assert.Equal(expectedFloating, values["Color.Material.Floating.Fill"]);
        Assert.Equal("#0C1A1B1F", values["Color.LauncherBackground.Fallback"]);
        Assert.Equal("#B31A1B1F", values["Color.LauncherBackground.BlurFallback"]);
        Assert.Equal("#FF1A1B1F", values["Color.LauncherBackground.Image.DimOverlay"]);
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void CompactGlassMaterialRemainsNeutralAndHighlyTranslucent(string fileName)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var values = document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value is not null)
            .ToDictionary(
                element => element.Attribute(xaml + "Key")!.Value,
                element => element.Value);

        Assert.Equal("#4A14171B", values["Color.Material.ContrastChip.Fill"]);
        Assert.Equal("#38FFFFFF", values["Color.Material.ContrastChip.Highlight"]);
        Assert.Equal("#3DFFFFFF", values["Color.Material.ContrastChip.Border"]);
        Assert.Equal("#FFFFFFFF", values["Color.Material.ContrastChip.Foreground"]);
        Assert.Equal("#D8FFFFFF", values["Color.Material.CompactGlassEdgeHighlight"]);
    }

    [Theory]
    [InlineData("Dark.xaml", "0.16")]
    [InlineData("Light.xaml", "0.10")]
    public void ThemeDefinesSharedCardSurfaceShadow(string fileName, string expectedOpacity)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var shadow = Assert.Single(document.Descendants()
            .Where(element => element.Name.LocalName == "DropShadowEffect"
                && element.Attribute(xaml + "Key")?.Value == "Effect.Card.Surface"));

        Assert.Equal("18", shadow.Attribute("BlurRadius")?.Value);
        Assert.Equal("270", shadow.Attribute("Direction")?.Value);
        Assert.Equal(expectedOpacity, shadow.Attribute("Opacity")?.Value);
        Assert.Equal("1", shadow.Attribute("ShadowDepth")?.Value);
    }

    [Fact]
    public void ContentSectionBackdropDefersItsCrossDictionaryBaseStyleLookup()
    {
        var controlStyles = LoadStyle("ControlStyles.xaml");
        var sources = controlStyles.Descendants()
            .Select(element => element.Attribute("Source")?.Value)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Cast<string>()
            .ToList();
        var effectsIndex = sources.FindIndex(source => source.EndsWith(
            "/ControlStyles.Effects.xaml",
            StringComparison.Ordinal));
        var buttonsIndex = sources.FindIndex(source => source.EndsWith(
            "/ControlStyles.Buttons.xaml",
            StringComparison.Ordinal));

        Assert.InRange(effectsIndex, 0, int.MaxValue);
        Assert.True(effectsIndex < buttonsIndex);

        var buttons = LoadStyle("ControlStyles.Buttons.xaml");
        var sectionBackdrop = Assert.Single(buttons.Descendants()
            .Where(element => element.Name.LocalName == "BackdropBlurBorder"
                && element.Attribute("Style")?.Value.Contains(
                    "BackdropBlurBorderStyle",
                    StringComparison.Ordinal) == true));

        Assert.Equal(
            "{DynamicResource BackdropBlurBorderStyle}",
            sectionBackdrop.Attribute("Style")?.Value);
    }

    [Theory]
    [InlineData("Image.xaml", "False")]
    [InlineData("ImageBlur.xaml", "True")]
    public void ImageBackgroundLayersGateApprovedMaterialBlurWithControlBlurPreference(
        string fileName,
        string expectedValue)
    {
        var document = LoadBackground(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var values = document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value is not null)
            .ToDictionary(
                element => element.Attribute(xaml + "Key")!.Value,
                element => element.Value);

        Assert.Equal(expectedValue, values["Is.Material.ChromeBlur.Enabled"]);
        Assert.Equal(expectedValue, values["Is.Material.TransientBlur.Enabled"]);

        if (string.Equals(fileName, "ImageBlur.xaml", StringComparison.Ordinal))
        {
            Assert.DoesNotContain("Is.SecondaryMenu.BackdropBlur.Enabled", values.Keys);
            Assert.DoesNotContain("Is.Surface.BackdropBlur.Enabled", values.Keys);
        }
    }

    private static HashSet<string> LoadKeys(string fileName)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants()
            .Select(element => element.Attribute(xaml + "Key")?.Value)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static XDocument Load(string fileName) =>
        XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Resources",
            "Themes",
            fileName));

    private static XDocument LoadBackground(string fileName) =>
        XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Resources",
            "Themes",
            "Backgrounds",
            fileName));

    private static XDocument LoadStyle(string fileName) =>
        XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Styles",
            fileName));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("BLOCKHELM_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot)
            && Directory.Exists(configuredRoot)
            && File.Exists(Path.Combine(configuredRoot, "Launcher.sln")))
        {
            return new DirectoryInfo(configuredRoot);
        }

        return TryFindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory))
            ?? TryFindRepositoryRoot(new DirectoryInfo(Environment.CurrentDirectory))
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static DirectoryInfo? TryFindRepositoryRoot(DirectoryInfo root)
    {
        while (root.GetFiles("Launcher.sln").Length == 0)
        {
            if (root.Parent is null)
                return null;

            root = root.Parent;
        }

        return root;
    }
}
