# Plan: Fix lỗi `.br` WebGL bằng HTTPS + cấu hình tên miền `luminx.io.vn` cho GCP

Ngày lập: 2026-06-16
Repo: `F:\DATN`  ·  Nhánh: `networking-gcp`
Production hiện tại: `http://35.240.203.91/` (HTTP, IP trần)
Tên miền đã mua: **`luminx.io.vn`**

---

## 0. Triệu chứng & bối cảnh

Lỗi trên production:
```
Unable to load file Build/WebGL.framework.js.br!
Check that the file exists on the remote server.
(also check browser Console and Devtools Network tab to debug)
```

Đây KHÔNG phải lỗi thiếu file. File `.br` tồn tại; vấn đề là **trình duyệt không giải nén được Brotli trên HTTP/IP trần**.

Cấu hình hiện tại (đã verify trong repo):
- **Build**: `Assets/Editor/BuildWebClient.cs:39-40` →
  `PlayerSettings.WebGL.compressionFormat = Brotli;` và `PlayerSettings.WebGL.decompressionFallback = false;`
- **Serving**: nginx HTTP-only (`infra/gcp/scripts/startup.sh`, block `__TANK_MAPF_NGINX_SITE__`):
  `listen 80 default_server;` + các `location ~* \.js\.br$ { add_header Content-Encoding br always; ... }`.

---

## 1. Nguyên nhân gốc (chính xác)

1. **Brotli chỉ được trình duyệt giải nén qua HTTPS.** Chrome/Edge/Firefox chỉ gửi `Accept-Encoding: br` và chỉ tự giải nén `Content-Encoding: br` trên **secure origin** (HTTPS hoặc `localhost`). Trên `http://<ip>` trần, trình duyệt **không** nhận Brotli → nhận raw bytes nén.
2. **`decompressionFallback = false`** → Unity **không nhúng** bộ giải nén JS dự phòng. Loader phụ thuộc 100% vào trình duyệt. Khi trình duyệt không giải nén (vì HTTP), Unity nhận byte nén → không parse được → văng đúng lỗi `Unable to load file ...br!`.
3. nginx lại `add_header Content-Encoding br always` **vô điều kiện** (không theo `Accept-Encoding`) → trên HTTP càng làm trình duyệt bối rối.

→ Cộng hưởng: **HTTP + Brotli + no-fallback = fail cứng.** Đây chính là điều bạn quan sát ("gãy ở http và không giải nén được").

### 1.1 Các hướng xử lý

| Hướng | Cách | Ưu | Nhược |
|------|------|----|------|
| **A — HTTPS (CHỌN)** | Cài TLS cho `luminx.io.vn`, giữ Brotli | Tải nhẹ nhất; **fix luôn clipboard** (secure context) và mở đường `wss://` | Cần DNS + cert + sửa hạ tầng |
| B — Gzip | Đổi build sang `Gzip` | Chạy được trên HTTP | File lớn hơn Brotli; vẫn kẹt clipboard + chỉ `ws://`; vẫn nên HTTPS |
| C — decompressionFallback=true | Nhúng giải nén JS | Chạy trên HTTP | Loader to hơn, chậm hơn; HTTP vẫn kém |

→ Chọn **A**. Plan này tập trung vào A. (B/C chỉ là phương án chữa cháy tạm nếu cần.)

---

## 2. ⚠️ HTTPS KHÔNG chỉ là "cài cert" — hiệu ứng dây chuyền

Khi trang chạy HTTPS, trình duyệt **chặn mixed-content**. Multiplayer hiện kết nối WebSocket tới dedicated server. Bắt buộc xử lý:

- **WebSocket phải là `wss://`** (không còn `ws://`). Trang HTTPS gọi `ws://` → bị chặn → "Mất kết nối với host".
- **Client Unity hiện chỉ set `UseWebSockets = true`, KHÔNG set `UseEncryption`** (`NetworkManagerFactory.ConfigureTransport`, `Assets/Scripts/Multiplayer/NetworkManagerFactory.cs`) → đang nối `ws://`.
- **Server WS expose plaintext port 7778** (`web_game_port`, firewall `allow_web_game_tcp`) → cần nginx **TLS-terminate reverse proxy** cho WS.
- **Registry trả endpoint cho client**: `webHost = PUBLIC_IP`, `webGamePort = 7778`, `webUrl = http://IP/play?...` (xem `infra/gcp/scripts/startup.sh` helper `tank-mapf-create-room` và `services/invite-registry/app.py`). Tất cả phải đổi sang domain + https + wss.
- **Registry base URL** (`locals.tf: registry_base_url = http://IP:8080`) → đổi `https://luminx.io.vn` (đã proxy qua nginx `/api/...`, `/s/`).

