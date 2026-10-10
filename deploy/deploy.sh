#!/usr/bin/env bash
# Deploys all of MovieBot to hotbox, the site and the four services. Run it from hotrod, with no
# arguments:
#
#   deploy/deploy.sh
#
# Either half deploys on its own: deploy/deploy-site.sh, which restarts nothing, and
# deploy/deploy-services.sh.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

"$root/deploy/deploy-services.sh"
"$root/deploy/deploy-site.sh"
