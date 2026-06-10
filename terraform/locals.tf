locals {
  prefix = "misty-${var.environment}"
  tags = {
    project     = "misty"
    environment = var.environment
  }
}
