resource "azurerm_servicebus_namespace" "main" {
  name                = "sb-${local.prefix}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Standard"
  tags                = local.tags
}

resource "azurerm_servicebus_topic" "message_events" {
  name         = "message-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_topic" "membership_events" {
  name         = "membership-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_topic" "role_events" {
  name         = "role-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_topic" "moderation_events" {
  name         = "moderation-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_topic" "friend_events" {
  name         = "friend-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_topic" "channel_invite_events" {
  name         = "channel-invite-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_topic" "channel_events" {
  name         = "channel-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_topic" "block_events" {
  name         = "block-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_topic" "user_events" {
  name         = "user-events"
  namespace_id = azurerm_servicebus_namespace.main.id
}


resource "azurerm_servicebus_subscription" "message_events__realtime_delivery" {
  name               = "realtime-delivery"
  topic_id           = azurerm_servicebus_topic.message_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "message_events__ai_response" {
  name               = "ai-response"
  topic_id           = azurerm_servicebus_topic.message_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "message_events__inbox_worker" {
  name               = "inbox-worker"
  topic_id           = azurerm_servicebus_topic.message_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "membership_events__cache_invalidation" {
  name               = "cache-invalidation"
  topic_id           = azurerm_servicebus_topic.membership_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "membership_events__realtime_broadcast" {
  name               = "realtime-broadcast"
  topic_id           = azurerm_servicebus_topic.membership_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "role_events__cache_invalidation" {
  name               = "cache-invalidation"
  topic_id           = azurerm_servicebus_topic.role_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "role_events__realtime_broadcast" {
  name               = "realtime-broadcast"
  topic_id           = azurerm_servicebus_topic.role_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "moderation_events__cache_invalidation" {
  name               = "cache-invalidation"
  topic_id           = azurerm_servicebus_topic.moderation_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "moderation_events__realtime_broadcast" {
  name               = "realtime-broadcast"
  topic_id           = azurerm_servicebus_topic.moderation_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "friend_events__inbox_worker" {
  name               = "inbox-worker"
  topic_id           = azurerm_servicebus_topic.friend_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "channel_invite_events__inbox_worker" {
  name               = "inbox-worker"
  topic_id           = azurerm_servicebus_topic.channel_invite_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "channel_events__audit" {
  name               = "audit"
  topic_id           = azurerm_servicebus_topic.channel_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "block_events__audit" {
  name               = "audit"
  topic_id           = azurerm_servicebus_topic.block_events.id
  max_delivery_count = 10
}

resource "azurerm_servicebus_subscription" "user_events__audit" {
  name               = "audit"
  topic_id           = azurerm_servicebus_topic.user_events.id
  max_delivery_count = 10
}
