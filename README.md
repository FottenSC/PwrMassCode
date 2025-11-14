# PwrMassCode – PowerToys Run plugin for massCode

PwrMassCode lets you search and copy your massCode snippets directly from PowerToys Run.
It talks to the local massCode HTTP API and exposes fast search, copy-to-clipboard, and quick create from clipboard.


## Requirements
- PowerToys with PowerToys Run enabled
- massCode app running on the same machine
 - Default port: `4321` 
 - Optional: set `MASSCODE_PORT` environment variable to override the port for this plugin


## Install and enable
There are two ways to get the plugin into PowerToys Run:

## Usage
- Open PowerToys Run and type the plugin action keyword, then your search.
- Default action keyword: `plug` (can be changed, see below)
- Search matches across:
 - `snippet.Name`
 - `content.Label`
 - `content.Language`
 - `snippet.Folder.Name`
 - `content.Value` (fulltext contains)
- Each snippet content appears as a separate result row (flattened view).
- Press Enter on a result to copy the content value to the clipboard.

### Create a new snippet from the clipboard
- Type `new <name>` or `create <name>`
- If your clipboard has text, you will see a suggestion:
 - Title: `Create massCode snippet: <name>`
 - Subtitle: `From clipboard (Fragment1 • plain_text)`
- Press Enter to create the snippet with one content fragment labeled `Fragment1` and language `plain_text`.


## Settings
Open PowerToys Settings > PowerToys Run > Plugins > `PwrMassCode`.

- Action keyword
 - Change the primary keyword (default `plug`) that invokes the plugin.
- PwrMassCode option
 - The plugin exposes a single boolean option in the PowerToys UI.
 - It’s currently reserved/no-op and does not change behavior.


## Configuration
- Port: The client defaults to `http://localhost:4321/`.
 - Override via the `MASSCODE_PORT` environment variable.
 - Example: set `MASSCODE_PORT=54321` to use `http://localhost:54321/`.


## Build
You can build with Visual Studio or the .NET CLI. The project targets `.NET9` and uses WPF.

### .NET CLI
- From `PwrMassCode` directory:
 - Restore: `dotnet restore`
 - Build x64 Debug: `dotnet build -c Debug -p:Platform=x64`
 - Build x64 Release: `dotnet build -c Release -p:Platform=x64`
 - Build ARM64 Release: `dotnet build -c Release -p:Platform=ARM64`
- Output folder: `PwrMassCode/bin/<Arch>/<Configuration>/net9.0-windows`

### Visual Studio
- Open the `PwrMassCode.csproj` (or add it to a solution)
- Select `x64` or `ARM64` platform
- Build Debug/Release
- Run debug.ps1 to deploy, or manually copy files (see Install section).

After building, use the debug script or manual copy to deploy into PowerToys.



