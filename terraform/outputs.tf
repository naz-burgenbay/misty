output "app_service_url" {
  description = "Public URL of the deployed API."
  value       = "https://${azurerm_linux_web_app.api.default_hostname}"
}

output "app_service_name" {
  description = "App Service resource name (used by the CI/CD deploy step)"
  value       = azurerm_linux_web_app.api.name
}

output "acr_login_server" {
  description = "ACR login server hostname (used by CI/CD to push images)"
  value       = azurerm_container_registry.main.login_server
}

output "resource_group_name" {
  description = "Resource group that contains all application resources"
  value       = azurerm_resource_group.main.name
}

output "key_vault_name" {
  description = "Key Vault name"
  value       = azurerm_key_vault.main.name
}

output "servicebus_namespace" {
  description = "Service Bus namespace name"
  value       = azurerm_servicebus_namespace.main.name
}

output "frontend_url" {
  description = "Public URL of the frontend (Azure Storage static website)"
  value       = azurerm_storage_account.main.primary_web_endpoint
}

output "storage_account_name" {
  description = "Storage account name (used by CI/CD to upload frontend files)"
  value       = azurerm_storage_account.main.name
}

output "application_insights_instrumentation_key" {
  description = "Application Insights instrumentation key"
  value       = azurerm_application_insights.main.instrumentation_key
  sensitive   = true
}
