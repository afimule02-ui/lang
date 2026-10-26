# ELDF — AfriCorpus AI Deployment Guide
## Server: `142.132.228.77`

---

## Architecture

```
┌──────────────────────────────────────────────┐
│           142.132.228.77 (Server)            │
│                                              │
│  ┌────────────┐       ┌──────────────────┐   │
│  │   Nginx    │──────▶│  Eldf.Api (.NET) │   │
│  │  Port 80   │       │   Port 8001      │   │
│  │  (reverse  │       │                  │   │
│  │   proxy)   │       │  ● API endpoints │   │
│  └────────────┘       │  ● Static files  │   │
│                       │    (index.html,   │   │
│                       │     app.js,       │   │
│                       │     styles.css)   │   │
│                       └───────┬──────────┘   │
│                               │              │
│                       ┌───────▼──────────┐   │
│                       │    SQL Server    │   │
│                       │   Port 1433      │   │
│                       └──────────────────┘   │
│                                              │
│  Docker Volume: eldf-data → /data            │
│  (recordings stored here)                    │
└──────────────────────────────────────────────┘
```

## Ports

| Port | Service | Purpose |
|------|---------|---------|
| **80** | Nginx | Frontend + API (user-facing, reverse proxy) |
| **8001** | Eldf.Api | Direct API + static files (backend) |
| **1433** | SQL Server | Database (must already be running) |

## Quick Deploy

### 1. Upload files to server

```bash
# From your local machine:
scp -r "deploy oct26" root@142.132.228.77:/opt/eldf/
```

### 2. SSH into the server

```bash
ssh root@142.132.228.77
cd /opt/eldf
```

### 3. Run deploy script

```bash
chmod +x deploy.sh
./deploy.sh
```

### That's it! 🎉

- **Website**: http://142.132.228.77
- **API**: http://142.132.228.77:8001
- **Health check**: http://142.132.228.77/health

---

## CORS Configuration

CORS is configured to allow these origins:

- `http://142.132.228.77` (port 80 via nginx)
- `http://142.132.228.77:8001` (direct API access)
- `http://142.132.228.77:80` (explicit port 80)

The frontend auto-detects the API base URL:
- On `localhost` → uses `http://localhost:8001`
- On production → uses `location.origin` (same-origin, no CORS needed!)

Since the .NET API serves both the static frontend and API endpoints on the same port, requests from the browser via nginx (port 80 → 8001) are same-origin from the user's perspective.

---

## Files in this folder

```
deploy oct26/
├── docker-compose.yml        # Docker services (API + Nginx)
├── Dockerfile                # Multi-stage .NET 8 build
├── nginx.conf                # Reverse proxy config
├── deploy.sh                 # One-click deployment script
├── .env                      # Environment variables (SECRETS!)
├── .dockerignore             # Docker build exclusions
├── appsettings.Production.json  # Production app config
├── EthiopianLanguagesDataFactory.sln
├── Eldf.Api/                 # .NET API source
│   ├── Program.cs
│   ├── Models.cs
│   ├── Eldf.Api.csproj
│   ├── appsettings.json      # Production config
│   └── wwwroot/              # Built frontend assets
├── index.html                # Frontend entry
├── app.js                    # Frontend application
├── styles.css                # Frontend styles
├── data/                     # Language catalog data
│   └── africa-language-catalog.json
└── DEPLOY-README.md          # This file
```

---

## Sample Accounts (Pre-seeded)

| Name | Phone | Password | Role |
|------|-------|----------|------|
| Abebe Girma | +251911000001 | Admin@2026!! | admin |
| Tigist Haile | +251911000002 | Agent@2026!! | agent |
| Dawit Bekele | +251911000003 | Contrib@2026!! | contributor |
| Meron Tadesse | +251911000004 | Contrib@2026!! | contributor |

---

## Management Commands

```bash
# View logs
docker compose logs -f

# Restart all
docker compose restart

# Stop all
docker compose down

# Rebuild and restart
docker compose up -d --build

# View API logs only
docker compose logs -f eldf-api

# Shell into API container
docker exec -it eldf-api bash

# Database connection test
docker exec -it eldf-api dotnet --info
```

---

## SQL Server Prerequisite

SQL Server must be running on `142.132.228.77:1433` before deploying. 
The database `LangDataPlatform` will be auto-created by EF Core on first run.

If SQL Server is running in Docker:
```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Sol@2026" \
  -p 1433:1433 --name sqlserver --restart unless-stopped \
  -d mcr.microsoft.com/mssql/server:2022-latest
```

---

## Adding HTTPS Later

1. Add SSL certificates and update `nginx.conf` to listen on port 443
2. Set `ELDF_COOKIE_SECURE=true` in `.env`
3. Update `ELDF_ALLOWED_ORIGINS` to use `https://` prefix
4. Restart: `docker compose up -d --build`
