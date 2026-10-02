param([string]$Baseline = 'v26A17091', [string]$ExpectedVersion = '26A17092')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    # Includes code-based animation and control rendering, not just XAML.
    $paths = @('Launcher.App/Views', 'Launcher.App/Styles', 'Launcher.App/Resources',
        'Launcher.App/Animations', 'Launcher.App/Effects', 'Launcher.App/Assets',
        'Launcher.App/Behaviors', 'Launcher.App/Controls', 'Launcher.App/Converters',
        'Launcher.App/Services/Navigation', 'Launcher.App/Services/Presentation',
        'Launcher.App/Services/Theming', 'Launcher.App/App.xaml')
    $entries = @(& git -c core.quotepath=false ls-tree -r $Baseline -- $paths)
    if ($LASTEXITCODE -ne 0 -or $entries.Count -eq 0) { throw 'Original 91 baseline is unavailable.' }
    # The user requested a text-contrast repair in both runtimes, not a redesign.
    $contrastFiles = @('Launcher.App/Controls/Lists/ListPageItemButton.xaml',
        'Launcher.App/Resources/Themes/Light.xaml', 'Launcher.App/Resources/Themes/Dark.xaml',
        'Launcher.App/Styles/ControlStyles.Lists.xaml', 'Launcher.App/Styles/ControlStyles.Page.xaml')
    # Theme selection is a separately requested behavior repair, with no design changes.
    $behaviorFiles = @('Launcher.App/Services/Theming/ThemeService.cs')
    $identical = 0
    $contrastChanges = 0
    $behaviorChanges = 0
    foreach ($entry in $entries) {
        if ($entry -notmatch '^\d+ blob ([a-f0-9]+)\t(.+)$') { throw 'Invalid baseline entry.' }
        $expected = $Matches[1]
        $path = $Matches[2]
        $actual = & git hash-object ('--path=' + $path) -- $path
        if ($LASTEXITCODE -ne 0) { throw "Cannot verify original file: $path" }
        if ($actual -eq $expected) { $identical++; continue }
        if ($path -in $contrastFiles) { $contrastChanges++; continue }
        if ($path -in $behaviorFiles) { $behaviorChanges++; continue }
        throw "Unexpected original 91 design change: $path"
    }
    [xml]$project = Get-Content 'Launcher.App/Launcher.App.csproj'
    if ($project.Project.PropertyGroup.InformationalVersion -notcontains $ExpectedVersion) {
        throw 'The expected launcher version is missing.'
    }
    if ($project.Project.PropertyGroup.InformationalVersion -notcontains ($ExpectedVersion + '-Compatible')) {
        throw 'The compatibility version suffix is missing.'
    }
    [pscustomobject]@{Baseline=$Baseline;IdenticalDesignFiles=$identical;ApprovedContrastFiles=$contrastChanges;ApprovedBehaviorFiles=$behaviorChanges;Result='PASS'}
}
finally { Pop-Location }
