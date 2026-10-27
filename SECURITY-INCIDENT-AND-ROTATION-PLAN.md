# Security incident: credentials published in a public Git repository

**Status: UNRESOLVED. The exposed credentials have NOT been rotated.** Nothing in this
document has been executed against the production server or the production database. The
repository working tree has been cleaned (see "What has already been done"), but cleaning
the working tree does not undo publication.

---

## 0. CONFIRMED: live production accounts are openable with published passwords

This is not a theoretical exposure. It was verified offline against the production database
`LangDataPlatform` on 2026-10-27 by reading the stored PBKDF2 hashes (a read-only `SELECT`) and
recomputing them locally. **No sign-in was attempted and nothing was written.**

Result: **5 of the 6 production accounts, including the administrator and the agent, are opened
by a password that was printed in the public repository.**

| Production account | Role | Published credential works? |
| :--- | :--- | :--- |
| `#1` ending `001` | **admin** | **YES** |
| `#2` ending `002` | **agent** | **YES** |
| `#3` ending `003` | contributor | **YES** |
| `#4` ending `004` | contributor | **YES** |
| `#5` | contributor | no |
| `#6` ending `888` | contributor | **YES** |

The old `README.md` published a table of "Pre-Configured Sample Accounts" listing phone numbers
**and passwords**. Those were described as database seed data, but there is no seeding code in
the project — somebody created them as real accounts using exactly those passwords. The matching
phone numbers are also on the `ELDF_ADMIN_PHONES` / `ELDF_AGENT_PHONES` allowlists.

**Anyone who read the public repository can sign in to the live platform as administrator today.**
An administrator can read every contributor's name and phone number, approve or reject work, and
mark payouts as paid.

Treat section 3 step 1 (the abuse audit) as urgent, and add this before anything else:

```sql
-- Force every active session to end. Users simply sign in again.
USE LangDataPlatform;
DELETE FROM dbo.eldf_sessions;
```

Then have each of the five account owners set a new password. **There is currently no
password-change endpoint in the API** (see section 5), so until one exists the only route is to
replace `dbo.eldf_users.password_hash` directly. A ready-to-run generator for the correct hash
format is in section 5.

I did not change any production password myself: doing so would lock out the legitimate owners,
and that decision is yours.

---

## 1. What was exposed

Commit `f2d471d` on branch `main` is pushed to `origin/main` at
`https://github.com/afimule02-ui/lang.git`, and the GitHub API reports that repository as
**`visibility: public`**. Every file in that commit is therefore world-readable, and must be
assumed to have been read, indexed and archived by third parties.

The commit contains, in plain text:

| Secret | Files it appears in | Notes |
| :--- | :--- | :--- |
| SQL Server `sa` password for `142.132.228.77,1433` | `deploy oct26/.env`, `deploy oct26/appsettings.Production.json`, `deploy oct26/Eldf.Api/appsettings.json`, `deploy oct26/Eldf.Api/bin/.../appsettings.json`, `deploy oct26/DEPLOY-README.md`, `Eldf.Api/appsettings.json` (as committed) | 8 characters. **The same value is used for the production `LangDataPlatform` database and the `LangDataPlatform_Test` database.** |
| `ELDF_ADMIN_ENROLLMENT_TOKEN` | `deploy oct26/.env` | 17 characters |
| `ELDF_AGENT_ENROLLMENT_TOKEN` | `deploy oct26/.env` | 17 characters |
| Advertised sample-account passwords | `deploy oct26/app.js`, `deploy oct26/README.md` | The accounts were never seeded, but assume the strings were reused elsewhere |

The same commit also publishes the server address `142.132.228.77`, the SQL port `1433`, the
database names, and the admin/agent allowlisted phone numbers `+251911000001` /
`+251911000002`.

### Why this is severe

1. **Full database control.** `sa` is the SQL Server superuser. Anyone who read the commit can
   connect to `142.132.228.77,1433` and read, modify or drop `LangDataPlatform` — including
   contributor names, phone numbers, password hashes and consent records.
