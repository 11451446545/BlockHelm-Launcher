/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Launcher.App.Effects;

internal sealed class LiquidGlassRefractionEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(
            nameof(Input),
            typeof(LiquidGlassRefractionEffect),
            0,
            SamplingMode.Bilinear);

    public static readonly DependencyProperty EdgeXProperty = RegisterConstantProperty(nameof(EdgeX), 0.5d, 0);
    public static readonly DependencyProperty EdgeYProperty = RegisterConstantProperty(nameof(EdgeY), 0d, 1);
    public static readonly DependencyProperty NormalXProperty = RegisterConstantProperty(nameof(NormalX), 0d, 2);
    public static readonly DependencyProperty NormalYProperty = RegisterConstantProperty(nameof(NormalY), 1d, 3);
    public static readonly DependencyProperty AspectRatioProperty = RegisterConstantProperty(nameof(AspectRatio), 1d, 4);
    public static readonly DependencyProperty IntensityProperty = RegisterConstantProperty(nameof(Intensity), 0d, 5);
    public static readonly DependencyProperty RefractionRadiusProperty = RegisterConstantProperty(nameof(RefractionRadius), 0.14d, 6);
    public static readonly DependencyProperty DistortionAmountProperty = RegisterConstantProperty(nameof(DistortionAmount), 0.008d, 7);
    public static readonly DependencyProperty PhaseProperty = RegisterConstantProperty(nameof(Phase), 0d, 8);
    public static readonly DependencyProperty HighlightGainProperty = RegisterConstantProperty(nameof(HighlightGain), 1d, 9);
    public static readonly DependencyProperty RestingRefractionProperty = RegisterConstantProperty(nameof(RestingRefraction), 0d, 10);
    public static readonly DependencyProperty CornerRadiusProperty = RegisterConstantProperty(nameof(CornerRadius), 0.1d, 11);

    private LiquidGlassRefractionEffect(PixelShader pixelShader)
    {
        PixelShader = pixelShader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(EdgeXProperty);
        UpdateShaderValue(EdgeYProperty);
        UpdateShaderValue(NormalXProperty);
        UpdateShaderValue(NormalYProperty);
        UpdateShaderValue(AspectRatioProperty);
        UpdateShaderValue(IntensityProperty);
        UpdateShaderValue(RefractionRadiusProperty);
        UpdateShaderValue(DistortionAmountProperty);
        UpdateShaderValue(PhaseProperty);
        UpdateShaderValue(HighlightGainProperty);
        UpdateShaderValue(RestingRefractionProperty);
        UpdateShaderValue(CornerRadiusProperty);
    }

    public Brush? Input
    {
        get => (Brush?)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public double EdgeX
    {
        get => (double)GetValue(EdgeXProperty);
        set => SetValue(EdgeXProperty, value);
    }

    public double EdgeY
    {
        get => (double)GetValue(EdgeYProperty);
        set => SetValue(EdgeYProperty, value);
    }

    public double NormalX
    {
        get => (double)GetValue(NormalXProperty);
        set => SetValue(NormalXProperty, value);
    }

    public double NormalY
    {
        get => (double)GetValue(NormalYProperty);
        set => SetValue(NormalYProperty, value);
    }

    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    public double Intensity
    {
        get => (double)GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    public double RefractionRadius
    {
        get => (double)GetValue(RefractionRadiusProperty);
        set => SetValue(RefractionRadiusProperty, value);
    }

    public double DistortionAmount
    {
        get => (double)GetValue(DistortionAmountProperty);
        set => SetValue(DistortionAmountProperty, value);
    }

    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public double HighlightGain
    {
        get => (double)GetValue(HighlightGainProperty);
        set => SetValue(HighlightGainProperty, value);
    }

    public double RestingRefraction
    {
        get => (double)GetValue(RestingRefractionProperty);
        set => SetValue(RestingRefractionProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    internal static bool TryCreate(out LiquidGlassRefractionEffect? effect, out Exception? exception)
    {
        if (!LiquidGlassRefractionShader.TryGet(out var shader, out exception) || shader is null)
        {
            effect = null;
            return false;
        }

        effect = new LiquidGlassRefractionEffect(shader);
        return true;
    }

    private static DependencyProperty RegisterConstantProperty(
        string name,
        double defaultValue,
        int registerIndex)
    {
        return DependencyProperty.Register(
            name,
            typeof(double),
            typeof(LiquidGlassRefractionEffect),
            new UIPropertyMetadata(defaultValue, PixelShaderConstantCallback(registerIndex)),
            IsFinite);
    }

    private static bool IsFinite(object value) => double.IsFinite((double)value);
}
