# Releases

This project uses automated GitHub Actions to build and release cross-platform packages for BatchExport.

## Package Contents

Each release package includes:
- **Self-contained executable**: No .NET runtime installation required
- **Game presets**: Pre-configured settings for supported games
- **Configuration template**: `appsettings.template.json` for manual setup
- **Documentation**: Complete README with usage instructions

## Release Features

- **Automatic releases**: Created when version tags (e.g., `v1.0.0`) are pushed
- **Manual releases**: Can be triggered via GitHub Actions workflow dispatch
- **Multi-platform**: Windows, Linux
- **Self-contained**: No dependencies or runtime installation needed
- **Single file**: Each executable is bundled into a single file for easy distribution

## Creating a New Release

### For Maintainers

1. **Create and push a version tag:**
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

2. **GitHub Actions will automatically:**
   - Build for all supported platforms
   - Create self-contained executables
   - Package with presets and documentation
   - Create a GitHub release with downloadable artifacts

### Manual Release Trigger

Releases can also be triggered manually:
1. Go to the **Actions** tab in the GitHub repository
2. Select **Build and Release BatchExport** workflow
3. Click **Run workflow**
4. Choose the branch and click **Run workflow**

## Build Process

The automated build process:

1. **Compilation**: Builds on Windows and Linux runners
2. **Self-contained publishing**: Creates executables that don't require .NET runtime
3. **Asset bundling**: Includes presets, templates, and documentation
4. **Archive creation**: Packages everything into platform-specific archives
5. **Release creation**: Publishes to GitHub Releases with auto-generated notes

## Version Naming

Follow semantic versioning for tags:
- **Major version**: `v1.0.0` - Breaking changes
- **Minor version**: `v1.1.0` - New features, backward compatible
- **Patch version**: `v1.0.1` - Bug fixes, backward compatible

## Quick Start After Download

1. **Extract the package** for your platform
2. **Run with presets:**
   ```bash
   # Windows
   .\BatchExport.exe --preset WarRobotsFrontiers --pak-dir "C:\Game\Paks" --output "C:\Export" --mappings "C:\mappings.usmap"

   # Linux
   ./BatchExport --preset WarRobotsFrontiers --pak-dir "/path/to/paks" --output "/path/to/export" --mappings "/path/to/mappings.usmap"
   ```

3. **See all options:**
   ```bash
   # Windows
   .\BatchExport.exe --help

   # Linux
   ./BatchExport --help
   ```

For detailed usage instructions, see the main [README](README.md).