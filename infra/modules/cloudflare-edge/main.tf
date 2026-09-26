resource "cloudflare_zone" "queenzone" {
  account = {
    id = var.account_id
  }
  name   = var.zone_name
  type   = "full"
  paused = false

  lifecycle {
    prevent_destroy = true
  }
}

# TLS/security/cache zone settings confirmed live during the #624 audit
# (infra/import/cloudflare-hostnames.json). min_tls_version is deliberately
# left unmanaged: it is a dashboard default (1.0), not a reviewed decision.
resource "cloudflare_zone_setting" "ssl" {
  zone_id    = var.zone_id
  setting_id = "ssl"
  value      = "strict"
}

resource "cloudflare_zone_setting" "tls_1_3" {
  zone_id    = var.zone_id
  setting_id = "tls_1_3"
  value      = "on"
}

resource "cloudflare_zone_setting" "always_use_https" {
  zone_id    = var.zone_id
  setting_id = "always_use_https"
  value      = "on"
}

resource "cloudflare_zone_setting" "automatic_https_rewrites" {
  zone_id    = var.zone_id
  setting_id = "automatic_https_rewrites"
  value      = "on"
}

resource "cloudflare_zone_setting" "security_level" {
  zone_id    = var.zone_id
  setting_id = "security_level"
  value      = "medium"
}

resource "cloudflare_zone_setting" "cache_level" {
  zone_id    = var.zone_id
  setting_id = "cache_level"
  value      = "aggressive"
}

resource "cloudflare_zone_setting" "browser_check" {
  zone_id    = var.zone_id
  setting_id = "browser_check"
  value      = "on"
}

resource "cloudflare_zone_setting" "challenge_ttl" {
  zone_id    = var.zone_id
  setting_id = "challenge_ttl"
  value      = 1800
}

resource "cloudflare_zone_setting" "brotli" {
  zone_id    = var.zone_id
  setting_id = "brotli"
  value      = "on"
}

resource "cloudflare_zone_setting" "http2" {
  zone_id    = var.zone_id
  setting_id = "http2"
  value      = "on"
}

resource "cloudflare_zone_setting" "http3" {
  zone_id    = var.zone_id
  setting_id = "http3"
  value      = "on"
}

resource "cloudflare_zone_setting" "websockets" {
  zone_id    = var.zone_id
  setting_id = "websockets"
  value      = "on"
}

resource "cloudflare_zone_setting" "polish" {
  zone_id    = var.zone_id
  setting_id = "polish"
  value      = "off"
}

resource "cloudflare_zone_setting" "hotlink_protection" {
  zone_id    = var.zone_id
  setting_id = "hotlink_protection"
  value      = "off"
}

resource "cloudflare_zone_setting" "development_mode" {
  zone_id    = var.zone_id
  setting_id = "development_mode"
  value      = "off"
}

# Keep high-volume archive crawlers at the edge. These user agents were the
# sustained source of expensive forum-author/topic reads in Application Insights
# on 6-9 September 2026. Search-engine crawlers such as Googlebot and bingbot are
# deliberately unaffected.
resource "cloudflare_ruleset" "bot_blocking" {
  zone_id     = var.zone_id
  name        = "QueenZone custom firewall rules"
  description = "Block crawlers proven to overload the forum archive read path"
  kind        = "zone"
  phase       = "http_request_firewall_custom"

  rules = [
    {
      ref         = "skip_azure_availability_health"
      description = "Skip Browser Integrity Check, security level, and managed WAF on GET /health for Azure availability probes"
      expression  = <<-EOT
        (http.host in {"queenzone.org" "www.queenzone.org"}) and
        (http.request.method eq "GET") and
        (http.request.uri.path eq "/health")
      EOT
      action      = "skip"
      enabled     = true
      action_parameters = {
        products = ["bic", "securityLevel", "waf"]
      }
    },
    {
      ref         = "block_expensive_archive_crawlers"
      description = "Block crawlers proven to overload the forum archive"
      expression  = <<-EOT
        (http.host in {"queenzone.org" "www.queenzone.org"}) and (
          http.user_agent contains "ClaudeBot" or
          http.user_agent contains "Claude-SearchBot" or
          http.user_agent contains "AionBot" or
          http.user_agent contains "Amazonbot" or
          http.user_agent contains "SemrushBot" or
          http.user_agent contains "MJ12bot" or
          http.user_agent contains "serpstatbot"
        )
      EOT
      action      = "block"
      enabled     = true
    },
  ]
}

