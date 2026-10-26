#!/bin/bash
# ============================================================
#  ELDF Deploy Script — 142.132.228.77
#  Run this ON the server after uploading the deploy oct26 folder
# ============================================================
set -e

echo "============================================="
echo "  ELDF - AfriCorpus AI Deployment"
echo "  Server: 142.132.228.77"
echo "  Ports:  80 (frontend+api via nginx)"
echo "          8001 (direct API access)"
echo "============================================="

# 1. Ensure Docker & Docker Compose are installed
if ! command -v docker &> /dev/null; then
    echo "[!] Docker not found. Installing..."
    curl -fsSL https://get.docker.com | sh
    systemctl enable docker
    systemctl start docker
fi

if ! docker compose version &> /dev/null; then
    echo "[!] Docker Compose v2 not found. Installing plugin..."
    apt-get update && apt-get install -y docker-compose-plugin
fi

echo "[✓] Docker $(docker --version)"
echo "[✓] $(docker compose version)"

# 2. Stop any existing containers
echo ""
echo "[*] Stopping existing containers (if any)..."
docker compose down 2>/dev/null || true

# 3. Build and start
echo ""
echo "[*] Building Docker image..."
docker compose build --no-cache

echo ""
echo "[*] Starting services..."
docker compose up -d

echo ""
echo "[*] Waiting for services to start..."
sleep 5

# 4. Health check
echo ""
echo "[*] Running health checks..."

# Check API health
if curl -sf http://localhost:8001/health > /dev/null 2>&1; then
    echo "[✓] API is healthy on port 8001"
else
    echo "[!] API health check failed on port 8001 — checking logs..."
    docker compose logs --tail=20 eldf-api
fi

# Check nginx
if curl -sf http://localhost:80/health > /dev/null 2>&1; then
    echo "[✓] Nginx proxy is healthy on port 80"
else
    echo "[!] Nginx health check failed on port 80 — checking logs..."
    docker compose logs --tail=20 nginx
fi

echo ""
echo "============================================="
echo "  DEPLOYMENT COMPLETE"
echo ""
echo "  Frontend + API:  http://142.132.228.77"
echo "  Direct API:      http://142.132.228.77:8001"
echo "  Health check:    http://142.132.228.77/health"
echo "  API endpoints:   http://142.132.228.77/api/auth/login"
echo "                   http://142.132.228.77/api/auth/register"
echo "                   http://142.132.228.77/api/auth/me"
echo ""
echo "  Useful commands:"
echo "    docker compose logs -f          # Follow logs"
echo "    docker compose restart          # Restart services"
echo "    docker compose down             # Stop all"
echo "    docker compose up -d --build    # Rebuild & restart"
echo "============================================="
