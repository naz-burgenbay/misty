resource "azurerm_service_plan" "main" {
  name                = "asp-${local.prefix}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  os_type             = "Linux"
  sku_name            = var.app_service_sku
  tags                = local.tags
}

resource "azurerm_linux_web_app" "api" {
  name                    = "app-${local.prefix}"
  resource_group_name     = azurerm_resource_group.main.name
  location                = azurerm_resource_group.main.location
  service_plan_id         = azurerm_service_plan.main.id
  client_affinity_enabled = false

  identity {
    type = "SystemAssigned"
  }

  site_config {
    always_on = true

    container_registry_use_managed_identity = true

    application_stack {
      docker_image_name   = "${azurerm_container_registry.main.login_server}/misty-api:latest"
      docker_registry_url = "https://${azurerm_container_registry.main.login_server}"
    }
  }

  app_settings = {
    ASPNETCORE_ENVIRONMENT = var.environment
    WEBSITES_PORT          = "8080"

    Jwt__Issuer           = "Misty.Api"
    Jwt__Audience         = "Misty.Web"
    ServiceBus__Namespace = azurerm_servicebus_namespace.main.name

    "ConnectionStrings__Database"           = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=sql-connection-string)"
    "ConnectionStrings__Redis"              = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=redis-connection-string)"
    "ConnectionStrings__BlobStorage"        = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=blob-connection-string)"
    "ConnectionStrings__ServiceBus"         = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=servicebus-connection-string)"
    "Jwt__Key"                              = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=jwt-signing-key)"
    "ApplicationInsights__ConnectionString" = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=appinsights-connection-string)"
    "OpenAI__ApiKey"                        = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=openai-api-key)"

    "Cors__AllowedOrigins__0" = trimsuffix(azurerm_storage_account.main.primary_web_endpoint, "/")
  }

  tags = local.tags
}

