output "server_static_ip" {
  value = google_compute_address.server_static_ip.address
}

output "game_endpoint" {
  value = "${google_compute_address.server_static_ip.address}:${var.game_port}"
}

output "registry_base_url" {
  value = local.registry_base_url
}

output "web_url" {
  value = "http://${google_compute_address.server_static_ip.address}"
}

output "web_game_endpoint" {
  value = "${google_compute_address.server_static_ip.address}:${var.web_game_port}"
}

output "invite_url" {
  value     = local.invite_url
  sensitive = true
}

output "artifact_bucket" {
  value = google_storage_bucket.artifacts.name
}
