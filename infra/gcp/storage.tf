resource "google_storage_bucket" "artifacts" {
  name                        = local.artifact_bucket_name
  location                    = var.region
  uniform_bucket_level_access = true
  force_destroy               = false

  public_access_prevention = "enforced"

  versioning {
    enabled = true
  }
}
