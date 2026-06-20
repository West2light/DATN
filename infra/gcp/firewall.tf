resource "google_compute_firewall" "allow_game_udp" {
  name    = "${local.name_prefix}-allow-game-udp"
  network = google_compute_network.tank_mapf.name

  target_tags   = [local.instance_tag]
  source_ranges = var.allowed_game_sources

  allow {
    protocol = "udp"
    ports    = [tostring(var.game_port)]
  }
}

resource "google_compute_firewall" "allow_registry_tcp" {
  name    = "${local.name_prefix}-allow-registry-tcp"
  network = google_compute_network.tank_mapf.name

  target_tags   = [local.instance_tag]
  source_ranges = var.allowed_registry_sources

  allow {
    protocol = "tcp"
    ports    = [tostring(var.registry_port)]
  }
}

resource "google_compute_firewall" "allow_web_game_tcp" {
  name    = "${local.name_prefix}-allow-web-game-tcp"
  network = google_compute_network.tank_mapf.name

  target_tags   = [local.instance_tag]
  source_ranges = var.allowed_web_game_sources

  allow {
    protocol = "tcp"
    ports    = [tostring(var.web_game_port)]
  }
}

resource "google_compute_firewall" "allow_web_http_tcp" {
  name    = "${local.name_prefix}-allow-web-http-tcp"
  network = google_compute_network.tank_mapf.name

  target_tags   = [local.instance_tag]
  source_ranges = var.allowed_web_http_sources

  allow {
    protocol = "tcp"
    ports    = [tostring(var.web_http_port)]
  }
}

resource "google_compute_firewall" "allow_ssh_tcp" {
  count   = length(var.allowed_ssh_sources) > 0 ? 1 : 0
  name    = "${local.name_prefix}-allow-ssh-tcp"
  network = google_compute_network.tank_mapf.name

  target_tags   = [local.instance_tag]
  source_ranges = var.allowed_ssh_sources

  allow {
    protocol = "tcp"
    ports    = ["22"]
  }
}
