resource "azurerm_storage_account" "main" {
  name                            = "st${replace(local.prefix, "-", "")}"
  resource_group_name             = azurerm_resource_group.main.name
  location                        = azurerm_resource_group.main.location
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  allow_nested_items_to_be_public = false
  tags                            = local.tags

  static_website {
    index_document     = "index.html"
    error_404_document = "index.html"
  }
}

resource "azurerm_storage_container" "avatars" {
  name                  = "avatars"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

resource "azurerm_storage_container" "attachments" {
  name                  = "attachments"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

resource "azurerm_storage_container" "channel_icons" {
  name                  = "channel-icons"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

# Azure CDN classic deleted. Azure Front Door Standard is the replacement but is $35/month. May add it later.