> Nếu chỉ cài cert mà bỏ qua các điểm trên: site **load được** nhưng **multiplayer chết** (mixed-content block wss).

---

## 3. Kiến trúc đích

```text
                         ┌──────────────────────── GCP VM (static IP 35.240.203.91) ────────────────────────┐
Browser (HTTPS) ──TLS──► nginx :443                                                                          │
   │                       ├── server_name luminx.io.vn, www.luminx.io.vn                                    │
   │                       │     • static WebGL (Brotli, Content-Encoding: br)                               │
   │                       │     • /api/sessions, /api/rooms, /s/, /create  ──proxy──► 127.0.0.1:8080 (registry)
   │                       │                                                                                  │
   └── wss://game.luminx.io.vn ── server_name game.luminx.io.vn                                              │
                           │     • WebSocket upgrade  ──proxy──► 127.0.0.1:7778 (NGO WS server, plaintext)   │
                           └──────────────────────────────────────────────────────────────────────────────┘
  nginx :80  → 301 redirect tới https (+ giữ /.well-known cho ACME)
```

- **1 cert** phủ 3 hostname: `luminx.io.vn`, `www.luminx.io.vn`, `game.luminx.io.vn`.
- Dùng **subdomain `game.`** cho WS để `location /` của nó không đụng `/` của site tĩnh (nginx route theo SNI/`server_name`).
- TLS do **nginx terminate**; server WS nội bộ vẫn chạy plaintext `ws` trên localhost:7778 (không đổi systemd unit).

---

## 4. DNS — trỏ `luminx.io.vn` về GCP

### 4.1 Lấy IP tĩnh
```bash
cd infra/gcp
terraform output server_static_ip      # vd: 35.240.203.91
```
IP này là `google_compute_address.server_static_ip` (đã static, không đổi khi reboot VM).

### 4.2 Tạo bản ghi A (tại trang quản lý DNS của nhà đăng ký .io.vn)
| Type | Name (host) | Value | TTL |
|------|-------------|-------|-----|
| A | `@` (luminx.io.vn) | `35.240.203.91` | 300 |
| A | `www` | `35.240.203.91` | 300 |
| A | `game` | `35.240.203.91` | 300 |

> `.io.vn` do nhà đăng ký VN quản lý — UI mỗi nơi khác nhau, nhưng đều có mục "DNS Records / Quản lý bản ghi". Chỉ cần 3 bản ghi A trỏ về IP tĩnh.

**(Tùy chọn) dùng Google Cloud DNS** nếu muốn quản lý bằng Terraform:
- Tạo managed zone `luminx-io-vn`; thêm record A như trên; rồi đổi **NS** ở registrar trỏ về 4 name server Cloud DNS. Phức tạp hơn — chỉ làm nếu cần IaC cho DNS.

