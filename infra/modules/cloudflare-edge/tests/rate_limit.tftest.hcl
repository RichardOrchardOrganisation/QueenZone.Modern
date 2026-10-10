mock_provider "cloudflare" {}

variables {
  account_id                  = "00000000000000000000000000000000"
  zone_id                     = "00000000000000000000000000000000"
  zone_name                   = "queenzone.org"
  worker_name                 = "pictures-queenzone-org"
  worker_route                = "cdn2.queenzone.org/*"
  legacy_redirect_worker_name = "pictures-legacy-redirect"
  legacy_redirect_route       = "pictures.queenzone.org/*"
}

run "forum_rate_limit_fits_free_plan" {
  command = plan

  assert {
    condition     = length(cloudflare_ruleset.archive_author_rate_limit.rules) == 1
    error_message = "The Free plan allows exactly one rate-limiting rule."
  }

  assert {
    condition = alltrue([
      for r in cloudflare_ruleset.archive_author_rate_limit.rules :
      r.ratelimit.period == 10 && r.ratelimit.mitigation_timeout == 10 &&
      r.ratelimit.characteristics == tolist(["cf.colo.id", "ip.src"])
    ])
    error_message = "Free plan rate limits must use a 10s period, 10s timeout and IP counting."
  }

  assert {
    condition = alltrue([
      for r in cloudflare_ruleset.archive_author_rate_limit.rules :
      strcontains(r.expression, "starts_with(http.request.uri.path, \"/forum/\")") &&
      strcontains(r.expression, "not starts_with(http.request.uri.path, \"/forum/attachment/\")") &&
      !strcontains(r.expression, "http.request.method") &&
      !strcontains(r.expression, "http.cookie") &&
      r.ratelimit.requests_per_period == 20
    ])
    error_message = "The forum rule must cover /forum/ (archive authors included), skip attachments, count every method, and use only Free-plan fields."
  }
}

run "archive_author_cache_includes_head" {
  command = plan

  assert {
    condition = alltrue([
      for r in cloudflare_ruleset.archive_author_cache.rules :
      strcontains(r.expression, "(http.request.method in {\"GET\" \"HEAD\"})") &&
      strcontains(r.expression, "starts_with(http.request.uri.path, \"/forum/archive-authors/\")") &&
      strcontains(r.expression, "(http.cookie eq \"\")")
    ])
    error_message = "Archive-author cache rule must cover cookie-free GET and HEAD only on /forum/archive-authors/."
  }
}

run "shapbot_is_blocked" {
  command = plan

  assert {
    condition = anytrue([
      for r in cloudflare_ruleset.bot_blocking.rules :
      r.ref == "block_expensive_archive_crawlers" && strcontains(r.expression, "http.user_agent contains \"ShapBot\"")
    ])
    error_message = "ShapBot must be in the archive crawler block list."
  }
}
