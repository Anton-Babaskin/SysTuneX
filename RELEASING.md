# Releasing SysTuneX

One merge to `main` that changes behaviour is one release. The workflow does the publishing; the
only manual step is deciding the number and writing the notes.

## Versioning

[Semantic versioning](https://semver.org/), read for an application rather than a library:

| Part      | Bump it when                                                                 |
| --------- | ---------------------------------------------------------------------------- |
| **Major** | The change journal from an older version can no longer be read, a stored setting is dropped, or the app stops supporting a Windows version it used to. |
| **Minor** | A new feature, a new tweak, a new page — anything a user would notice as an addition. |
| **Patch** | A fix, with nothing added.                                                     |

A suffix (`v2.6.0-rc1`) marks the GitHub release as a prerelease automatically. Without one it is
marked as the latest release.

## Cutting a release

1. **Bump `release.version`** in the repository root. One line, the tag with its `v`: `v2.6.0`.
   This file is what the workflow reads; nothing else decides the tag.

2. **Add a section to `CHANGELOG.md`**, newest first, headed with the exact tag:

   ```markdown
   ## v2.6.0
   ```

   The release page shows **only** this section, sliced out by the heading. So write it for
   someone who has the previous version and wants to know whether to update — what changed and
   why it matters, not a list of commit subjects. If a decision in the release is worth
   defending, defend it there.

3. **Update both READMEs** if the change is user-visible. `README.md` and `README.ru.md` are kept
   in step; a feature documented in one and not the other is half-documented.

4. **Open a pull request** and let CI finish. The publish job builds the real single-file
   executable on Windows, so a packaging problem fails there rather than on someone's desktop.

5. **Merge to `main`.** The release job runs only on the default branch. It creates the tag,
   slices the changelog, appends `docs/release-footer.md`, and uploads `SysTuneX.exe` with its
   `SHA256SUMS.txt`.

## What the workflow will not do for you

- **It will not invent a version.** No `release.version`, no release.
- **It will not release from a branch.** The job is gated on the default branch, which is why a
  version bumped on a feature branch stays unpublished until it merges.
- **It will not re-release a tag that exists.** Bump the number instead of retagging; a published
  release that changes underneath people is worse than a version gap.

## Testing a build before it is released

Every push builds `SysTuneX.exe` and uploads it as a workflow artifact, branch or not. Open the
run under **Actions**, take `SysTuneX-win-x64` from the bottom of the page, and check it against
the `SHA256SUMS.txt` beside it. Artifacts expire after 30 days; releases do not.

## If a release goes out broken

Fix forward. Bump the patch version, write a changelog section that says plainly what was wrong,
and release again. Deleting or editing a published release breaks the checksum anyone already
recorded, and SysTuneX is a tool where people are right to check what they downloaded.