### 4.3 Verify trước khi xin cert
```bash
dig +short luminx.io.vn          # phải ra 35.240.203.91
dig +short game.luminx.io.vn     # phải ra 35.240.203.91
```
Chờ propagate (vài phút–vài giờ). **Phải đúng IP trước khi chạy certbot** (Let's Encrypt dùng HTTP-01 challenge qua port 80).

---

## 5. TLS cert

### 5A — certbot trên VM (Let's Encrypt) — KHUYẾN NGHỊ cho MVP
- Miễn phí, tự gia hạn (systemd timer), nhanh.
- Yêu cầu: port 80 mở (đã có `allow_web_http_tcp`), DNS đã trỏ đúng.
```bash
# SSH vào VM
sudo apt-get update
sudo apt-get install -y certbot python3-certbot-nginx

sudo certbot --nginx \
  -d luminx.io.vn -d www.luminx.io.vn -d game.luminx.io.vn \
  --non-interactive --agree-tos -m quangdong010203@gmail.com --redirect
```
- certbot tự: thêm `listen 443 ssl`, đường dẫn cert, **redirect 80→443**, và cron gia hạn (`certbot renew`).
- Cert lưu ở `/etc/letsencrypt/live/luminx.io.vn/`.

### 5B — GCP HTTPS Load Balancer + Google-managed cert (production bền hơn)
- Cert tự gia hạn bởi Google, không phụ thuộc VM. Nhưng cần: Global LB + backend service + (cho WS) cấu hình timeout/NEG riêng. Phức tạp hơn nhiều.
- **Để giai đoạn sau.** MVP dùng 5A.

---

## 6. nginx — cập nhật (sửa `infra/gcp/scripts/startup.sh`)

> Hiện startup.sh ghi 1 server block `listen 80`. Cần: (a) site tĩnh 443, (b) game subdomain 443 → WS proxy, (c) redirect 80→443. Lưu ý idempotency với certbot (xem §9.2).

### 6.1 Server block site tĩnh (443) — giữ nguyên các location .br + registry proxy
```nginx
server {
    listen 443 ssl;
    listen [::]:443 ssl;
    server_name luminx.io.vn www.luminx.io.vn;

    ssl_certificate     /etc/letsencrypt/live/luminx.io.vn/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/luminx.io.vn/privkey.pem;

    root ${web_root}/current;
    index index.html;

    # (GIỮ NGUYÊN) các location .js.br / .wasm.br / .data.br / .symbols.json.br
    #   add_header Content-Encoding br always; default_type ...; try_files $uri =404;
    # (GIỮ NGUYÊN) proxy registry: /api/sessions/  = /api/rooms  /s/  = /create

    location / { try_files $uri $uri/ /index.html; }
}
```

### 6.2 Server block game subdomain (443) → reverse-proxy WS tới 7778
```nginx
server {
    listen 443 ssl;
    server_name game.luminx.io.vn;

    ssl_certificate     /etc/letsencrypt/live/luminx.io.vn/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/luminx.io.vn/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:${web_game_port};   # 7778, ws plaintext nội bộ
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout  3600s;
        proxy_send_timeout  3600s;
    }
}
```

### 6.3 Redirect HTTP→HTTPS (port 80) — giữ ACME challenge
```nginx
server {
    listen 80 default_server;
    server_name luminx.io.vn www.luminx.io.vn game.luminx.io.vn;
    location /.well-known/acme-challenge/ { root /var/www/html; }   # certbot renew
    location / { return 301 https://$host$request_uri; }
}
```
> Nếu dùng `certbot --nginx --redirect`, certbot tự tạo phần redirect — khi đó để startup.sh chỉ ghi block 80 tối thiểu, tránh xung đột (xem §9.2).

---

## 7. Unity client — chuyển sang `wss://` (secure)

### 7.1 `NetworkManagerFactory.ConfigureTransport` — bật encryption cho WebGL internet
File: `Assets/Scripts/Multiplayer/NetworkManagerFactory.cs` (hàm `ConfigureTransport`).
```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    transport.UseWebSockets = true;
    // Trang HTTPS → phải wss. UseEncryption=true khiến NGO dùng wss trên WebGL
    // (TLS do trình duyệt + nginx lo; server nội bộ vẫn plaintext sau proxy).
    bool secure = transportMode == NetworkTransportMode.WebSocket
                  && LanSessionManager.UseSecureWebSocket;   // cờ mới, xem 7.2
    transport.UseEncryption = secure;
    if (secure)
        transport.SetClientSecret(LanSessionManager.SecureWebSocketHost, null); // serverCommonName = game.luminx.io.vn
#else
    transport.UseWebSockets = transportMode == NetworkTransportMode.WebSocket;
#endif
```
> ⚠️ **VERIFY theo version NGO** đang dùng: API `UseEncryption` / `SetClientSecret(serverCommonName, caCertificate)` có thể khác tên giữa các bản `com.unity.netcode.gameobjects`. Mục tiêu: WebGL client connect bằng **scheme `wss`** tới `game.luminx.io.vn:443`, còn server (sau nginx) vẫn nhận **plaintext ws** trên 7778. Đây là pattern reverse-proxy chuẩn cho NGO + WebGL. Test kỹ ở milestone D4.

### 7.2 Nguồn cờ secure + host (LanSessionManager / NetworkEndpointConfig)
- Cách gọn: WebGL internet client **luôn** secure khi registry trả `webHost` là domain + port 443. Thêm:
  - `NetworkEndpointConfig.secureWebSocket` (bool) hoặc suy ra `port == 443`.
  - `LanSessionManager.UseSecureWebSocket` + `SecureWebSocketHost` set khi `ActivateInternetClient`.
- Hoặc thêm enum `NetworkTransportMode.WebSocketSecure` và để registry trả `webTransport = "wss"`; `NetworkTransportModeUtility.Parse` map `"wss"` → secure. (Sạch hơn về mặt dữ liệu.)

### 7.3 Registry / helper / env — trả endpoint HTTPS + wss
File: `infra/gcp/scripts/startup.sh` (helper `tank-mapf-create-room`, payload Python) + `infra/gcp/systemd/*` env.
- `webHost = game.luminx.io.vn`
- `webGamePort = 443`
- `webTransport = "websocket"` (hoặc `"wss"` nếu dùng 7.2 enum)
- `webUrl = https://luminx.io.vn/play?session=<CODE>`
- `WEB_PUBLIC_BASE_URL = https://luminx.io.vn`
- `REGISTRY_PUBLIC_BASE_URL = https://luminx.io.vn`  (dùng cho link `/s/CODE`, `build_join_url`)
- **Tách internal vs public**: server-side POST của helper nên gọi **`http://127.0.0.1:8080`** (tránh TLS loopback), còn URL nhúng vào link/response dùng `https://luminx.io.vn`. Thêm biến `REGISTRY_INTERNAL_URL=http://127.0.0.1:${registry_port}` và đổi `curl` trong helper sang biến này.

### 7.4 Invite link builder phía client — tự đúng theo origin
- `LanLobbyController.BuildInviteLink` + `MenuViewBootstrap.GetRuntimeRegistryBaseUrl` suy URL từ `Application.absoluteURL` → khi site là `https://luminx.io.vn` thì link tự thành `https://...`. **Không cần sửa thêm** ngoài việc đảm bảo registry trả `webUrl` https.
- Liên kết plan workflow: sau khi HTTPS, clipboard `navigator.clipboard` chạy (secure context) → fallback `execCommand` trong `adds/multiplayer_internet_production_workflow_fix_plan.md` §4.3 vẫn giữ làm dự phòng, không hại.

---

## 8. Terraform / infra — thay đổi dự kiến

| File | Thay đổi |
|------|----------|
| `infra/gcp/variables.tf` | Thêm `domain` (=`luminx.io.vn`), `web_game_subdomain` (=`game.luminx.io.vn`), `acme_email`. |
| `infra/gcp/locals.tf` | `registry_base_url = "https://${var.domain}"`; thêm `registry_internal_url = "http://127.0.0.1:${var.registry_port}"`; `web_public_base_url = "https://${var.domain}"`. Truyền `domain`, `web_game_subdomain` vào `startup_script`. |
| `infra/gcp/firewall.tf` | Thêm rule **allow tcp 443** (`allow_web_https_tcp`). (Tùy chọn, sau khi proxy ổn) **đóng public 7778 và 8080** — chỉ còn truy cập nội bộ qua nginx. |
| `infra/gcp/scripts/startup.sh` | nginx 443 site + game subdomain WS proxy + redirect 80→443; helper env webHost/webGamePort/webUrl/base urls (§7.3); (tùy) bootstrap certbot. |
| `infra/gcp/systemd/tank-mapf-server-web.service.tpl` | **Không đổi** — server WS vẫn plaintext nội bộ 7778. |
| `infra/gcp/outputs.tf` | `web_url = "https://${var.domain}"`; thêm `web_game_endpoint = "wss://${var.web_game_subdomain}"`. |

> Cert: certbot tạo file ngoài Terraform. startup.sh nên **kiểm tra cert tồn tại** trước khi bật block 443 (tránh `nginx -t` fail khi chưa có cert) — xem §9.2.

---

## 9. Rủi ro & lưu ý

### 9.1 Trình tự bắt buộc
DNS đúng IP **trước** → certbot (HTTP-01 cần port 80 + DNS) → bật 443. Xin cert khi DNS chưa trỏ → fail.

### 9.2 startup.sh ghi đè nginx vs certbot
- startup.sh chạy mỗi lần (re)create VM và **ghi đè** `/etc/nginx/sites-available/tank-mapf-web`. Nếu certbot đã sửa file → bị mất khi re-run.
- **Giải pháp**: startup.sh ghi block 443 với đường dẫn cert cố định `/etc/letsencrypt/live/luminx.io.vn/...` nhưng **bọc điều kiện**: nếu chưa có cert thì chỉ bật block 80 (+ ACME), chạy certbot, rồi reload. Hoặc tách TLS thành `include /etc/nginx/snippets/tls.conf;` do certbot quản lý. Đảm bảo `nginx -t` không fail khi thiếu cert.
- Re-create VM ⇒ phải re-issue cert (Let's Encrypt). Cân nhắc lưu/khôi phục `/etc/letsencrypt` qua bucket nếu hay tạo lại VM, hoặc dùng DNS-01.

### 9.3 NGO wss (điểm cần test kỹ)
- API `UseEncryption`/`SetClientSecret` khác theo version NGO. Nếu client wss + server plaintext-sau-proxy không bắt tay được → fallback: **TLS port riêng** (nginx `listen 7779 ssl; proxy → 7778`), client connect `host=luminx.io.vn, port=7779, secure`. Không cần subdomain.
- Verify bằng DevTools → Network → WS: phải thấy `wss://game.luminx.io.vn` status 101.

### 9.4 Mixed content
Mọi request phải HTTPS. Map files (`StreamingAssets/MapData/*.map`) cùng origin → OK. Registry `/api` qua https domain → OK.

### 9.5 Giữ nguyên
- **Không đổi build** (giữ Brotli + `decompressionFallback=false`) — HTTPS giải quyết. Chỉ cân nhắc Gzip nếu HTTPS bị hoãn.
- Giữ font/UI WebGL, readiness gate.

---

## 10. Milestones (làm tuần tự, test từng bước)

| MS | Nội dung | Done khi |
|----|----------|----------|
| **D1** | DNS: 3 bản ghi A (`@`, `www`, `game`) → IP tĩnh | `dig +short luminx.io.vn` & `game.luminx.io.vn` ra đúng IP |
| **D2** | certbot + nginx 443 cho site tĩnh + redirect 80→443; firewall 443 | `https://luminx.io.vn` mở được, **game load KHÔNG còn lỗi `.br`** (đã fix lỗi chính) |
| **D3** | nginx game subdomain 443 → WS proxy 7778 | `curl -I https://game.luminx.io.vn` ra TLS OK; WS upgrade 101 |
| **D4** | Unity client `wss` (UseEncryption + host) + registry webHost/webGamePort/webUrl/base = https/wss → rebuild WebGL + redeploy | Tạo room + join: DevTools thấy `wss://game...` 101; multiplayer chạy |
| **D5** | Terraform hóa: `domain` var, base urls, firewall 443, (đóng 7778/8080 public) | `terraform apply` tái lập đầy đủ; re-create VM vẫn HTTPS |
| **D6** | Verify e2e | §11 pass |

---

## 11. Verify (checklist)

```bash
# 1. Header Brotli qua HTTPS
curl -sI "https://luminx.io.vn/Build/$(...).framework.js.br" | grep -i "content-encoding"
#   → content-encoding: br   (status 200)

# 2. Cert hợp lệ
curl -sI https://luminx.io.vn | head -1            # HTTP/1.1 200
echo | openssl s_client -connect game.luminx.io.vn:443 -servername game.luminx.io.vn 2>/dev/null | openssl x509 -noout -subject -dates
```
- Mở `https://luminx.io.vn` → game load, **không** còn `Unable to load file ...br`.
- Tạo room (HOST) → DevTools Network → WS → `wss://game.luminx.io.vn` **101 Switching Protocols**.
- Tab 2 mở invite link `https://luminx.io.vn/play?session=...` → vào cùng phòng → 2 tank.
- **Clipboard**: bấm COPY LINK → dán ra được (secure context).
- HTTP cũ `http://luminx.io.vn` → 301 sang HTTPS.

---

## 12. Liên kết plan khác

- `adds/multiplayer_internet_production_workflow_fix_plan.md` — workflow Internet/lobby. Sau HTTPS: clipboard chạy thật (secure context); link mời thành `https://`.
- `adds/networking_gcp_deployment_plan*.md` — hạ tầng GCP gốc (Terraform/systemd/nginx) mà plan này mở rộng.
- `adds/webgl_production_ui_rendering_fix_plan.md` — ràng buộc không đụng font/scale.
