# PwrMassCode – PowerToys Run plugin for massCode
Ai slop copy of the vscode extension 

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
- Currently doesnt work
- Type `new <name>` or `create <name>`

## Configuration
- Open PowerToys Settings > PowerToys Run > Plugins > `PwrMassCode`.
	- Change the  default primary keyword to ms 
- Port: The client defaults to `http://localhost:4321/`.
- Override via the `MASSCODE_PORT` environment variable.
- Example: set `MASSCODE_PORT=54321` to use `http://localhost:54321/`.


### Visual Studio
- Open the `PwrMassCode.csproj` (or add it to a solution)
- Select `x64` or `ARM64` platform
- Build Debug/Release
- Run debug.ps1 to deploy, or manually copy files (see Install section).


