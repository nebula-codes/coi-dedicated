# Dedicated world lifecycle

The host uses your installed Captain of Industry assemblies. It creates and saves simulation data in the directory supplied by `--data-dir`.

Create a populated island and keep its simulation running:

```powershell
dotnet run --project src/CoiDedicated -- --serve --map "New Haven" --starting-factory --data-dir server-data
```

For another installation, add `--game-dir "PATH"` or set `COI_GAME_DIR`. The host requires the installed `Mafi.dll`, `Mafi.Core.dll`, and `Mafi.Base.dll` files.

Restart the existing island:

```powershell
dotnet run --project src/CoiDedicated -- --serve --data-dir server-data
```

`--serve` automatically resumes `server-data/island.save` when present. `--load PATH` selects an existing save to import into the server data directory. The server acquires an exclusive `.server.lock` file before preparing the world, preventing two host instances from using the same directory simultaneously.

Before opening a listener, startup creates or loads the world in a separate process and saves it. A new island then passes through a second process that loads and saves it again. This applies the game's load-time terrain repairs before the server accepts clients. The main process loads that prepared save and starts the service. Existing saves pass through the load-and-save preparation once on every restart.

Preparation logs and intermediate saves are retained under `server-data/.bootstrap/new` and `server-data/.bootstrap/canonical`. Preparation failures stop startup and preserve the current `island.save`. Successful promotion and later autosaves preserve the previous save as `island.save.previous`.

Press **Ctrl+C** after the service starts to save and stop. `--run-seconds 30` gives a bounded service run; `--autosave-seconds 60` selects the save interval. `--steps 0` without `--serve` loads or creates a world and saves it without advancing simulation, which is useful for persistence comparisons.

The bootstrap has been checked on game version **0.8.7d**. Populated New Haven retained selected world counters and terrain fingerprints across save/load after preparation. These checks are not a complete proof that every simulation field remains identical, and a null-string deserialization warning still appears in the game's loader.
