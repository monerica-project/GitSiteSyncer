#!/usr/bin/env bash
# deploy.sh — generic GitSiteSyncer deployer.
# Lives in GitSiteSyncer/CI/ and is committed to source control.
# No secrets. No per-instance values.
#
# Takes a path to a per-instance config file as its first argument.
#
# Usage:
#   ./deploy.sh <path-to-instance-config.sh> [--skip-build]

set -euo pipefail

CONFIG_PATH=""
SKIP_BUILD=0
for arg in "$@"; do
    case "$arg" in
        --skip-build) SKIP_BUILD=1 ;;
        -h|--help)    sed -n '2,12p' "$0"; exit 0 ;;
        --*)          echo "Unknown flag: $arg" >&2; exit 1 ;;
        *)
            if [[ -z "$CONFIG_PATH" ]]; then
                CONFIG_PATH="$arg"
            else
                echo "Multiple config paths given: '$CONFIG_PATH' and '$arg'" >&2
                exit 1
            fi
            ;;
    esac
done

if [[ -z "$CONFIG_PATH" ]]; then
    echo "Usage: ./deploy.sh <path-to-instance-config.sh> [--skip-build]" >&2
    exit 1
fi

[[ -f "$CONFIG_PATH" ]] || { echo "ERROR: config not found: $CONFIG_PATH" >&2; exit 1; }
CONFIG_PATH="$(realpath "$CONFIG_PATH")"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATE_FILE="$SCRIPT_DIR/appsettings.template.json"
[[ -f "$TEMPLATE_FILE" ]] || { echo "ERROR: template not found: $TEMPLATE_FILE" >&2; exit 1; }

# Source the per-instance config.
# shellcheck disable=SC1090
. "$CONFIG_PATH"

# Required values from the instance config.
: "${VPS:?$CONFIG_PATH must set VPS}"
: "${APP_NAME:?$CONFIG_PATH must set APP_NAME}"
: "${PROJECT:?$CONFIG_PATH must set PROJECT}"
: "${DLL_NAME:?$CONFIG_PATH must set DLL_NAME}"
: "${DEPLOY_PATH:?$CONFIG_PATH must set DEPLOY_PATH}"
: "${WORK_DIR:?$CONFIG_PATH must set WORK_DIR}"
: "${SERVICE_USER:?$CONFIG_PATH must set SERVICE_USER}"
: "${INTERVAL:?$CONFIG_PATH must set INTERVAL (e.g. 10min)}"

# Required appsettings values
: "${GIT_DIRECTORY:?$CONFIG_PATH must set GIT_DIRECTORY}"
: "${LOCK_FILE_DIRECTORY:?$CONFIG_PATH must set LOCK_FILE_DIRECTORY}"
: "${GIT_EMAIL:?$CONFIG_PATH must set GIT_EMAIL}"
: "${GIT_USERNAME:?$CONFIG_PATH must set GIT_USERNAME}"
: "${GIT_PASSWORD:?$CONFIG_PATH must set GIT_PASSWORD (GitHub PAT)}"
: "${SITEMAP_URL:?$CONFIG_PATH must set SITEMAP_URL}"
: "${MINUTES_TO_CONSIDER:?$CONFIG_PATH must set MINUTES_TO_CONSIDER}"
: "${APP_HOST_DOMAIN:?$CONFIG_PATH must set APP_HOST_DOMAIN}"
: "${NO_APP_HOST_DOMAIN:?$CONFIG_PATH must set NO_APP_HOST_DOMAIN}"