2. **Self-promotion to administrator.** `StaffRole()` in `Program.cs` grants the `admin` role to
   any registration whose phone number is on `ELDF_ADMIN_PHONES` **and** whose `staff_code`
   matches `ELDF_ADMIN_ENROLLMENT_TOKEN`. Both the phone number and the token are published, so
   an outsider can register themselves as an administrator of the live platform.
3. **Credentials in transit are unprotected.** `deploy oct26/appsettings.Production.json` and
   `deploy oct26/Eldf.Api/appsettings.json` set `Encrypt=False`, so the `sa` password crosses the
   network unencrypted.
4. **Session cookies are not secured.** `deploy oct26/.env` sets `ELDF_COOKIE_SECURE=false`, and
   the site is served over plain HTTP, so session tokens travel in clear text.

---

## 2. What has already been done (local working tree only)

These changes do **not** reduce the exposure of the published commit. They stop the secrets
from being committed again.

- `deploy oct26/.env` was removed from Git tracking with `git rm --cached` (**the file is still on
  disk**, so the deployment keeps working). It is now matched by `.gitignore`.
- 163 committed build-output files under `bin/` and `obj/` were removed from tracking.
- The hard-coded connection strings in `deploy oct26/Eldf.Api/appsettings.json`,
  `deploy oct26/appsettings.Production.json` and the stale `bin` copy were emptied. The
  application already prefers the `ELDF_SQLSERVER_CONNECTION_STRING` environment variable and
  throws a clear startup error when neither source is set, so the deployment is unaffected.
- Credentials in `deploy oct26/DEPLOY-README.md` were replaced with placeholders.
- The advertised sample-account passwords were removed from `deploy oct26/app.js`.
- The development connection string was moved out of `Eldf.Api/appsettings.Development.json`
  into the dotnet User Secrets store (`UserSecretsId` = `eldf-api-local-development`), which lives
  outside the repository. The API was restarted and verified to still reach SQL Server.
- `.gitignore` now covers `.env`, `**/.env`, `bin/`, `obj/` and the generated `wwwroot` frontend
  copies, while keeping `.env.example` tracked.

**None of this is committed.** Review `git status` and commit when you are satisfied.

---

## 3. Rotation plan

The ordering below is deliberate: the application is moved onto a brand-new least-privilege
login **before** the `sa` password changes, so the live service never depends on the rotation
itself. The only interruption is one container restart (a few seconds), and step 5 can be done
with no interruption at all.

### Step 0 — Containment (do first, no downtime)

1. Make the GitHub repository **private**, or delete it if it is not needed.
   This does not un-publish the commit, but it stops further crawling.
2. Restrict TCP 1433 at the firewall so only the application host can reach SQL Server.
   Right now the database port is reachable from the internet with a published superuser password.
   This single change removes most of the immediate risk.
3. Disable staff self-enrolment until the tokens are replaced. In `deploy oct26/.env` set:

   ```
   ELDF_ADMIN_ENROLLMENT_TOKEN=
   ELDF_AGENT_ENROLLMENT_TOKEN=
   ```

   `StaffRole()` requires a non-empty token, so an empty value makes admin and agent enrolment
   impossible. Apply with `docker compose up -d` (step 4).

### Step 1 — Audit for abuse before changing anything

Run these against `LangDataPlatform` and keep the output:

