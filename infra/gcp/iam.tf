resource "google_service_account" "vm" {
  account_id   = local.vm_service_account
  display_name = "Tank MAPF VM Service Account"
}

resource "google_storage_bucket_iam_member" "vm_artifact_admin" {
  bucket = google_storage_bucket.artifacts.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.vm.email}"
}
