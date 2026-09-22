#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)

fail()
{
    printf '%s\n' "Legacy brand compatibility test failed: $1" >&2
    exit 1
}

require_literal()
{
    file=$1
    literal=$2
    description=$3

    [ -f "$root_dir/$file" ] ||
        fail "$description source is missing: $file"

    grep -Fq -- "$literal" "$root_dir/$file" ||
        fail "$description changed without an explicit compatibility migration."
}

# Persisted MAUI session keys. Renaming these would sign users out and can
# strand refresh state on installed clients.
require_literal "Services/AuthSession.cs" '"billwatch_access_token"'     "MAUI access-token secure-storage key"
require_literal "Services/AuthSession.cs" '"billwatch_refresh_token"'     "MAUI refresh-token secure-storage key"

# Claims, cookies, browser preferences, and Data Protection identifiers are
# persisted across sessions/releases. They require explicit dual-read/migration.
require_literal "FullWorth.API/Data/Entities/ApplicationUser.cs" '"billwatch:display_name"'     "display-name claim type"
require_literal "FullWorth.Web/Program.cs" '"__Host-BillWatch.Web.Auth"'     "Web authentication cookie name"
require_literal "FullWorth.Web/wwwroot/js/theme.js" '"billwatch-theme"'     "browser theme preference key"
require_literal "FullWorth.Web/wwwroot/js/theme-bootstrap.js" '"billwatch-theme"'     "bootstrap theme preference key"
require_literal "FullWorth.Web/wwwroot/js/local-time.js" '"billwatch.timestamp-display-mode"'     "timestamp display preference key"
require_literal "FullWorth.API/Program.cs" '"BillWatchDatabase"'     "production connection-string key"
require_literal "FullWorth.API/Program.cs" '"BillWatch");'     "ASP.NET Core Data Protection application name"
require_literal "FullWorth.API/Services/Plaid/PlaidTokenProtector.cs" '"BillWatch.Plaid.AccessToken.v1"'     "Plaid access-token Data Protection purpose"
require_literal "FullWorth.API/Services/Plaid/PlaidTokenProtector.cs" '"BillWatch.Plaid.LinkToken.v1"'     "Plaid link-token Data Protection purpose"
require_literal "FullWorth.Web/Infrastructure/WebHostingExtensions.cs" '"BillWatch.Web"'     "Web Data Protection application name"
require_literal "FullWorth.Web/Infrastructure/ExternalAuthenticationEndpointMappings.cs" '"billwatch:external-id-token"'     "external-auth ID-token property"
require_literal "FullWorth.Web/Infrastructure/ExternalAuthenticationEndpointMappings.cs" '"billwatch:external-provider"'     "external-auth provider property"
require_literal "FullWorth.Web/Infrastructure/ExternalAuthenticationEndpointMappings.cs" '"__Host-BillWatch.Web.External"'     "external-auth cookie name"
require_literal "FullWorth.Web/Infrastructure/ExternalAuthenticationEndpointMappings.cs" '__Host-BillWatch.Web.'     "external-auth correlation cookie prefix"
require_literal "FullWorth.API/Services/Subscriptions/StripeBillingService.cs" '"billwatch_user_id"'     "Stripe user metadata key"

# External operations identifiers remain stable until their consumers/storage
# have a separately verified migration.
require_literal "FullWorth.API/Authorization/SubscriptionAuthorizationTelemetry.cs" '"billwatch.subscription.denials"'     "subscription-denial metric name"
require_literal "deploy/send-operations-alert.sh" 'billwatch-production'     "operations alert source identifier"
require_literal "deploy/backup/backup.sh" "'BillWatch encrypted production backup'"     "encrypted backup manifest tag"
require_literal "FullWorth.API/Services/Identity/IdentityEmailOptions.cs" '"security@billbeacon.net"'     "legacy verified identity sender"
require_literal "FullWorth.API/appsettings.json" '"security@billbeacon.net"'     "configured legacy identity sender default"

# Production environment/path identities and legacy private-corpus ignores stay
# valid until their own guarded migrations are complete.
require_literal ".env.production.example" 'BILLWATCH_HOST='     "production API host environment variable"
require_literal ".env.production.example" 'BILLWATCH_WEB_HOST='     "production Web host environment variable"
require_literal "deploy/systemd/billwatch-runtime-readiness.service" '/opt/billwatch'     "production deployment path"
require_literal ".env.production.example" 'BILLWATCH_LEGACY_HOST='     "legacy API alias environment variable"
require_literal ".env.production.example" 'BILLWATCH_LEGACY_WEB_HOST='     "legacy Web alias environment variable"
require_literal "deploy/run-plaid-observation-proof.sh" 'I completed the BillWatch Plaid update flow in Plaid Hosted Link'     "Plaid observation confirmation phrase"
require_literal "deploy/run-alert-observation-proof.sh" 'I observed both BillWatch alert proof messages'     "alert observation confirmation phrase"
require_literal ".gitignore" '.private/BillWatch.AiShadowCorpus/'     "legacy private corpus ignore"
require_literal "BILLWATCH_CONTEXT.md" 'FULLWORTH_CONTEXT.md'     "legacy continuation pointer"

printf '%s\n' 'Legacy BillWatch compatibility identifiers remain intentionally guarded.'
