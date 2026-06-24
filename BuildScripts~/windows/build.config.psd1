@{
    # livekit-ffi release tag to download and build.
    # Tags: https://github.com/livekit/rust-sdks/releases (the livekit-ffi/vX.Y.Z ones).
    Tag = 'livekit-ffi/v0.12.48'

    # Where to clone the rust source. Relative paths resolve against this tool folder; absolute
    # paths (e.g. 'C:\src') work too. Default '.src' is gitignored - if you point this at another
    # in-repo folder, add it to .gitignore yourself.
    SourceDir = '.src'

    # Where build-win.ps1 puts the resulting livekit_ffi.dll + livekit_ffi.pdb:
    #   $true  -> replace the binaries in Runtime/Plugins/ffi-windows-x86_64 (ready to ship)
    #   $false -> drop them in this tool folder for inspection (gitignored, copy them yourself)
    InstallToPlugins = $true

    # Delete the cloned source (the <SourceDir>\rust-sdks-<tag> folder) after a successful build.
    #   $false -> keep it so re-runs skip the clone (faster, but uses disk)
    #   $true  -> remove it once the DLL + PDB are placed
    CleanSourceAfterBuild = $false
}
