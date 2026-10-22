# TheMatrix

The standalone development repository for **Matrix Bridge**,
a Unity Editor package that bundles its local MCP server and can be installed independently
from FeederBase.

## Requirements

- Unity 2022.3 or newer
- Windows x64 for the currently bundled local server

## Install in another Unity project

Open **Window > Package Manager**, click **+**, choose **Add package from git URL**, paste this URL and
click **Add**:

```text
https://github.com/DigitalGhost2362/TheMatrix.git?path=/Packages/com.feeder.mcp
```

Or add the same URL to the project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.feeder.mcp": "https://github.com/DigitalGhost2362/TheMatrix.git?path=/Packages/com.feeder.mcp"
  }
}
```

No scoped registry, installer or FeederBase package is needed. Git must be installed on the machine.

### Update

Open **Window > Package Manager**, select **Matrix Bridge** and click **Update**. Unity fetches the
newest version from `main`.

### Projects that used the Matrix registry

Replace the `com.feeder.mcp` version number in `Packages/manifest.json` with the URL above, and remove
the `Matrix` entry under `scopedRegistries`.

## Use

Open the project once after installing: the package auto-starts the local MCP server, writes
the project-root `.mcp.json` for Claude Code, and generates `.claude/skills`. No manual setup
is needed for Claude Code.

For other AI clients or to opt out of auto-configure, open
**Tools > Feeder > Bridge**.

## Repository layout

- `Packages/com.feeder.mcp`: the distributable UPM package
- `Assets`, `ProjectSettings`, and the remaining `Packages` files: the local development project

Projects that install Matrix Bridge receive only `Packages/com.feeder.mcp`.

## Release checklist

1. Set the new version in `Packages/com.feeder.mcp/package.json` and in `MatrixPlugin.Version`
   (`Packages/com.feeder.mcp/Runtime/MatrixPlugin.cs`).
2. Add the release notes to `Packages/com.feeder.mcp/CHANGELOG.md`.
3. Open this project in the minimum supported Unity version and verify there are no compile errors.
4. Commit and push to `main`. Projects get the release the next time someone clicks **Update** in
   Package Manager.

## License

MIT. See [LICENSE](LICENSE).
