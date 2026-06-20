resource "google_compute_network" "tank_mapf" {
  name                    = var.network_name
  auto_create_subnetworks = false
}

resource "google_compute_subnetwork" "tank_mapf" {
  name          = var.subnetwork_name
  ip_cidr_range = "10.20.0.0/24"
  region        = var.region
  network       = google_compute_network.tank_mapf.id
}

resource "google_compute_address" "server_static_ip" {
  name   = local.static_ip_name
  region = var.region
}

resource "google_compute_instance" "server" {
  name         = var.vm_name
  machine_type = var.machine_type
  zone         = var.zone
  tags         = [local.instance_tag]

  labels = {
    app = "tank-mapf"
    env = "dev"
  }

  boot_disk {
    initialize_params {
      image = var.boot_image
      size  = var.boot_disk_size_gb
      type  = "pd-balanced"
    }
  }

  network_interface {
    subnetwork = google_compute_subnetwork.tank_mapf.id

    access_config {
      nat_ip = google_compute_address.server_static_ip.address
    }
  }

  service_account {
    email  = google_service_account.vm.email
    scopes = ["https://www.googleapis.com/auth/cloud-platform"]
  }

  metadata = {
    enable-oslogin = "TRUE"
    startup-script = local.startup_script
  }
}