```sql
-- Unexpected admin or agent accounts
SELECT id, full_name, phone, role, is_active, created_at
FROM dbo.eldf_users
WHERE role IN ('admin','agent')
ORDER BY created_at DESC;

-- Everything that happened recently, with the responsible account
SELECT TOP 500 created_at, actor_user_id, actor_role, event_type, entity_type, entity_id, details
FROM dbo.eldf_activity_events
ORDER BY created_at DESC;

-- Sessions that are still valid
SELECT s.id, s.user_id, u.phone, u.role, s.expires_at
FROM dbo.eldf_sessions s JOIN dbo.eldf_users u ON u.id = s.user_id
WHERE s.expires_at > SYSUTCDATETIME();

-- Payouts marked paid, and who marked them
SELECT id, user_id, amount, status, reviewed_by_user_id, paid_at, payment_proof_reference
FROM dbo.eldf_payment_requests
WHERE status IN ('Approved','Paid')
ORDER BY reviewed_at DESC;
```

If any account, QA decision or payout is unexplained, treat this as a confirmed breach and
preserve the evidence before continuing.

### Step 2 — Create a least-privilege application login (no downtime)

The application must stop using `sa`. It needs read/write plus the ability to run EF migrations.

```sql
-- On the server
CREATE LOGIN eldf_app WITH PASSWORD = 'PUT-A-LONG-RANDOM-PASSPHRASE-HERE',
  CHECK_POLICY = ON;
GO
USE LangDataPlatform;
CREATE USER eldf_app FOR LOGIN eldf_app;
ALTER ROLE db_datareader ADD MEMBER eldf_app;
ALTER ROLE db_datawriter ADD MEMBER eldf_app;
-- Required because Program.cs runs EnsureCreated/Migrate at startup:
ALTER ROLE db_ddladmin ADD MEMBER eldf_app;
GO
```

Generate the password with at least 32 random characters, for example:

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 } | ForEach-Object { [byte]$_ }))
```

Do not reuse the old value and do not put it in any file inside the repository.

### Step 3 — Point the application at the new login (no downtime yet)

Edit `deploy oct26/.env` on the server only:

```
ELDF_SQLSERVER_CONNECTION_STRING="Server=tcp:142.132.228.77,1433;Database=LangDataPlatform;User Id=eldf_app;Password=<NEW_PASSWORD>;TrustServerCertificate=True;MultipleActiveResultSets=true;Encrypt=True;"
ELDF_ADMIN_ENROLLMENT_TOKEN=<NEW_LONG_RANDOM_SECRET>
ELDF_AGENT_ENROLLMENT_TOKEN=<A_DIFFERENT_LONG_RANDOM_SECRET>
ELDF_COOKIE_SECURE=true
```

`Encrypt=True` matters: the current production value is `Encrypt=False`, which sends the
password in clear text. Keep `TrustServerCertificate=True` only until a real certificate is
installed on SQL Server.

Set `ELDF_COOKIE_SECURE=true` **only once HTTPS is in front of the site** — see step 7. With
plain HTTP a secure cookie is never sent and nobody can sign in.

### Step 4 — Restart the application (one short restart)

```sh
cd "/path/to/deploy oct26"
docker compose config >/dev/null      # validates the env file before touching the service
docker compose up -d                  # recreates only the changed container
docker compose logs -f eldf-api       # watch for "Now listening on"
curl -fsS http://127.0.0.1:8001/health
```

`restart: unless-stopped` plus nginx in front means the gap is a few seconds. If `/health`
does not return `{"status":"ok"...}`, restore the previous `.env` and run `docker compose up -d`
again; the old `sa` password still works at this point, which is exactly why the `sa` change
comes afterwards.

### Step 5 — Change the `sa` password (no downtime)

Only now, when nothing uses `sa` any more:

```sql
ALTER LOGIN sa WITH PASSWORD = 'PUT-A-DIFFERENT-LONG-RANDOM-PASSPHRASE-HERE';
GO
-- Better still, once a separate sysadmin account exists:
-- ALTER LOGIN sa DISABLE;
```

Because the application is already on `eldf_app`, this change cannot interrupt the service.

### Step 6 — Invalidate anything the leaked credentials could have created

```sql
USE LangDataPlatform;
-- Force every user to sign in again (sessions are rows, so this is safe and reversible by design)
DELETE FROM dbo.eldf_sessions;

