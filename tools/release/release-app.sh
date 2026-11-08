#!/bin/bash
# One app's release, for the Release workflow (.github/workflows/release.yml), run from the repo's top folder. An app describes its
# release in release.json in its folder: its name and tag, its tests, the projects it ships with each one's file name and the
# chips it's built for, and the folders it's built from.
#
#   tools/release/release-app.sh changed <folder>   exits 0 when the app needs a release: something it's built from changed
#                                                    since its last one (Markdown aside), it has none yet, or FORCE=true
#   tools/release/release-app.sh release <folder>   tests it, builds and signs each file, checks the signatures, and publishes
#                                                    <tag>-$VERSION with the files and the changes since the last release
#
# release needs VERSION (1.0.N), KEYSTORE (the signing key's file), STORE_PASS, KEY_PASS and KEY_ALIAS, ANDROID_HOME, JAVA_HOME,
# OUT (a working folder) and GH_TOKEN. EXPECTED_SHA256 (the key's fingerprint, in any case, with or without colons) stops a file
# signed with any other key from being published; NOTES adds the owner's words; DRY_RUN=true does everything but publish.
set -euo pipefail

command=$1
folder=${2%/}
config="$folder/release.json"
tag=$(jq -r .tag "$config")
name=$(jq -r .name "$config")
mapfile -t paths < <(jq -r '.paths[]' "$config")
pathspec=("${paths[@]}" ':(exclude,glob)**/*.md')
previous=$(git for-each-ref --count=1 --sort=-creatordate --format='%(refname:short)' "refs/tags/$tag-*")

case $command in
  changed)
    if [ "${FORCE:-false}" = true ]; then
      echo "$name: released whether or not it changed."
    elif [ -z "$previous" ]; then
      echo "$name: no release yet."
    elif git diff --quiet "$previous" HEAD -- "${pathspec[@]}"; then
      echo "$name: nothing it's built from changed since $previous."
      exit 1
    else
      echo "$name: changed since $previous."
    fi
    ;;

  release)
    mapfile -t tests < <(jq -r '.tests[]' "$config")
    for project in "${tests[@]}"; do
      dotnet test --project "$project"
    done

    out="$OUT/$tag"
    rm -rf "$out"
    mkdir -p "$out"
    want=$(printf '%s' "${EXPECTED_SHA256:-}" | tr -d ': ' | tr 'A-F' 'a-f')
    signer=""
    while read -r build; do
      project=$(jq -r .project <<<"$build")
      asset=$(jq -r .asset <<<"$build")
      # The chips it's built for, joined with + (MSBuild's command line splits on semicolons and commas); Directory.Build.targets
      # makes them a list again, on the app alone.
      runtimes=$(jq -r '.runtimes | join("+")' <<<"$build")
      marker="$out/.$asset.started"
      touch "$marker"
      dotnet build "$project" -c Release \
        -p:ApplicationDisplayVersion="$VERSION" \
        -p:AndroidPackageFormats=apk \
        -p:ReleaseRuntimeIdentifiers="$runtimes" \
        -p:AndroidKeyStore=true \
        -p:AndroidSigningKeyStore="$KEYSTORE" \
        -p:AndroidSigningStorePass=env:STORE_PASS \
        -p:AndroidSigningKeyAlias="$KEY_ALIAS" \
        -p:AndroidSigningKeyPass=env:KEY_PASS \
        -p:AndroidSdkDirectory="$ANDROID_HOME" \
        -p:JavaSdkDirectory="$JAVA_HOME"
      apk=$(find "artifacts/bin/$(basename "$project" .csproj)" -name '*-Signed.apk' -newer "$marker" -print -quit)
      if [ -z "$apk" ]; then
        echo "::error::Building $project made no signed app file."
        exit 1
      fi
      sha=$("$ANDROID_HOME/build-tools/36.0.0/apksigner" verify --print-certs "$apk" | sed -n 's/^Signer #1 certificate SHA-256 digest: //p')
      if [ -z "$sha" ]; then
        echo "::error::$asset isn't signed."
        exit 1
      fi
      if [ -n "$want" ] && [ "$sha" != "$want" ]; then
        echo "::error::$asset is signed by $sha, not by the devices' key ($want): it wouldn't install as an update, so nothing is published."
        exit 1
      fi
      echo "$asset: $(du -h "$apk" | cut -f1), signed by $sha"
      cp "$apk" "$out/$asset"
      signer=$sha
    done < <(jq -c '.builds[]' "$config")

    {
      if [ -n "${NOTES:-}" ]; then printf '%s\n\n' "$NOTES"; fi
      if [ -n "$previous" ]; then
        printf 'Changes since %s:\n\n' "$previous"
        git log --max-count=40 --format='- %s' "$previous..HEAD" -- "${pathspec[@]}"
      else
        echo "The first $name release from GitHub."
      fi
      printf '\nBuilt from %s. Signed by the key with SHA-256 %s.\n' "$(git rev-parse --short HEAD)" "$signer"
    } > "$out/notes.md"

    if [ "${DRY_RUN:-false}" = true ]; then
      echo "Dry run: $tag-$VERSION would be published with $(cd "$out" && ls ./*.apk | tr '\n' ' ')"
      cat "$out/notes.md"
      exit 0
    fi
    gh release create "$tag-$VERSION" "$out"/*.apk --target "${GITHUB_SHA:-$(git rev-parse HEAD)}" --title "$name $VERSION" \
      --notes-file "$out/notes.md"
    ;;

  *)
    echo "Use: tools/release/release-app.sh changed|release <folder>" >&2
    exit 2
    ;;
esac
