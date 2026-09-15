terraform {
  required_version = ">= 1.2.7"
  required_providers {
    dynatrace = {
      version = "~> 1.0"
      source  = "dynatrace-oss/dynatrace"
    }
  }
}

provider "dynatrace" {
  dt_env_url   = var.dynatrace_env_url
  dt_api_token = var.dynatrace_api_token
}


# # Example 1: Create a Custom Log Metric
# resource "dynatrace_log_custom_metric" "cpu_log_metric" {
#   key     = "calc:log.process.cpu.utilization"
#   query   = "fetch logs | filter isNotNull(process.cpu.utilization_percent)"
#   active  = true

#   metric_extraction {
#     metric_extraction_type = "ATTRIBUTE"
#     value_attribute        = "process.cpu.utilization_percent"
#   }
# }

# Example 2: Create a Basic Dashboard
# resource "dynatrace_dashboard" "service_availability_dashboard" {
#   dashboard_metadata {
#     name   = "Service Health & CPU Overview"
#     # shared = true
#     owner  = "Terraform Automation"
#   }

#   tile {
#     name       = "API Availability Overview"
#     tile_type  = "DQL"
#     configured = true
#     query      = "fetch logs | filter isNotNull(service.name) | summarize total = count(), failed = countIf(http.status_code >= 500), by: { service.name }"

#     bounds {
#       top    = 0
#       left   = 0
#       width  = 304
#       height = 304
#     }
#   }

#   tile {
#     name       = "Requests by Service and Route"
#     tile_type  = "DQL"
#     configured = true
#     query      = "fetch logs | filter isNotNull(service.name) and isNotNull(http.route) | summarize requests = count(), by: { service.name, http.route } | fieldsRename page = service.name, action = http.route | sort requests desc"

#     bounds {
#       top    = 320
#       left   = 0
#       width  = 304
#       height = 304
#     }
#   }
# }

resource "dynatrace_json_dashboard" "service_availability_dashboard" {
  contents = jsonencode({
    dashboardMetadata = {
      name   = "Service Availability Dashboard"
      shared = true
    }
    tiles = [
      {
        name     = "Service Overview"
        tileType = "HEADER"
        bounds   = { top = 0, left = 0, width = 15, height = 1 }
      }
    ]
  })
}