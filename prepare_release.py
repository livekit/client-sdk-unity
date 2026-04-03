import argparse
import configparser
import json
import os
import shutil
import subprocess
import sys

import install


def update_package_json(version):
    print(f"\n=== Updating package.json to version {version} ===")
    with open("package.json", "r") as f:
        data = json.load(f)
    data["version"] = version
    with open("package.json", "w") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    print("Done.")


def update_version_ini(ffi_version):
    tag = f"livekit-ffi@v{ffi_version}"
    print(f"\n=== Updating version.ini tag to {tag} ===")
    config = configparser.ConfigParser()
    config.read("version.ini")
    config["ffi"]["tag"] = tag
    with open("version.ini", "w") as f:
        config.write(f)
    print("Done.")


def download_ffi_binaries():
    print("\n=== Downloading FFI binaries ===")
    if os.path.isdir("downloads~"):
        print("Removing existing downloads~ directory...")
        shutil.rmtree("downloads~")
    install.main()
    print("Done.")


def update_submodule(ffi_version):
    tag = f"livekit-ffi/v{ffi_version}"
    print(f"\n=== Updating client-sdk-rust~ submodule to {tag} ===")
    subprocess.run(["git", "-C", "client-sdk-rust~", "fetch", "--tags", "--force"], check=True)
    subprocess.run(["git", "-C", "client-sdk-rust~", "checkout", tag], check=True)
    print("Done.")


def regenerate_protos():
    print("\n=== Regenerating protobuf files ===")
    subprocess.run(
        ["./generate_proto.sh"],
        cwd="BuildScripts~",
        check=True,
    )
    print("Done.")


def stage_all_changes():
    print("\n=== Staging all changes ===")
    subprocess.run([
        "git", "add",
        "package.json",
        "version.ini",
        "Runtime/Plugins",
        "client-sdk-rust~",
        "Runtime/Scripts/Proto/",
    ], check=True)
    print("Done.")


def main():
    parser = argparse.ArgumentParser(description="Prepare the repo for a new release.")
    parser.add_argument("sdk_version", help="SDK version (e.g. 1.4.0)")
    parser.add_argument("ffi_version", help="FFI version (e.g. 0.12.53)")
    args = parser.parse_args()

    print(f"Preparing release: SDK v{args.sdk_version}, FFI v{args.ffi_version}")

    update_package_json(args.sdk_version)
    update_version_ini(args.ffi_version)
    download_ffi_binaries()
    update_submodule(args.ffi_version)
    regenerate_protos()
    stage_all_changes()

    print("\n=== All done! ===")
    print("All changes have been staged. Review with:")
    print("  git diff --cached")
    print("  git status")


if __name__ == "__main__":
    main()