-- Disable any admin/agent account you did not create yourself.
-- Review the list from step 1 first. Disable rather than delete so the audit trail survives:
-- UPDATE dbo.eldf_users SET is_active = 0 WHERE id = <id>;
```

Also rotate the local development credential, because it is the same value as production:
the development connection string now lives in User Secrets at
`%APPDATA%\Microsoft\UserSecrets\eldf-api-local-development\secrets.json`.

### Step 7 — Fix the transport

Until HTTPS terminates in front of nginx, session cookies and passwords travel in clear text
over the network. Add a certificate (Let's Encrypt via certbot is free), redirect `:80` to
`:443` in `nginx.conf`, then set `ELDF_COOKIE_SECURE=true` and restart.

### Step 8 — Git history (destructive; coordinate first)

The secrets remain in commit `f2d471d` even after everything above. Removing them requires
rewriting history and a **force push**, which overwrites the remote branch and breaks every
existing clone. Do not do this casually, and do not treat it as a substitute for rotation —
the values must be considered compromised regardless.

If you decide to proceed, the usual tool is `git filter-repo`:

```sh
git filter-repo --invert-paths \
  --path "deploy oct26/.env" \
  --path "deploy oct26/appsettings.Production.json" \
  --path "deploy oct26/Eldf.Api/appsettings.json"
# then, with every collaborator informed:
git push --force origin main
```

GitHub also keeps the old objects reachable through the API and through forks for some time;
open a GitHub support request to purge cached views if that matters.

---

## 4. Verification checklist

Do not consider this closed until every line is ticked, and do not report the incident as
resolved on the strength of the plan alone.

- [ ] GitHub repository is private or deleted
- [ ] TCP 1433 is firewalled to the application host only
- [ ] Step 1 audit queries were run and the output was reviewed and kept
- [ ] `eldf_app` login exists with a new 32+ character password, and `sa` is no longer used by the app
- [ ] `sa` password changed (and `sa` disabled if a replacement sysadmin exists)
- [ ] Both enrolment tokens replaced with new long random values
- [ ] `Encrypt=True` in the production connection string
- [ ] `/health` returns `{"status":"ok"}` after the restart
- [ ] Attempting the **old** `sa` password is rejected by SQL Server
- [ ] Attempting registration with the **old** enrolment token yields the `contributor` role, not `admin`
- [ ] `DELETE FROM dbo.eldf_sessions` executed, forcing re-authentication
- [ ] Unexpected admin/agent accounts disabled
- [ ] HTTPS in front of the site and `ELDF_COOKIE_SECURE=true`
- [ ] Local development credential rotated too
- [ ] Decision recorded on whether to rewrite Git history
- [ ] **All five production accounts with published passwords have new passwords** (section 0)
- [ ] Re-run the section 5 checker and confirm it reports 0 compromised accounts

---

## 5. Replacing a user's password, and the missing password-change endpoint

### The gap

The API exposes `register`, `login`, `logout` and `me`. **There is no change-password and no
password-reset endpoint at all.** A user whose password is compromised has no way to replace it,
and an administrator has no way to force a reset: `PATCH /api/admin/users/{id}` can change the
name, role, active flag and agent assignment, but never the password.

This needs to be built. Until it is, passwords can only be replaced by writing the hash directly.

### Generating a hash in the format the API expects

`HashPassword()` in `Program.cs` stores `pbkdf2_sha256$310000$<saltHex>$<hashHex>` using
PBKDF2-HMAC-SHA256, a 16-byte random salt, 310,000 iterations and a 32-byte output. Anything
written into `password_hash` must match that layout or `VerifyPassword()` will reject it.

Save this as `new-password-hash.mjs` and run it with `node new-password-hash.mjs`:

```js
import crypto from "node:crypto";

