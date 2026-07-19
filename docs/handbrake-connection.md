# HandBrake Detection and Connection

The desktop application can discover multiple installed and portable HandBrake copies without modifying HandBrake program files.

## Detection sources

The **Find HandBrake** operation inspects the following sources:

1. Running `HandBrake.exe` processes whose executable path is readable.
2. Standard `Program Files\HandBrake` locations.
3. Windows uninstall records for the current user and local machine in 32-bit and 64-bit registry views.
4. Previously tested executable paths.
5. A HandBrake executable selected with Browse.

Selected folders are searched to a maximum depth of four directories. Inaccessible folders and protected processes are skipped without failing the entire search.

Each result includes:

- Executable location
- File version when available
- Installed, portable, or unknown type
- Whether that copy is currently running
- Whether a previously saved location is missing

Start menu shortcut resolution is not currently implemented.

## Saved connections

A connection is saved only after **Test Pipeline** succeeds. Connection state is stored at:

```text
%LOCALAPPDATA%\HandBrake Completed Manager\handbrake-connections.json
```

When `HBCM_DATA_DIRECTORY` is set, the connection file is stored there instead. No settings are written inside a HandBrake installation directory.

## Pipeline validation

**Test Pipeline** performs the following non-destructive validation:

1. Confirm the selected `HandBrake.exe` still exists.
2. Create an isolated database under the Windows temporary directory.
3. Simulate a completion event using the selected executable as harmless file metadata.
4. Pass the event through capture, duplicate identity, SQLite insertion, and query code.
5. Confirm exactly one record was persisted.
6. Close SQLite and remove the temporary database.
7. Save the selected installation as connected only when every step succeeds.

The test never writes a fake row into the real completed history and never modifies HandBrake files or preferences.

## HandBrake configuration

After the desktop solution is built, launch the application and expand **Configure completed-encode capture**. The panel displays the receiver executable copied beside the desktop executable and the required arguments.

In HandBrake:

1. Open **Tools > Preferences > When Done**.
2. Under **Encode Completed**, enable **Send File To**.
3. Set the executable to the receiver path displayed by HandBrake Completed Manager.
4. Set the arguments to:

   ```text
   --source {source} --destination {destination} --destination-folder {destination_folder} --exit-code {exit_code}
   ```

5. Save the preference and complete a short test encode.
6. Refresh HandBrake Completed Manager and confirm the encode appears in history.

HandBrake replaces the placeholders with the completed job values. It also supplies equivalent `HB_SOURCE`, `HB_DESTINATION`, `HB_DESTINATION_FOLDER`, and `HB_EXIT_CODE` environment variables, which the receiver accepts as a fallback.

The application intentionally does not edit HandBrake's `settings.json`. HandBrake owns and may rewrite the complete settings file while running; manual configuration prevents unrelated preferences from being overwritten or corrupted.
