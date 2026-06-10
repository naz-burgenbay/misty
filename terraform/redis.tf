resource "azurerm_managed_redis" "main" {
  name                = "redis-${local.prefix}"
  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  sku_name            = "Balanced_B1"
  tags                = local.tags

  default_database {
    access_keys_authentication_enabled = true
  }
}
