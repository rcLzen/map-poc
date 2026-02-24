# .NET 9.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that a .NET 9.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 9.0 upgrade.
3. Upgrade DwgMapOverlayPoc.Tests\DwgMapOverlayPoc.Tests.csproj

## Settings

This section contains settings and data used by execution steps.

### Excluded projects

| Project name                        | Description                                                  |
|:------------------------------------|:-------------------------------------------------------------|
| DwgMapOverlayPoc.csproj             | No issues found during analysis, no upgrade needed           |

### Project upgrade details

This section contains details about each project upgrade and modifications that need to be done in the project.

#### DwgMapOverlayPoc.Tests\DwgMapOverlayPoc.Tests.csproj modifications

Project properties changes:
  - Target framework should be changed from `net8.0` to `net9.0`