// A strong random password, or replace with one chosen by the account owner.
const password = crypto.randomBytes(18).toString("base64url");
const salt = crypto.randomBytes(16);
const hash = crypto.pbkdf2Sync(password, salt, 310000, 32, "sha256");
const stored = `pbkdf2_sha256$310000$${salt.toString("hex")}$${hash.toString("hex")}`;

console.log("password (give this to the owner over a private channel, then delete it):");
console.log("  " + password);
console.log("password_hash (safe to paste into SQL):");
console.log("  " + stored);
```

Then, for each affected account:

```sql
USE LangDataPlatform;
UPDATE dbo.eldf_users
SET password_hash = 'pbkdf2_sha256$310000$...paste the generated value...'
WHERE id = 1;   -- repeat for 2, 3, 4, 6
```

Do not reuse one password across accounts, and do not store the plaintext anywhere afterwards.

### Confirming the fix

Re-run the offline checker to prove no account is still opened by a published password. It reads
hashes only and never attempts a sign-in:

```js
import crypto from "node:crypto";
import sql from "mssql";   // npm install mssql

// Read the three passwords out of the old commit rather than re-publishing them here:
//   git show f2d471d:deploy oct26/DEPLOY-README.md
// and copy the values from its account table into this array.
const PUBLISHED = ["...", "...", "..."];
const verify = (pw, stored) => {
  const [scheme, iter, saltHex, hashHex] = String(stored).split("$");
  if (scheme !== "pbkdf2_sha256") return null;
  const expected = Buffer.from(hashHex, "hex");
  const actual = crypto.pbkdf2Sync(pw, Buffer.from(saltHex, "hex"), Number(iter), expected.length, "sha256");
  return crypto.timingSafeEqual(actual, expected);
};

const pool = await new sql.ConnectionPool(process.env.ELDF_CHECK_CONNECTION).connect();
const { recordset } = await pool.request().query("SELECT id, role, password_hash FROM dbo.eldf_users");
let bad = 0;
for (const u of recordset) {
  if (PUBLISHED.some(pw => verify(pw, u.password_hash))) { bad++; console.log(`STILL COMPROMISED: #${u.id} ${u.role}`); }
}
console.log(bad === 0 ? "clean: no account uses a published password" : `${bad} account(s) still compromised`);
await pool.close();
```

---

## 6. Production is running an older database schema

Also discovered during the read-only inspection, and relevant before you deploy the current code:

`LangDataPlatform` has only 4 of the 12 tables the current code expects, and
`dbo.__EFMigrationsHistory` does not exist at all.

| Present in production | Missing in production |
| :--- | :--- |
| `eldf_users` (6 rows), `eldf_sessions` (5), `eldf_speakers` (4), `eldf_submissions` (2) | `eldf_agent_referrals`, `eldf_work_drafts`, `eldf_review_events`, `eldf_support_tickets`, `eldf_support_messages`, `eldf_payment_requests`, `eldf_activity_events`, `eldf_site_content` |

`dbo.eldf_users` is also missing the `is_active` and `referred_by_agent_id` columns.

So the live site is running the **pre-workflow** build. Deploying the current code will run
`EnsureCreatedAsync()` followed by `MigrateAsync()` at startup, and migration
`202610270001_AddServerWorkflows` will create the missing tables and columns. That migration is
written as idempotent `IF NOT EXISTS` SQL and only adds structures — its `Down()` is deliberately
empty so a rollback never drops collected data.

Before deploying:

1. **Back up `LangDataPlatform`.** The migration is additive, but the deploying account needs DDL
   rights and a backup is the only real safety net.
2. Note that `EnsureCreatedAsync()` is called before `MigrateAsync()`. On a database that already
   has tables, `EnsureCreated` does nothing, so the migration does the work. This combination is
   fragile and should eventually be reduced to `MigrateAsync()` alone.
3. There is no EF `ModelSnapshot` in the project, so `dotnet ef migrations add` cannot generate the
   next migration until one is created. Plan for that before the next schema change.
4. Run the deployment against a restored copy of the production database first.
