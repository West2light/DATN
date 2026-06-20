[Unit]
Description=Tank MAPF Invite Registry
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=${service_user}
Group=${service_user}
WorkingDirectory=${working_dir}
EnvironmentFile=${env_file}
ExecStart=/usr/bin/python3 ${registry_script}
Restart=always
RestartSec=5
StandardOutput=append:/var/log/tank-mapf/registry.log
StandardError=append:/var/log/tank-mapf/registry.log

[Install]
WantedBy=multi-user.target
