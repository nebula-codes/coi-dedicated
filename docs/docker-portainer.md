# Run the dedicated server in Portainer

This stack runs the island on a **Linux Intel/AMD 64-bit Docker host**. Portainer supplies start, stop, restart, logs, console, and health status. The Windows browser manager is not part of this image. Players continue using the Windows CoI multiplayer client.

This is an unofficial alpha for **Captain of Industry 0.8.7d**. It has the same command coverage as the Windows server, including native building batches, roads, transports, terrain, truck assignment, logistics, and many inspector controls. It supports shared speeds 1–4 and selected-state checkpoints. The matching client attempts bounded snapshot recovery after a connection or state failure. See the [coverage ledger](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.5/docs/multiplayer-coverage.md) and [synchronization limits](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.5/docs/synchronization.md); full gameplay parity and long-session determinism remain unproven.

## 1. Copy your installed game to the Linux host

Copy the **entire** `Captain of Industry` installation from your own Windows Steam library to a directory on the Linux machine running Docker, for example:

```text
/srv/coi/game/
  Captain of Industry_Data/
    Managed/Mafi.dll
    Managed/Mafi.Core.dll
    Managed/Mafi.Base.dll
    Plugins/x86_64/steam_api64.dll
  Maps/New Haven.map
  ...the rest of the installation...
```

Use your NAS file share, SFTP, or another normal file copy. Preserve the original filename case and directory structure. `/srv/coi/game` is an example **Linux host path**, not a path on the Windows PC or the machine merely displaying Portainer. The container needs read access to these files and traverse access to their directories as UID/GID **1654**.

Use matching game assemblies and DLC configuration on server and clients. The current integration supports the base-game save dependencies only. Keep a separate server copy so a Steam update cannot change a running island's game files. The public container and GitHub downloads contain our server software, not the game, Steam credentials, saves, or keys. No game login is entered in Portainer.

## 2. Deploy the stack

In your **Docker Standalone** environment in Portainer:

1. Open **Stacks → Add stack**, name it `coi-dedicated`, and select **Web editor**.
2. Paste [compose.portainer.yml](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.5/compose.portainer.yml).
3. Under **Environment variables**, add `COI_GAME_DIR` with the absolute Linux game directory, for example `/srv/coi/game`.
4. Optionally set `COI_BIND_IP` to the Linux host's LAN IP to restrict the published player port to that interface. Its default is `0.0.0.0`.
5. Select **Deploy the stack**. Open the container's **Logs** while it prepares the world. Health changes from `starting` to `healthy` once the simulation answers authenticated status requests.

The image is `ghcr.io/nebula-codes/coi-dedicated:0.1.0-alpha.5`, and the stack explicitly selects `linux/amd64`. ARM NAS/Raspberry Pi, Docker Swarm, and Kubernetes are not covered by this stack. Initial world creation can take several minutes; health has a five-minute startup allowance. A Docker `unhealthy` status is a diagnostic, not an automatic restart trigger.

The named volume **`coi-dedicated-data`** holds the island, keys, autosaves, and bootstrap logs. Docker initializes a new volume with ownership for the non-root app user. The explicit volume name preserves the same data when the stack is redeployed; use a different `COI_DATA_VOLUME` for a separate island. Run only one server against a data volume.

The game mount is read-only. The container root filesystem is also read-only; its persistent writes go to `/data`, and temporary files go to `/tmp`. A small symlink view at the stable path `/tmp/coi-game` accommodates the Windows game's backslash-based dependency paths on Linux, while still reading the original files. The view is recreated on each container start; the actual save and keys remain in `/data`. No game DLL is patched. The image runs as UID/GID **1654**, drops Linux capabilities, and does not require privileged mode or a Docker socket mount.

If your registry cannot fetch GHCR, use [compose.portainer.build.yml](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.5/compose.portainer.build.yml) instead. This builds from the tagged public repository and checks the SHA-256 of the public Linux package. It requires outbound access to GitHub, Microsoft's image registry, and Ubuntu package repositories. Your game folder remains a runtime mount and never becomes build context or image content.

## 3. Connect players

Allow inbound **TCP 28551** on the Linux host/NAS firewall from your home network. On a shared home network, no router port forwarding is needed. Use a trusted LAN or VPN: gameplay traffic is authenticated but not encrypted.

Give players:

