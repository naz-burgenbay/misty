variable "subscription_id" {
  description = "Azure subscription ID"
  type        = string
}

variable "location" {
  description = "Azure region for all resources"
  type        = string
  default     = "polandcentral"
}

variable "environment" {
  description = "dev"
  type        = string
}

variable "app_service_sku" {
  description = "App Service plan SKU B1"
  type        = string
  default     = "B1"
}

variable "sql_admin_login" {
  description = "SQL Server administrator login name"
  type        = string
}

variable "sql_admin_password" {
  description = "SQL Server administrator login password"
  type        = string
  sensitive   = true
}

variable "sql_sku" {
  description = "SQL Database SKU"
  type        = string
  default     = "Basic"
}

variable "acr_sku" {
  description = "Container Registry SKU (Basic)"
  type        = string
  default     = "Basic"
}

variable "jwt_signing_key" {
  description = "JWT signing key"
  type        = string
  sensitive   = true
}

variable "jwt_issuer" {
  type    = string
  default = "Misty.Api"
}

variable "jwt_audience" {
  type    = string
  default = "Misty.Web"
}