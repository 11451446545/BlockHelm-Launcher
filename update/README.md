# BlockHelm Launcher Remote Update Manifests

This directory stores release notes and the update manifest template for
BlockHelm Launcher releases. The default branch does not store live
`latest.json` manifests.

The `update` directory is not used as the launcher's local runtime
configuration. Live manifests are published to the `update-manifests` branch
of the configured GitHub repository:

- `update/release/latest.json`: stable release channel.
- `update/beta/latest.json`: beta channel.

## Release Checklist

When preparing a new version, update the build metadata in
`Launcher.App/Launcher.App.csproj` and add a matching release notes file:

- `release/notes/{versionName}.md` for stable releases.
- `beta/notes/{versionName}.md` for beta releases.

Stable release version names use eight uppercase hexadecimal digits and are
stored as a positive 32-bit JSON number:

- Stable example: `26A1708F` -> `648114319`. The first digit must be `0` through `7`.
- Beta builds continue to use `MMmmppbb` semantics, for example
  `0.9.1-beta.1` -> `90101`.

The GitHub Actions release workflows verify the tag, informational version,
channel, and version code before building the Windows x64 single-file
executable. They then create a GitHub Release and publish the matching
manifest to `update-manifests`.

Each final manifest contains one GitHub Release download URL. The client reads
the manifest from:

`https://raw.githubusercontent.com/{owner}/{repository}/update-manifests/update/{channel}/latest.json`

The initial manifest and executable URLs must point to the configured GitHub
repository over HTTPS. GitHub Release redirects may continue to an HTTPS asset
host and are limited to five hops. The downloaded executable must match the
manifest's exact byte size and SHA-256 hash before installation.

Update manifests are intentionally unsigned. The size and SHA-256 values
protect against corrupt or mismatched downloads, but do not provide an
independent authenticity guarantee if the GitHub repository or publishing
workflow is compromised.
