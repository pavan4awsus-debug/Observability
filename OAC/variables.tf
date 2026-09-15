variable "dynatrace_env_url" {
  type        = string
  description = "Your Dynatrace environment URL (e.g., https://abc12345.live.dynatrace.com or https://your-domain.dynatrace-managed.com/e/env-id)"
}

variable "dynatrace_api_token" {
  type        = string
  description = "Dynatrace API v2 token with configuration permissions"
  sensitive   = true
}