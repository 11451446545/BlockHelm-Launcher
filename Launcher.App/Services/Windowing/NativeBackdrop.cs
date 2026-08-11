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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Launcher.App.Services;

internal static class NativeBackdrop
{
    // WindowCompositionAttribute uses ABGR. A one-alpha neutral black keeps the
    // BlurBehind accent path active on newer DWM builds without visibly tinting it.
    private const int MinimalNeutralBlurGradientColor = 0x01000000;

    /// <summary>
    /// Enables the legacy compositor blur-behind effect without selecting a DWM
    /// system backdrop. The policy uses only a one-alpha neutral gradient so the
    /// desktop remains visible and is not recolored by the app theme.
    /// </summary>
    internal static BlurBehindApplyResult ApplyBlurBehind(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return BlurBehindApplyResult.NoWindowHandle;

        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        return TryApplyBlurBehind(handle);
    }

    public static void Enable(Window window, DwmSystemBackdropType backdropType, EffectiveTheme theme)
    {
        window.SourceInitialized += (_, _) =>
        {
            ApplyToWindow(window, backdropType, theme);
        };
    }

    public static bool ApplyToWindow(
        Window window,
        DwmSystemBackdropType backdropType,
        EffectiveTheme theme,
        bool extendIntoClientArea = true)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return false;

        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = extendIntoClientArea
                ? Colors.Transparent
                : GetOpaqueWindowBackgroundColor(theme);
        }

        return TryApply(handle, backdropType, theme, extendIntoClientArea);
    }

    public static bool TryApplyToPopup(Popup popup, DwmSystemBackdropType backdropType, EffectiveTheme theme)
    {
        if (popup.Child is null)
            return false;

        if (PresentationSource.FromVisual(popup.Child) is not HwndSource source)
            return false;

        if (source.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        return TryApply(source.Handle, backdropType, theme);
    }

    public static bool TryApply(
        IntPtr handle,
        DwmSystemBackdropType backdropType,
        EffectiveTheme theme,
        bool extendIntoClientArea = true)
    {
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            var margins = new Margins
            {
                Left = extendIntoClientArea ? -1 : 0,
                Right = extendIntoClientArea ? -1 : 0,
                Top = extendIntoClientArea ? -1 : 0,
                Bottom = extendIntoClientArea ? -1 : 0
            };
            _ = DwmExtendFrameIntoClientArea(handle, ref margins);

            var darkMode = theme is EffectiveTheme.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, DwmWindowAttribute.UseImmersiveDarkMode, ref darkMode, sizeof(int));

            var cornerPreference = (int)DwmWindowCornerPreference.Round;
            _ = DwmSetWindowAttribute(handle, DwmWindowAttribute.WindowCornerPreference, ref cornerPreference, sizeof(int));

            var borderColorNone = unchecked((int)0xFFFFFFFE);
            _ = DwmSetWindowAttribute(handle, DwmWindowAttribute.BorderColor, ref borderColorNone, sizeof(int));

            var backdrop = (int)backdropType;
            return DwmSetWindowAttribute(handle, DwmWindowAttribute.SystemBackdropType, ref backdrop, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public static Color GetOpaqueWindowBackgroundColor(EffectiveTheme theme)
    {
        return theme is EffectiveTheme.Light
            ? Colors.White
            : Color.FromRgb(0x15, 0x15, 0x15);
    }

    private static BlurBehindApplyResult TryApplyBlurBehind(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return BlurBehindApplyResult.NoWindowHandle;

        try
        {
            // Reset the native frame extension before enabling Accent blur. On
            // Windows 11 build 26200, a full-client (-1) DWM frame can resolve
            // WPF transparent pixels to the theme surface (white in light mode)
            // even though SetWindowCompositionAttribute reports success.
            var margins = new Margins
            {
                Left = 0,
                Right = 0,
                Top = 0,
                Bottom = 0
            };
            if (DwmExtendFrameIntoClientArea(handle, ref margins) != 0)
                return BlurBehindApplyResult.Failed;

            var policy = new AccentPolicy
            {
                State = AccentState.EnableBlurBehind,
                Flags = AccentFlags.None,
                GradientColor = MinimalNeutralBlurGradientColor,
                AnimationId = 0
            };
            var policySize = Marshal.SizeOf<AccentPolicy>();
            var policyPointer = Marshal.AllocHGlobal(policySize);
            try
            {
                Marshal.StructureToPtr(policy, policyPointer, fDeleteOld: false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.AccentPolicy,
                    Data = policyPointer,
                    SizeOfData = policySize
                };
                return SetWindowCompositionAttribute(handle, ref data) != 0
                    ? BlurBehindApplyResult.Applied
                    : BlurBehindApplyResult.Failed;
            }
            finally
            {
                Marshal.FreeHGlobal(policyPointer);
            }
        }
        catch (DllNotFoundException)
        {
            return BlurBehindApplyResult.Unavailable;
        }
        catch (EntryPointNotFoundException)
        {
            return BlurBehindApplyResult.Unavailable;
        }
        catch (COMException)
        {
            return BlurBehindApplyResult.Failed;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attribute, ref int attributeValue, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WindowCompositionAttributeData data);

    private enum DwmWindowAttribute
    {
        UseImmersiveDarkMode = 20,
        WindowCornerPreference = 33,
        BorderColor = 34,
        SystemBackdropType = 38
    }

    private enum DwmWindowCornerPreference
    {
        Round = 2
    }

    private enum WindowCompositionAttribute
    {
        AccentPolicy = 19
    }

    private enum AccentState
    {
        EnableBlurBehind = 3
    }

    [Flags]
    private enum AccentFlags
    {
        None = 0
    }

    internal enum DwmSystemBackdropType
    {
        None = 1,
        MainWindow = 2,
        TransientWindow = 3,
        TabbedWindow = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState State;
        public AccentFlags Flags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}

internal enum BlurBehindApplyResult
{
    NoWindowHandle,
    Applied,
    Unavailable,
    Failed
}
