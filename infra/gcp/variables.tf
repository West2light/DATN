variable "project_id" {
  type = string
}

variable "region" {
  type = string
}

variable "zone" {
  type = string
}

variable "machine_type" {
  type    = string
  default = "e2-medium"
}

variable "game_port" {
  type    = number
  default = 7777
}

variable "registry_port" {
  type    = number
  default = 8080
}

variable "web_game_port" {
  type    = number
  default = 7778
}

variable "web_http_port" {
  type    = number
  default = 80
}

variable "map_file" {
  type    = string
  default = "random-32-32-10.map"
}

variable "algorithm" {
  type    = string
  default = "AStar"
}

variable "max_players" {
  type    = number
  default = 8
}

variable "registry_data_file" {
  type    = string
  default = "/var/lib/tank-mapf/sessions.json"
}

variable "allowed_game_sources" {
  type    = list(string)
  default = ["0.0.0.0/0"]
}

variable "allowed_registry_sources" {
  type    = list(string)
  default = ["0.0.0.0/0"]
}

variable "allowed_web_game_sources" {
  type    = list(string)
  default = ["0.0.0.0/0"]
}

variable "allowed_web_http_sources" {
  type    = list(string)
  default = ["0.0.0.0/0"]
}

variable "allowed_ssh_sources" {
  type    = list(string)
  default = []
}

variable "server_session_code" {
  type      = string
  sensitive = true
}

variable "registry_admin_token" {
  type      = string
  sensitive = true
}

variable "vm_name" {
  type    = string
  default = "tank-mapf-server"
}

variable "network_name" {
  type    = string
  default = "tank-mapf-network"
}

variable "subnetwork_name" {
  type    = string
  default = "tank-mapf-subnet"
}

variable "artifact_bucket_name" {
  type    = string
  default = ""
}

variable "boot_image" {
  type    = string
  default = "projects/ubuntu-os-cloud/global/images/family/ubuntu-2204-lts"
}

variable "boot_disk_size_gb" {
  type    = number
  default = 30
}