# Archive-author pages perform database-backed lookups for each distinct legacy
# author ID. Limit bursts per source IP while leaving ordinary browsing and
# search-engine discovery unaffected. The exact Serpstat crawler currently
# causing sustained traffic is blocked above; this rule is the safety net for
# other clients that sweep unique author IDs too quickly for edge caching to help.
resource "cloudflare_ruleset" "archive_author_rate_limit" {
  zone_id     = var.zone_id
  name        = "QueenZone rate limiting rules"
  description = "Rate limit bursts across expensive archive-author pages"
  kind        = "zone"
  phase       = "http_ratelimit"

  rules = [{
    ref         = "rate_limit_archive_author_requests"
    description = "Block archive-author clients exceeding 10 requests in 10 seconds"
    expression  = <<-EOT
      (http.host in {"queenzone.org" "www.queenzone.org"}) and
      starts_with(http.request.uri.path, "/forum/archive-authors/")
    EOT
    action      = "block"
    enabled     = true

    ratelimit = {
      characteristics     = ["cf.colo.id", "ip.src"]
      period              = 10
      requests_per_period = 10
      mitigation_timeout  = 10
    }
  }]
}

# Archive-author pages are public and identical for cookie-free visitors. Cache
# them at Cloudflare for an hour so repeat legitimate crawler/browser requests do
# not reach App Service or Azure SQL. Requests carrying any cookie bypass this
# rule and retain the application's normal authentication-aware caching path.
resource "cloudflare_ruleset" "archive_author_cache" {
  zone_id     = var.zone_id
  name        = "QueenZone cache rules"
  description = "Cache expensive anonymous archive-author pages at the edge"
  kind        = "zone"
  phase       = "http_request_cache_settings"

  rules = [{
    ref         = "cache_anonymous_archive_authors"
    description = "Cache cookie-free archive-author GET requests for one hour"
    expression  = <<-EOT
      (http.host in {"queenzone.org" "www.queenzone.org"}) and
      (http.request.method eq "GET") and
      starts_with(http.request.uri.path, "/forum/archive-authors/") and
      (http.cookie eq "")
    EOT
    action      = "set_cache_settings"
    enabled     = true

    action_parameters = {
      cache = true
      edge_ttl = {
        mode    = "override_origin"
        default = 3600
      }
      browser_ttl = {
        mode = "respect_origin"
      }
    }
  }]
}

# --- DNS records (infra/import/cloudflare-hostnames.json, #624 audit) ---

