# Server Types and Tags

SSMConnect shows what kind of server each one is (Production, UAT, Development, ...) as a small tag on the card. It reads this from your **AWS tags**, not from the server name.

## How it decides

1. It looks at the server's tags, in this order: `Environment`, `Env`, `Stage`, `Tier` and similar names (any letter case, also keys ending in `:environment` or `-env`).
2. It normalises the value:

| Tag value (examples) | Shown as |
|---|---|
| `prod`, `PRD`, `production`, `live` | **PROD** (red) |
| `pre-prod`, `preprod` | Pre-production |
| `uat` | UAT |
| `stage`, `staging` | Staging |
| `test`, `qa` | Test |
| `dev`, `development` | Development |
| `dr` | DR |
| `sandbox` | Sandbox |
| `internal` | Internal |

3. Only when a server has **no such tag** does SSMConnect fall back to guessing from the name, word by word.

`non-prod` and `pre-prod` are never treated as production.

## Tag your servers for the best result

Add an `Environment` tag to every instance, for example `Environment = Production`. It takes a minute in the AWS console, and it makes the PROD marker exact.

## Servers saved before you tagged them

The type is stored when you save a server and refreshed from the last scan. After adding tags in AWS, run **Servers > Scan for new or changed servers** to pick them up.
