# Contributing

## Commit messages decide the version

Every push to `main` publishes a new release automatically. A tag in the commit message picks the version bump:

| Put this in the commit message | Version | Use it for |
|---|---|---|
| *(nothing)* | `1.0.3` → `1.0.4` (patch) | bug fixes, small tweaks |
| `#minor` | `1.0.4` → `1.1.0` (minor) | new features |
| `#major` | `1.1.0` → `2.0.0` (major) | big or breaking changes |
| `[skip release]` | no new version, tests only | docs, README, CI-only changes |

Examples:

```
Fix countdown when the reset is under a minute away
Add a battery-saver refresh interval #minor
Redesign the dashboard #major
Fix typo in README [skip release]
```

Notes:
- `#minor` / `#major` count if they appear in **any** commit in the push. `[skip release]` must be in the **last** commit.
- You can also release by hand: GitHub → Actions → Release → Run workflow, then pick the bump.
- Pushes to other branches and pull requests only run the tests, never release. Work on a branch and merge into `main` to ship.

Every new commit shows this reminder in the editor, once the template is on (`scripts/set-repo.sh` turns it on):

```sh
git config commit.template .gitmessage
```

## Before you push

Both apps must look and behave the same. Change [shared/DESIGN.md](shared/DESIGN.md) and both apps together.
If you change text or formatting, update [shared/fixtures/expectations.json](shared/fixtures/expectations.json),
and run both test suites (see the README's "Build from source").
