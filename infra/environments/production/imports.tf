locals {
  azure_resource_group_id = "/subscriptions/${var.azure_subscription_id}/resourceGroups/${var.azure_resource_group_name}"
  azure_web_base_id       = "${local.azure_resource_group_id}/providers/Microsoft.Web"
  azure_monitor_base_id   = "${local.azure_resource_group_id}/providers/Microsoft.Insights"
  log_analytics_base_id   = "${local.azure_resource_group_id}/providers/Microsoft.OperationalInsights"
  azure_sql_base_id       = "${local.azure_resource_group_id}/providers/Microsoft.Sql"
  azure_storage_base_id   = "${local.azure_resource_group_id}/providers/Microsoft.Storage"
  mobile_build_storage_id = "${local.azure_storage_base_id}/storageAccounts/queenzonemobilebuilds"
  target_sql_database_id  = "${local.azure_sql_base_id}/servers/queenzone-prod-sql/databases/queenzone-db"
  azure_data_containers = toset([
    "album-or-single-covers", "attachments", "avatars", "brian-may",
    "css", "databasebackup", "fan-art", "fan-pics", "forum",
    "freddie-mercury", "freddie-tribute-concert", "images", "john-deacon",
    "miscellaneous", "mp3", "pre-queen", "queen",
    "queen-and-adam-lambert", "queen-and-paul-rodgers",
    "queen-memorabillia", "roger-taylor", "songfiles", "special-events",
    "test", "ugc-articles", "ugc-avatars", "ugc-forum", "ugc-photos",
    "us-convention-2001",
  ])
}

import {
  to = module.azure_mobile_builds.azapi_resource.storage_account
  id = local.mobile_build_storage_id
}

import {
  to = module.azure_mobile_builds.azapi_resource.blob_service
  id = "${local.mobile_build_storage_id}/blobServices/default"
}

import {
  to = module.azure_mobile_builds.azapi_resource.builds_container
  id = "${local.mobile_build_storage_id}/blobServices/default/containers/builds"
}

import {
  to = module.azure_mobile_builds.azurerm_role_assignment.mobile_publisher
  id = "${local.mobile_build_storage_id}/providers/Microsoft.Authorization/roleAssignments/9b23875e-46d9-4393-9ee7-1fec8a74fd7c"
}

import {
  to = azurerm_resource_group.production
  id = local.azure_resource_group_id
}

import {
  to = module.azure_data.azapi_resource.sql_server[0]
  id = "${local.azure_sql_base_id}/servers/queenzone-sql-server"
}

import {
  to = module.azure_data.azurerm_mssql_firewall_rule.azure_services[0]
  id = "${local.azure_sql_base_id}/servers/queenzone-sql-server/firewallRules/AllowAllWindowsAzureIps"
}

import {
  to = module.azure_data.azurerm_mssql_server_extended_auditing_policy.production[0]
  id = "${local.azure_sql_base_id}/servers/queenzone-sql-server/extendedAuditingSettings/Default"
}

import {
  to = module.azure_data_target.azurerm_mssql_database.production[0]
  id = local.target_sql_database_id
}

import {
  to = module.azure_data_target.azurerm_mssql_database_extended_auditing_policy.production[0]
  id = "${local.target_sql_database_id}/extendedAuditingSettings/Default"
}

import {
  to = module.azure_data.azapi_resource.storage_account
  id = "${local.azure_storage_base_id}/storageAccounts/queenzone"
}

import {
  to = module.azure_data.azapi_resource.blob_service[0]
  id = "${local.azure_storage_base_id}/storageAccounts/queenzone/blobServices/default"
}

import {
  for_each = local.azure_data_containers
  to       = module.azure_data.azapi_resource.container[each.value]
  id       = "${local.azure_storage_base_id}/storageAccounts/queenzone/blobServices/default/containers/${each.value}"
}

# --- Cloudflare edge (#626) ---

import {
  to = module.cloudflare_edge.cloudflare_zone.queenzone
  id = var.cloudflare_zone_id
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.ssl
  id = "${var.cloudflare_zone_id}/ssl"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.tls_1_3
  id = "${var.cloudflare_zone_id}/tls_1_3"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.always_use_https
  id = "${var.cloudflare_zone_id}/always_use_https"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.automatic_https_rewrites
  id = "${var.cloudflare_zone_id}/automatic_https_rewrites"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.security_level
  id = "${var.cloudflare_zone_id}/security_level"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.cache_level
  id = "${var.cloudflare_zone_id}/cache_level"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.browser_check
  id = "${var.cloudflare_zone_id}/browser_check"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.challenge_ttl
  id = "${var.cloudflare_zone_id}/challenge_ttl"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.brotli
  id = "${var.cloudflare_zone_id}/brotli"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.http2
  id = "${var.cloudflare_zone_id}/http2"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.http3
  id = "${var.cloudflare_zone_id}/http3"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.websockets
  id = "${var.cloudflare_zone_id}/websockets"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.polish
  id = "${var.cloudflare_zone_id}/polish"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.hotlink_protection
  id = "${var.cloudflare_zone_id}/hotlink_protection"
}

import {
  to = module.cloudflare_edge.cloudflare_zone_setting.development_mode
  id = "${var.cloudflare_zone_id}/development_mode"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.apex
  id = "${var.cloudflare_zone_id}/c22f8759158c7a3f06e290e5f51f5da8"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.www
  id = "${var.cloudflare_zone_id}/fdd05b163df7c2941ae1e36986558228"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.dev
  id = "${var.cloudflare_zone_id}/299da79a473f1c6bc03dc8a2f735269a"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.cdn
  id = "${var.cloudflare_zone_id}/c8989e49d2d624756ddf35a05e3ac153"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.cdn2
  id = "${var.cloudflare_zone_id}/1af26073508a0494bc5d66f5b23df57f"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.pictures_legacy
  id = "${var.cloudflare_zone_id}/4bc9968da3b9ec19d792fef11ef4d77a"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.asverify_cdn
  id = "${var.cloudflare_zone_id}/9b996207c2701bd567962763ed653142"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.asuid_apex
  id = "${var.cloudflare_zone_id}/afd377459a21a49b39458293e929221a"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.asuid_www
  id = "${var.cloudflare_zone_id}/b3d6ee3412118593835755b3a424f0c8"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.google_site_verification
  id = "${var.cloudflare_zone_id}/4d6c859c709bc0f9382ba74f93465b89"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.bing_verification_1
  id = "${var.cloudflare_zone_id}/30eaa5bf1d4ad3834d5c7db37174c83b"
}

import {
  to = module.cloudflare_edge.cloudflare_dns_record.bing_verification_2
  id = "${var.cloudflare_zone_id}/fd000f4e7e714d719eb3ae0618e9ff4c"
}

import {
  to = module.cloudflare_edge.cloudflare_workers_script.pictures_queenzone_org
  id = "${var.cloudflare_account_id}/pictures-queenzone-org"
}

import {
  to = module.cloudflare_edge.cloudflare_workers_script.pictures_legacy_redirect
  id = "${var.cloudflare_account_id}/pictures-legacy-redirect"
}

import {
  to = module.cloudflare_edge.cloudflare_workers_route.cdn2_media
  id = "${var.cloudflare_zone_id}/0fd4ebdc9c3e4825a1d9e6527fbd4d24"
}

import {
  to = module.cloudflare_edge.cloudflare_workers_route.pictures_legacy_redirect
  id = "${var.cloudflare_zone_id}/276e102a9ef8402c9b610c7bc60bbedb"
}
