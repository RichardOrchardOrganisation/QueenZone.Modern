terraform {
  required_version = "= 1.12.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "= 5.0.1"
    }
  }
}