# comment/comment_modified_on are ignored on every record below: adding a
# comment is a real, safe in-place update (confirmed by a live #626 plan
# against CLOUDFLARE_API_TOKEN_READONLY), but this module's job is a no-op
# import, not editing dashboard-authored notes.
resource "cloudflare_dns_record" "apex" {
  zone_id = var.zone_id
  name    = "queenzone.org"
  type    = "A"
  content = "40.69.106.99"
  ttl     = 1
  proxied = true
  comment = "Apex -> App Service inbound IP"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "www" {
  zone_id = var.zone_id
  name    = "www.queenzone.org"
  type    = "CNAME"
  content = "queenzone-prod.azurewebsites.net"
  ttl     = 1
  proxied = true

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "dev" {
  zone_id = var.zone_id
  name    = "dev.queenzone.org"
  type    = "CNAME"
  content = "queenzone-devbox.azurewebsites.net"
  ttl     = 1
  proxied = false
  comment = "Azure Static Web App mobile test-build downloads (#809)"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "cdn" {
  zone_id = var.zone_id
  name    = "cdn.queenzone.org"
  type    = "CNAME"
  content = "queenzoneprod.blob.core.windows.net"
  ttl     = 1
  proxied = true
  comment = "Straight proxy to Azure Blob; no Worker route"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "cdn2" {
  zone_id = var.zone_id
  name    = "cdn2.queenzone.org"
  type    = "CNAME"
  content = "queenzoneprod.blob.core.windows.net"
  ttl     = 1
  proxied = true
  comment = "Worker route cdn2.queenzone.org/* fronts this record"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "pictures_legacy" {
  zone_id = var.zone_id
  name    = "pictures.queenzone.org"
  type    = "CNAME"
  content = "cdn.queenzone.org"
  ttl     = 1
  proxied = true
  comment = "Retired media hostname compatibility"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "asverify_cdn" {
  zone_id = var.zone_id
  name    = "asverify.cdn.queenzone.org"
  type    = "CNAME"
  content = "asverify.queenzoneprod.blob.core.windows.net"
  ttl     = 1
  proxied = false
  comment = "Azure Storage custom-domain verification"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

# Verification TXT records. Content values were confirmed live during the
# #626 plan (public DNS is not secret) and match byte-for-byte, quotes
# included, as returned by the Cloudflare API.
resource "cloudflare_dns_record" "asuid_apex" {
  zone_id = var.zone_id
  name    = "asuid.queenzone.org"
  type    = "TXT"
  content = "\"BF3C6D1B1E965ADCA69EAFCA5EE652A43A44E9441CE4E3A82C4B47646F3A9AE2\""
  ttl     = 1
  comment = "App Service custom domain verification"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "asuid_www" {
  zone_id = var.zone_id
  name    = "asuid.www.queenzone.org"
  type    = "TXT"
  content = "\"BF3C6D1B1E965ADCA69EAFCA5EE652A43A44E9441CE4E3A82C4B47646F3A9AE2\""
  ttl     = 1
  comment = "App Service custom domain verification"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "google_site_verification" {
  zone_id = var.zone_id
  name    = "queenzone.org"
  type    = "TXT"
  content = "\"google-site-verification=gDSLwU-DDTXw-Z3VYtqy0xZrLPcqqfdX4VZkA-9Ttbg\""
  ttl     = 3600
  comment = "Google site verification"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "bing_verification_1" {
  zone_id = var.zone_id
  name    = "809286c091b4aeaedf6cd9c90b65a333.queenzone.org"
  type    = "CNAME"
  content = "verify.bing.com"
  ttl     = 1
  proxied = false
  comment = "Bing verification"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

resource "cloudflare_dns_record" "bing_verification_2" {
  zone_id = var.zone_id
  name    = "a0151149b4c5e0d0ac33d250b9dd6db4.queenzone.org"
  type    = "CNAME"
  content = "verify.bing.com"
  ttl     = 1
  proxied = false
  comment = "Bing verification"

  lifecycle {
    prevent_destroy = true
    ignore_changes  = [comment]
  }
}

# --- Workers ---

resource "cloudflare_workers_script" "pictures_queenzone_org" {
  account_id  = var.account_id
  script_name = var.worker_name
  # The checked-in snapshot already carries a leading UTF-8 BOM byte,
  # matching the live script (confirmed by a live #626 plan).
  content            = file("${path.module}/../../import/workers/pictures-queenzone-org.js")
  main_module        = "worker.js"
  compatibility_date = "2026-06-25"

  lifecycle {
    prevent_destroy = true

    # Remaining runtime knobs (flags, usage model, bindings) were not
    # captured during the #624 audit and showed no drift in that same plan.
    # Ignoring them keeps this module from guessing at values that could
    # change published Worker behaviour.
    ignore_changes = [
      compatibility_flags,
      usage_model,
      bindings,
      observability,
      migrations,
      assets,
      body_part,
    ]
  }
}

resource "cloudflare_workers_script" "pictures_legacy_redirect" {
  account_id         = var.account_id
  script_name        = var.legacy_redirect_worker_name
  content            = file("${path.module}/../../import/workers/pictures-legacy-redirect.js")
  compatibility_date = "2026-08-16"

  lifecycle {
    prevent_destroy = true

    ignore_changes = [
      compatibility_flags,
      usage_model,
      bindings,
      observability,
      migrations,
      assets,
      main_module,
      body_part,
    ]
  }
}

resource "cloudflare_workers_route" "cdn2_media" {
  zone_id = var.zone_id
  pattern = var.worker_route
  script  = cloudflare_workers_script.pictures_queenzone_org.script_name

  lifecycle {
    prevent_destroy = true
  }
}

resource "cloudflare_workers_route" "pictures_legacy_redirect" {
  zone_id = var.zone_id
  pattern = var.legacy_redirect_route
  script  = cloudflare_workers_script.pictures_legacy_redirect.script_name

  lifecycle {
    prevent_destroy = true
  }
}
