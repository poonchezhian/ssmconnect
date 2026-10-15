# Contributing to SSMConnect

Thank you for helping make AWS Systems Manager easier to use. SSMConnect is built for people who are not infrastructure experts, so clear, friendly and reliable matters more than clever.

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to help

- **Report a bug** or a confusing screen using the [issue form](https://github.com/poonchezhian/ssmconnect/issues/new/choose).
- **Suggest an improvement.** Describe the problem you had, not only the feature you imagine.
- **Improve the docs.** The [wiki](https://github.com/poonchezhian/ssmconnect/wiki) pages are kept in the `wiki/` folder of the repository, so wiki fixes can be proposed as pull requests too.
- **Test** a release on a non-production server and tell us what felt hard.
- **Fix something.** Small, focused pull requests are the easiest to review.

## Before you start

1. Search the [existing issues](https://github.com/poonchezhian/ssmconnect/issues) and the wiki [Troubleshooting](https://github.com/poonchezhian/ssmconnect/wiki/Troubleshooting) and [FAQ](https://github.com/poonchezhian/ssmconnect/wiki/FAQ) pages.
2. For anything bigger than a small fix, **open an issue first** and describe what you plan to change. This avoids work that cannot be merged.
3. **Security problems are the exception.** Do not open a public issue. Follow the [security policy](SECURITY.md).

## Never post secrets

Issues, logs, screenshots and pull requests are public. Remove access keys, secret keys, session tokens, passwords, `.pem` and `.ppk` key contents, company sign-in addresses, account numbers and server names you do not want shared. The log file (**Help > Open log file**) is useful but check it before attaching.

## Building from source

You need Windows 10 or 11 (64-bit), the .NET 8 SDK and the WiX Toolset v3.14. The repository includes a helper that installs the build tools.

```
install-build-tools.ps1
Build-SSMConnect.cmd
```

`Build-SSMConnect.cmd` builds the app and the setup program and writes a log to `build\build.log`. A good build ends with `RESULT: SUCCESS` and **zero warnings**. The version number in the `VERSION` file goes up on every build, so please do not include changes to that file in a pull request.

The code is organised like this:

| Folder | What it holds |
| ------ | ------------- |
| `src/SSMConnect.Core` | Logic with no screens: AWS CLI handling, sign-in, discovery, tunnels, tool configuration |
| `src/SSMConnect.Wizard` | The Windows Forms app: setup wizard, dashboard, dialogs |
| `installer` | The WiX setup bundle and its build scripts |
| `wiki` | Source of the wiki pages |

## Code guidelines

- **Plain language in the interface.** Write messages for someone who has never used AWS. Say what happened and the one thing to do next. Avoid acronyms on screen unless the person already uses them.
- **Safe by default.** Tunnels listen on `127.0.0.1` only. Everything passed to the AWS CLI is validated and started as an argument list, never as a command string. Do not store passwords, keys or tokens, and never write secrets to logs.
- **Keep the user's own data safe.** Back up a file before editing it, and never change the person's own PuTTY or WinSCP sessions.
- **Run as the user.** Nothing may require administrator rights at run time.
- **No new warnings.** The build must stay at zero warnings.
- **Licence header.** New source files begin with the same Apache License 2.0 header as the existing ones.
- **Small changes.** One purpose per pull request. Do not mix a fix with a reformat.
- **Dependencies.** Avoid adding packages. The setup file must stay small, so say why in the pull request if a new dependency is truly needed.

## Pull requests

1. Fork the repository and create a branch with a short descriptive name, for example `fix-expired-sign-in-message`.
2. Make your change and build it. Test it on a real Windows PC, ideally against a non-production AWS account.
3. If people will notice the change, update the **README** and the matching page in `wiki/`, and add a line to `wiki/Whats-New.md`.
4. Open a pull request using the template. Say what changed, why, and how you tested it. Screenshots help for anything visual, with sample data only.
5. Be patient and kind with review. This is a small project and replies may take a few days.

Maintainers may ask for changes, or may decline a change that does not fit the goals of the project. That is never personal.

## Commit messages

Start with a short summary in the present tense, for example `Show a Sign in button on expired server cards`. Add a few lines explaining why if it is not obvious.

## Licence of contributions

SSMConnect is licensed under the [Apache License 2.0](LICENSE). Unless you state otherwise, any contribution you submit is licensed under the same terms, as described in section 5 of that licence. Only submit work you have the right to share.

## Questions

Open an issue with the question label or start from the [wiki](https://github.com/poonchezhian/ssmconnect/wiki).
