# Changelog (Linux edition)

All notable changes to the Linux edition of SSMConnect. Versions follow [Semantic Versioning](https://semver.org/);
Git tags and GitHub Releases are named `linux-vX.Y.Z` so they never clash with the Windows edition's `vX.Y.Z` tags.
While the major version is 0, the minor and patch numbers may change behaviour between releases.

## [0.0.1] - 2026-10-16
First iteration: the engine, without a window yet.
### Added
- Tunnel manager: one `aws ssm start-session` port-forward per server on `127.0.0.1`, quiet renewal of idle sessions,
  back-off, safety close when a port is reachable from the network, refusal to start on a port another program owns.
- AWS profile reader/writer for `~/.aws/config` and `credentials` (SSO sessions, organization accounts, access keys),
  with `.ssmconnect.bak` backups and owner-only file permissions.
- Server discovery, change detection and the daily passive rescan.
- Environment type from AWS tags (PROD, UAT, ...), SSH key matching by fingerprint, name or similar name.
- OpenSSH integration: `Host ssc-<server>` entries in `~/.ssh/ssmconnect.conf`, included from `~/.ssh/config`.
- Launchers for a terminal, the file manager (`sftp://`) and Remote Desktop (FreeRDP / Remmina).
- User-space installer for the AWS CLI and Session Manager plugin (no root needed).
- Command line: `doctor`, `install-tools`, `list`, `status`, `run`, `scan`, `cleanup`.
### Known limits
- Downloads of the AWS CLI and plugin are fetched over HTTPS from Amazon but their signatures are not yet verified.