- The Windows [multiplayer client download](https://github.com/nebula-codes/coi-dedicated/releases/tag/v0.1.0-alpha.5) and the [player guide](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.5/docs/player-guide.md). This container keeps the same `dedicated-prototype-5 / cmd1` wire protocol; exact game assembly fingerprints must also match.
- The **Linux host's LAN IP**, and player port **28551** (or your `COI_GAME_PORT`). Do not use the container's internal IP or the old Windows host address.
- The server's **join.key**, sent privately.

The server generates `/data/join.key` and `/data/control.key` on first start, unless you imported them. To copy the player key to your Linux host, find the container name in Portainer and run:

```sh
docker cp <container-name>:/data/join.key ./join.key
chmod 600 ./join.key
```

Alternatively, copy the key privately through a trusted volume/file manager. Avoid printing it into a shared console or posting it in an issue. Players select the full path to their private `join.key` file in the in-game **F8** panel. Keep **control.key** only with the server administrator.

Only the player port is published. Administration uses TCP 28550 inside the container; do not publish it. There is no browser dashboard port in this image.

## Move the current Windows island

Migrate before deploying a fresh island, or stop the new stack first. This transfers a copy; it does not remove your Windows data.

1. In the Windows manager, create and download a current backup. Download that profile's player `join.key` too.
2. Stop the Windows profile and wait for **Stopped**. If gameplay continued after the backup, take/download a new stopped-state backup so the transfer includes the final changes.
3. Copy the backup to the Linux host as `/srv/coi/import/island.save`, and the key as `/srv/coi/import/join.key`. These are private files, not GitHub assets.
4. On the Linux host, import them into an unused data volume with the commands below. If `island.save` or `join.key` already exists in that volume, the import deliberately stops. Back up existing data and select a new volume name to perform a separate import.
5. Deploy the stack with `COI_DATA_VOLUME=coi-dedicated-data`. It resumes `island.save` automatically, preserving the player's join key. Give players the Linux host's new address.

```sh
docker volume create coi-dedicated-data
docker run --rm --user 0:0 --entrypoint /bin/sh \
  --mount type=volume,source=coi-dedicated-data,target=/data \
  --mount type=bind,source=/srv/coi/import,target=/import,readonly \
  ghcr.io/nebula-codes/coi-dedicated:0.1.0-alpha.5 \
  -c 'set -eu; test ! -e /data/island.save; test ! -e /data/join.key; chown 1654:1654 /data; chmod 700 /data; install -o 1654 -g 1654 -m 600 /import/island.save /data/island.save; install -o 1654 -g 1654 -m 600 /import/join.key /data/join.key'
```

This one-time import runs as root to set file ownership; the game server runs as the non-root app user. It creates a fresh admin key on start. Preserve the Windows profile as a rollback copy and avoid running two independent copies if you expect one shared island history.

## Controls, backups, and updates

Use Portainer's container **Console** with `/bin/sh`. The wrapper reads the admin key from the data volume without putting its value in arguments:

```sh
/app/control.sh --command status
/app/control.sh --command pause
/app/control.sh --command resume
/app/control.sh --command save
/app/control.sh --command snapshot --output /data/backups/manual-001.save
```

Choose a new filename for each snapshot; existing files are never overwritten by the control client. Snapshot transfer verifies SHA-256. Copy the snapshot off the NAS/host with `docker cp <container-name>:/data/backups/manual-001.save ./manual-001.save` for a separate backup. Preserve the complete data volume for recovery, including keys; autosave's one previous generation does not replace offline backups.

Use Portainer **Stop** or **Restart** for lifecycle controls. Docker sends SIGTERM, and the host saves before exiting. The stack gives it **120 seconds** before Docker force-stops it. Larger/slow islands may need a longer `stop_grace_period`; a force kill cannot promise a final save. `restart: unless-stopped` restarts a crashed process or resumes after a Docker-host restart, while a deliberately stopped container stays stopped. Do not use the control client's `shutdown` for a permanent stop under this restart policy, because Docker can restart that exited process.

On restart, an existing `/data/island.save` takes precedence over new-world map settings. To create another island, use a new data volume and a different player port. Do not delete the old volume to change settings. When removing a stack, retain its data volume; deleting the volume deletes the island and keys.

Before updating, take an offline backup, stop the stack, and record the previous image tag. Update server/client releases and game files as a compatible set. Do not point the container at files being modified by Steam. If a new release cannot load your island, restore a separate backup with the previous game/image version.

## Configuration

| Portainer variable | Default | Meaning |
| --- | --- | --- |
| `COI_GAME_DIR` | Required | Absolute game installation path on the Linux Docker host. |
| `COI_DATA_VOLUME` | `coi-dedicated-data` | Persistent named volume; keep it across updates. |
| `COI_BIND_IP` | `0.0.0.0` | Host interface for the published player port. |
| `COI_GAME_PORT` | `28551` | Player port on the Linux host. Container port stays 28551. |
| `COI_MAP` | `New Haven` | Exact shipped map name without `.map`; new islands only. |
| `COI_STARTING_FACTORY` | `true` | Starting factory on a new island; `true` or `false`. |
| `COI_AUTOSAVE_SECONDS` | `60` | Autosave interval, minimum 10 seconds. |

For a host bind mount instead of the `/data` named volume, create a dedicated directory and assign UID/GID 1654 ownership before deployment. An existing named volume created by another image may also need its ownership repaired while the server is stopped. Do not solve write failures by enabling privileged mode or giving the whole game directory write access.

## Build the public Linux package

Developers need .NET SDK 10, PowerShell 7, this source checkout, and their own matching CoI installation. The compilation can run on Windows; the result is framework-dependent Linux x64 managed binaries, with no game DLLs copied into the package:

```powershell
pwsh ./scripts/package-linux-server.ps1 -GameDir 'F:\SteamLibrary\steamapps\common\Captain of Industry'
```

The script emits `CoI-Dedicated-Server-0.1.0-alpha.5-linux-x64.zip` and `SHA256SUMS-linux.txt`. Release maintainers publish the ZIP and checksum together. The release workflow selects the matching version and verifies its archive checksum before publishing the image; the Dockerfile also pins the current release for source builds. The runtime image supplies .NET 10. The Linux ZIP alone is not a self-contained executable and does not include the Windows browser manager.

Container behavior follows the official [Docker Compose service reference](https://docs.docker.com/reference/compose-file/services/) and [Microsoft .NET container guidance](https://learn.microsoft.com/dotnet/core/docker/introduction). Portainer's [stack deployment guide](https://docs.portainer.io/user/docker/stacks/add) describes its editor and environment variables.
