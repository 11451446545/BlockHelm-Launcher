namespace Launcher.Application.Services;

/// <summary>Publisher-assigned identity/order; independent of the user-facing version name.</summary>
public sealed record LauncherReleaseIdentity(string DisplayVersion, string ReleaseId, long Sequence);

public enum LauncherReleaseKind { Full, Patch }