# Resolve project path against the config files directory.
CONFIG_DIR="$(dirname "$CONFIG_PATH")"
[[ "$PROJECT" == /* ]] || PROJECT="$(realpath -m "$CONFIG_DIR/$PROJECT")"
[[ -f "$PROJECT" ]] || { echo "ERROR: project not found: $PROJECT" >&2; exit 1; }

C_CYAN=$'\033[36m'; C_GREEN=$'\033[32m'; C_RED=$'\033[31m'; C_YELLOW=$'\033[33m'; C_RESET=$'\033[0m'
step() { echo; echo "${C_CYAN}==> [$APP_NAME] $*${C_RESET}"; }
ok()   { echo "${C_GREEN}    OK: $*${C_RESET}"; }
warn() { echo "${C_YELLOW}    WARN: $*${C_RESET}"; }
errx() { echo "${C_RED}ERROR: $*${C_RESET}" >&2; exit 1; }

command -v envsubst >/dev/null 2>&1 || errx "envsubst not installed. Run: sudo apt-get install gettext-base"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ---- Pre-flight: ensure service user and working dir exist on the VPS -----
step "Ensuring service user and working directory"
ssh "$VPS" bash <<EOF
set -e
if ! id -u $SERVICE_USER >/dev/null 2>&1; then
    sudo useradd --system --home-dir $WORK_DIR --shell /usr/sbin/nologin $SERVICE_USER
fi
sudo mkdir -p $WORK_DIR
sudo chown -R $SERVICE_USER:$SERVICE_USER $WORK_DIR
sudo chmod 750 $WORK_DIR
sudo mkdir -p $DEPLOY_PATH
EOF
ok "User $SERVICE_USER and $WORK_DIR ready"

# ---- Build -----------------------------------------------------------------
PUBLISH_OUT="$SCRIPT_DIR/../publish/$APP_NAME"
PUBLISH_OUT="$(realpath -m "$PUBLISH_OUT")"

if (( ! SKIP_BUILD )); then
    step "Publishing $APP_NAME (linux-x64)"
    rm -rf "$PUBLISH_OUT"
    PROJECT_DIR="$(dirname "$PROJECT")"
    find "$PROJECT_DIR/.." -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
    dotnet publish "$PROJECT" \
        -c Release -r linux-x64 --self-contained false \
        -o "$PUBLISH_OUT" --nologo -v minimal
    ok "Published to $PUBLISH_OUT"
fi

# ---- Build appsettings.json from template ---------------------------------
step "Building appsettings.json from template"

export GIT_DIRECTORY LOCK_FILE_DIRECTORY GIT_EMAIL GIT_USERNAME GIT_PASSWORD
export SITEMAP_URL MINUTES_TO_CONSIDER APP_HOST_DOMAIN NO_APP_HOST_DOMAIN

envsubst < "$TEMPLATE_FILE" > "$PUBLISH_OUT/appsettings.json"
ok "Wrote $PUBLISH_OUT/appsettings.json"

# ---- Sync binaries ---------------------------------------------------------
step "Syncing to $VPS:$DEPLOY_PATH"
rsync -rlptDz --delete --rsync-path="sudo rsync" \
    "$PUBLISH_OUT/" "$VPS:$DEPLOY_PATH/"
ssh "$VPS" "sudo chown -R root:root $DEPLOY_PATH && sudo chmod -R 755 $DEPLOY_PATH"
# But the appsettings.json contains the PAT — lock it down
ssh "$VPS" "sudo chown root:$SERVICE_USER $DEPLOY_PATH/appsettings.json && sudo chmod 640 $DEPLOY_PATH/appsettings.json"
ok "Binaries synced"

# ---- Install git on VPS if missing ----------------------------------------
ssh "$VPS" "command -v git >/dev/null 2>&1 || sudo apt-get install -y git"

# ---- systemd unit + timer --------------------------------------------------
step "Installing systemd service and timer"

SVC_FILE="$TMP/$APP_NAME.service"
cat > "$SVC_FILE" <<EOF
[Unit]
Description=$APP_NAME (one-shot run)
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
WorkingDirectory=$WORK_DIR
ExecStart=/usr/bin/dotnet $DEPLOY_PATH/$DLL_NAME
User=$SERVICE_USER
Group=$SERVICE_USER
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=DOTNET_CLI_HOME=$WORK_DIR
Environment=HOME=$WORK_DIR
StandardOutput=journal
StandardError=journal
TimeoutStartSec=20min
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true
ReadWritePaths=$WORK_DIR
EOF

TIMER_FILE="$TMP/$APP_NAME.timer"
cat > "$TIMER_FILE" <<EOF
[Unit]
Description=Run $APP_NAME every $INTERVAL after the last run finished

[Timer]
OnBootSec=2min
OnCalendar=*:0/10
Unit=$APP_NAME.service
AccuracySec=30s
Persistent=true

[Install]
WantedBy=timers.target
EOF

scp "$SVC_FILE" "$VPS:/tmp/$APP_NAME.service"
scp "$TIMER_FILE" "$VPS:/tmp/$APP_NAME.timer"
ssh "$VPS" "sudo mv /tmp/$APP_NAME.service /etc/systemd/system/"
ssh "$VPS" "sudo mv /tmp/$APP_NAME.timer /etc/systemd/system/"
ssh "$VPS" "sudo systemctl daemon-reload"
# Enable timer (and stop any old long-running service variant if it exists)
ssh "$VPS" "sudo systemctl disable --now $APP_NAME.service 2>/dev/null || true"
ssh "$VPS" "sudo systemctl enable --now $APP_NAME.timer"
ok "Timer installed and enabled"

# ---- Trigger an immediate run to verify ------------------------------------
step "Triggering an immediate run"
ssh "$VPS" "sudo systemctl start $APP_NAME.service" || true
sleep 3
ssh "$VPS" "sudo systemctl status $APP_NAME.service --no-pager | head -20" || true

echo
echo "${C_GREEN}===============================${C_RESET}"
echo "${C_GREEN} [$APP_NAME] Deploy complete${C_RESET}"
echo "${C_GREEN}===============================${C_RESET}"
echo " Timer:   ssh $VPS sudo systemctl list-timers $APP_NAME.timer"
echo " Status:  ssh $VPS sudo systemctl status $APP_NAME.service"
echo " Logs:    ssh $VPS sudo journalctl -u $APP_NAME.service -f"
echo " Run now: ssh $VPS sudo systemctl start $APP_NAME.service"
echo
