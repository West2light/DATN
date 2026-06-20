locals {
  name_prefix          = "tank-mapf"
  instance_tag         = "tank-mapf-server"
  static_ip_name       = "${local.name_prefix}-ip"
  vm_service_account   = "${local.name_prefix}-vm"
  artifact_bucket_name = var.artifact_bucket_name != "" ? var.artifact_bucket_name : "${var.project_id}-tank-mapf-artifacts"
  registry_base_url    = "http://${google_compute_address.server_static_ip.address}:${var.registry_port}"
  invite_url           = "${local.registry_base_url}/s/${var.server_session_code}"
  service_user         = "tankmapf"
  server_env_file      = "/etc/tank-mapf/server.env"
  runtime_env_file     = "/etc/tank-mapf/runtime.env"
  install_root         = "/opt/tank-mapf"
  registry_root        = "/opt/tank-mapf/registry"
  releases_root        = "/opt/tank-mapf/releases"
  current_root         = "/opt/tank-mapf/current"
  scripts_root         = "/opt/tank-mapf/scripts"
  log_root             = "/var/log/tank-mapf"
  web_root             = "/var/www/tank-mapf-web"
  registry_app_py      = replace(file("${path.module}/../../services/invite-registry/app.py"), "\r\n", "\n")
  deploy_release_sh    = replace(file("${path.module}/scripts/deploy-release.sh"), "\r\n", "\n")
  deploy_web_sh        = replace(file("${path.module}/scripts/deploy-web.sh"), "\r\n", "\n")
  registry_service = templatefile("${path.module}/systemd/tank-mapf-registry.service.tpl", {
    service_user    = local.service_user
    env_file        = local.server_env_file
    working_dir     = local.registry_root
    registry_script = "${local.registry_root}/app.py"
  })
  server_service = templatefile("${path.module}/systemd/tank-mapf-server.service.tpl", {
    service_user     = local.service_user
    env_file         = local.server_env_file
    runtime_env_file = local.runtime_env_file
    working_dir      = local.current_root
    install_root     = local.install_root
    server_binary    = "${local.current_root}/TankMapfServer.x86_64"
  })
  web_server_service = templatefile("${path.module}/systemd/tank-mapf-server-web.service.tpl", {
    service_user     = local.service_user
    env_file         = local.server_env_file
    runtime_env_file = local.runtime_env_file
    working_dir      = local.current_root
    install_root     = local.install_root
    server_binary    = "${local.current_root}/TankMapfServer.x86_64"
  })
  startup_script = templatefile("${path.module}/scripts/startup.sh", {
    service_user         = local.service_user
    install_root         = local.install_root
    registry_root        = local.registry_root
    releases_root        = local.releases_root
    current_root         = local.current_root
    scripts_root         = local.scripts_root
    log_root             = local.log_root
    web_root             = local.web_root
    registry_data_file   = var.registry_data_file
    server_env_file      = local.server_env_file
    runtime_env_file     = local.runtime_env_file
    public_ip            = google_compute_address.server_static_ip.address
    game_port            = var.game_port
    registry_port        = var.registry_port
    web_game_port        = var.web_game_port
    registry_base_url    = local.registry_base_url
    map_file             = var.map_file
    algorithm            = var.algorithm
    max_players          = var.max_players
    session_code         = var.server_session_code
    registry_admin_token = var.registry_admin_token
    registry_app_py      = local.registry_app_py
    deploy_release_sh    = local.deploy_release_sh
    deploy_web_sh        = local.deploy_web_sh
    registry_service     = local.registry_service
    server_service       = local.server_service
    web_server_service   = local.web_server_service
  })
}
