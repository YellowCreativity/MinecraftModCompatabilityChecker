# MinecraftModCompatabilityChecker

Visualizes version compatibility between multiple mods in a table structure.

## Usage

Run the exe within a command prompt or PowerShell window. The first argument is the target mod loader (ex. "forge",
"neoforge", "fabric"). The second optional argument is a path to the modlist.txt file, If not provided it assumes the
modlist.txt file is in the executing directory.

Example:
``MinecraftModCompatabilityChecker.exe fabric "C:\Users\Purple\Desktop\modlist.txt"``

### modlist.txt

Each line is a link to a modrinth mod, resource pack, datapack or shader, as an example:<br>
`https://modrinth.com/mod/create-fabric`<br>
`https://modrinth.com/resourcepack/fresh-animations`<br>
`https://modrinth.com/datapack/tidal-towns`<br>
`https://modrinth.com/shader/complementary-reimagined`<br>

If a line begins with a `#`, It's ignored:<br>
`...`<br>
`https://modrinth.com/mod/fabric-seasons-delight-compat`<br>
`# ^ Only if Fabric Seasons gets added`<br>
`...`

