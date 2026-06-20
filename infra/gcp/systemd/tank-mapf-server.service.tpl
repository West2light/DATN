[Unit]
Description=Tank MAPF Dedicated Server
After=network-online.target tank-mapf-registry.service
Wants=network-online.target
ConditionPathExists=${server_binary}

[Service]
Type=simple
User=${service_user}
Group=${service_user}
WorkingDirectory=${working_dir}
EnvironmentFile=${env_file}
EnvironmentFile=-${runtime_env_file}
ExecStart=/bin/bash -lc 'cd ${working_dir} && exec ${server_binary} --server --port "$GAME_PORT" --map "$MAP_FILE" --algorithm "$ALGORITHM" --sessionCode "$SESSION_CODE" --maxPlayers "$MAX_PLAYERS" --registryUrl "$REGISTRY_PUBLIC_BASE_URL"'
Restart=always
RestartSec=5
StandardOutput=append:/var/log/tank-mapf/server.log
StandardError=append:/var/log/tank-mapf/server.log

[Install]
WantedBy=multi-user.target
