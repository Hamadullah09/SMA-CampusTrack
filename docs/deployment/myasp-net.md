# Deploying to campus.sma-techno.net (MyASP.NET)

The whole product runs as **one IIS application**. The .NET API owns `/api/v1/*` and
`/hubs/*`; every other path falls through to the React app in `wwwroot`. That is why there is
no CORS configuration to maintain, no second certificate, and no rebuild when the domain
changes: the browser only ever talks to one origin, and the client asks for the relative path
`/api/v1`.

```
campus.sma-techno.net/                → wwwroot/index.html   (React, client-side routing)
campus.sma-techno.net/assets/*        → hashed JS and CSS
campus.sma-techno.net/brand/*         → logo and icons
campus.sma-techno.net/api/v1/*        → .NET API
campus.sma-techno.net/hubs/campus     → SignalR (live movement feed)
campus.sma-techno.net/health/live     → used by the deploy workflow to confirm the site came up
```

## Before the first deploy

Confirm with MyASP.NET that the plan provides all three. Any one of them missing changes the
plan, so it is worth asking before uploading 54 MB.

| Requirement | Why it matters | If it is missing |
|---|---|---|
| **ASP.NET Core Hosting Bundle, .NET 10** | The site is a .NET 10 app. IIS needs `AspNetCoreModuleV2` and the matching runtime. | Publish self contained instead: change `--self-contained false` to `true` in `.github/workflows/deploy.yml`. The upload grows to roughly 140 MB but stops depending on what the host has installed. |
| **MySQL database** | The schema is MySQL specific (see `docs/architecture/adr-0001-mysql-provider.md`). | The app will not run on MSSQL without a provider change. Host MySQL elsewhere and point the connection string at it. |
| **WebSockets enabled** | The live movement feed uses SignalR. | The feed silently falls back to long polling. Everything still works, just less promptly. |

## One-time setup

**1. Repository secrets.** In GitHub → Settings → Secrets and variables → Actions:

| Secret | Value |
|---|---|
| `FTP_HOST` | `win8238.site4now.net` |
| `FTP_USER` | `campus` |
| `FTP_PASSWORD` | The password set when the FTP account was created |
| `PROD_CONNECTION_STRING` | MySQL details from the control panel |
| `PROD_JWT_KEY` | Generate one: 64 random characters, never reused between environments |

Optional: `PROD_ADMIN_PASSWORD` and `PROD_RFID_BOOTSTRAP_KEY`. Both are generated randomly
when unset, so a missing value cannot leave a guessable administrator on a public site.

The deploy finds the web root itself. It lists the FTP account root and uploads into
`wwwroot/` when that exists, or into the account root when the account is already scoped to
the site folder. Set a repository **variable** named `FTP_REMOTE_DIR` to override.

**2. Production settings.** Copy `deploy/appsettings.Production.template.json` to
`appsettings.Production.json`, fill in every `REPLACE_` value, and upload it **by FTP to the
site root, once**. It is gitignored and the deploy workflow never touches it, so it survives
every deploy and never enters the repository.

Two values deserve care:

- **`Jwt:Key`** — generate a fresh one (`openssl rand -base64 64`). Anyone holding it can mint
  a valid token for any user, including an administrator. Never reuse the development key;
  it is public in this repository.
- **`SchoolTime:TimeZoneId`** — a Windows timezone id such as `Pakistan Standard Time`.
  Lateness and the daily attendance rollover are computed in this zone, so a wrong value
  shifts every recorded arrival time.

**3. First run.** Leave `Database:AutoMigrate` and `Database:AutoSeed` set to `true` for the
first deploy so the schema is created and the first administrator exists. **Turn `AutoSeed`
off afterwards**, and consider turning `AutoMigrate` off too, so a future deploy can never
alter the schema unattended. Sign in and change the seeded password immediately.

## Deploying

Actions → **Deploy to campus.sma-techno.net** → Run workflow.

The workflow is manual on purpose. An FTP upload is not atomic, so for a few seconds the site
serves a half replaced set of files. That is fine when someone is watching and poor as an
automatic consequence of a merge. To deploy on every push to `main`, uncomment the `push:`
trigger in `.github/workflows/deploy.yml`.

What it does:

1. Builds the React app. No `VITE_API_BASE` is set, deliberately, so the bundle uses the
   relative `/api/v1` and is not pinned to one environment.
2. Publishes the API for `win-x64`.
3. Copies the web build into `wwwroot/`, and `deploy/iis/web.config` over the one the SDK
   generates.
4. **Deletes `appsettings.Development.json`**, then fails the build if it or the development
   signing key survived. `dotnet publish` includes that file by default, and it carries a
   local connection string, a known admin password and a public signing key.
5. Uploads changed files only. `storage/` and `logs/` are excluded, so published APKs and
   assignment attachments are never deleted by a deploy.
6. Polls `/health/live` until the site answers.

## Publishing the mobile app

The APK is not built by the deploy workflow; Android builds need a JDK and the Android SDK, so
they live in **Actions → Build Android APK**. Run it, download the artefact, then upload it
from the admin portal at **Mobile app → Publish a build**. Families download it from
`campus.sma-techno.net/mobile-app`, which is reachable without signing in, because a parent
who has not got the app yet is exactly the person who needs that page.

The Android project is committed with the application id `net.smatechno.campustrack` and a
signing configuration that reads the keystore from repository secrets. Set those secrets
before the first release build: see [android-signing.md](android-signing.md). Until they are
set the workflow signs with the debug key and then deliberately fails, because a debug signed
build can never be upgraded in place by a properly signed one.

## When something is wrong

**The site returns 500.30 or a blank error page.** The app failed to start and the reason is
only in stdout. Set `stdoutLogEnabled="true"` in `web.config`, reproduce, read
`logs/stdout_*.log`, then set it back to `false`. On shared hosting that log has nothing
rotating it and will grow without limit.

**Every page except the home page 404s on refresh.** The fallback is not running, which
usually means the API is not handling the request at all. Check that `web.config` is present
at the site root and that `AspNetCoreModuleV2` is registered.

**The site loads but every request fails.** Almost always the connection string. Confirm the
MySQL host, and whether MyASP.NET requires `SslMode` on the connection.

**The live feed never connects.** WebSockets are disabled on the plan. The app degrades to
polling on its own; enable WebSockets in the control panel to get the immediate feed back.
