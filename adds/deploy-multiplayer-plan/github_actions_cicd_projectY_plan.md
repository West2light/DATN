# Kế hoạch CI/CD GitHub Actions cho ProjectY

Ngày lập: 2026-06-13  
Repo: `D:\2025.2\DATN\projectY`  
Branch nền nên triển khai MVP: `networking-gcp`

## 1. Mục tiêu

Thiết lập GitHub Actions để tự động hóa luồng build và deploy cho nhánh GCP/Web multiplayer của ProjectY:

- Kiểm tra pull request/push không làm hỏng cấu hình Unity, Terraform và service registry.
- Build Linux Dedicated Server bằng entrypoint `BuildServer.BuildLinuxServer`.
- Build WebGL client bằng entrypoint `BuildWebClient.BuildWebGL`.
- Chạy Terraform plan cho hạ tầng trong `infra/gcp`.
- Cho phép Terraform apply và deploy artifact lên VM GCP bằng thao tác manual có kiểm soát.
- Không đưa secrets, local Terraform state hoặc file biến thật vào Git.

## 2. Trạng thái hiện tại của repo

### 2.1 Branch

`networking-gcp` đang khác `dev`:

- `networking-gcp` có commit riêng `be80c4e feat: add web multiplayer deployment lane`.
- `dev` đang hơn `networking-gcp` 2 commit, chủ yếu sửa `Assets/Scripts/Backtest/*` và `Tools/backtest_plot_report.py`.
- Merge thử `dev` vào `networking-gcp` không thấy conflict trực tiếp.

Khuyến nghị trước khi làm CI/CD:

1. Giữ `networking-gcp` làm branch CI/CD MVP vì branch này chứa Terraform, deploy scripts, WebGL/server build code và invite registry.
2. Merge `dev` vào `networking-gcp` trước khi tạo workflow để branch deploy không thiếu sửa mới.
3. Sau khi pipeline chạy ổn, mới quyết định merge `networking-gcp` về `dev` hoặc `main`.

### 2.2 File build/deploy đã có

Unity build entrypoint:

- `Assets/Editor/BuildServer.cs`
  - Method: `BuildServer.BuildLinuxServer`
  - Output: `Builds/LinuxServer/TankMapfServer.x86_64`
  - Target: Linux Dedicated Server
- `Assets/Editor/BuildWebClient.cs`
  - Method: `BuildWebClient.BuildWebGL`
  - Output: `Builds/WebGL`
  - Target: WebGL
  - Tắt WebGL compression để phục vụ qua HTTP hiện tại.

GCP/Terraform:

- `infra/gcp/*.tf`
- `infra/gcp/scripts/deploy-release.sh`
- `infra/gcp/scripts/deploy-web.sh`
- `infra/gcp/systemd/*.service.tpl`
- `services/invite-registry/app.py`

Unity version hiện tại:

- `6000.3.10f1`

### 2.3 Khoảng trống hiện tại

- Chưa có thư mục `.github/workflows`.
- `infra/gcp/tankmapf.auto.tfvars`, `terraform.tfstate`, `terraform.tfstate.backup` đang tồn tại local nhưng đã được `.gitignore` bỏ qua. Không đưa các file này vào commit.
- Chưa có remote Terraform backend. MVP có thể chạy local state trong GitHub Actions tạm thời, nhưng apply lâu dài nên dùng remote backend GCS để tránh state trôi giữa các lần chạy.

## 3. Chiến lược branch cho CI/CD

### 3.1 Giai đoạn MVP

Dùng `networking-gcp`:

- `push` vào `networking-gcp`: chạy validate, Terraform plan, build server, build WebGL.
- `pull_request` vào `networking-gcp`: chạy validate, Terraform plan, build nếu thời gian chấp nhận được.
- `workflow_dispatch`: deploy manual lên GCP.

Không nên auto deploy mỗi lần push trong giai đoạn MVP vì Terraform apply và restart service trên VM là thao tác có rủi ro.

### 3.2 Sau khi ổn định

Khi `networking-gcp` đã được kiểm chứng:

- Nếu `dev` là integration branch: chuyển build/test/plan sang `dev`.
- Nếu `main` là release branch: chỉ deploy thật từ `main` hoặc tag release.
- Giữ deploy production bằng manual approval, không auto apply từ branch dev.

## 4. GitHub Environments và secrets

Tạo 2 GitHub Environments:

- `gcp-preview`
  - Dùng cho Terraform plan, build artifact, test deploy tạm.
- `gcp-production`
  - Dùng cho Terraform apply/deploy thật.
  - Bật required reviewers.

### 4.1 Secrets/variables cần có

GitHub Repository Variables:

- `GCP_PROJECT_ID`
- `GCP_REGION`
- `GCP_ZONE`
- `GCP_WORKLOAD_IDENTITY_PROVIDER`
- `GCP_DEPLOY_SERVICE_ACCOUNT`
- `TF_VAR_PROJECT_ID` nếu muốn map riêng cho Terraform
- `TF_VAR_REGION`
- `TF_VAR_ZONE`
- `TF_VAR_ARTIFACT_BUCKET_NAME`

GitHub Environment Secrets:

- `TF_VAR_SERVER_SESSION_CODE`
- `TF_VAR_REGISTRY_ADMIN_TOKEN`
- `UNITY_LICENSE` hoặc bộ credentials Unity tương ứng với runner/action build được chọn

Không lưu các giá trị này trong:

- `terraform.tfvars`
- `*.auto.tfvars`
- workflow YAML
- commit history
- log deploy

## 5. Xác thực GCP bằng GitHub OIDC

Mục tiêu là không dùng JSON service account key dài hạn trong GitHub secrets.

Thiết lập Workload Identity Federation trên GCP:

1. Tạo service account deploy, ví dụ `github-deploy`.
2. Cấp quyền tối thiểu:
   - Compute Admin hoặc role custom đủ quản lý VM/firewall/address.
   - Storage Admin hoặc quyền bucket cụ thể để upload artifact.
   - Service Account User nếu Terraform cần gắn service account cho VM.
   - IAM Workload Identity User cho GitHub provider.
3. Tạo Workload Identity Pool/Provider cho repo GitHub.
4. Restrict principal theo repo và branch nếu có thể:
   - Cho `networking-gcp` được plan/build/deploy MVP.
   - Về sau đổi sang `main` hoặc tag release cho production.

Workflow sẽ dùng:

- `google-github-actions/auth`
- `google-github-actions/setup-gcloud`
- `hashicorp/setup-terraform`

## 6. Cấu trúc workflow đề xuất

Tạo các file sau trong `.github/workflows/`:

```text
.github/workflows/
  ci-validate.yml
  terraform-plan.yml
  unity-build.yml
  deploy-gcp.yml
```

Tách workflow giúp dễ debug:

- `ci-validate.yml`: kiểm tra nhẹ, không cần secrets nhạy cảm.
- `terraform-plan.yml`: kiểm tra hạ tầng, cần GCP OIDC và Terraform vars.
- `unity-build.yml`: build artifact server/web, cần Unity license.
- `deploy-gcp.yml`: manual deploy, cần GCP OIDC, artifact, Terraform output và approval.

## 7. Phase 0 - Chuẩn bị repo trước CI/CD

### Việc cần làm

1. Merge `dev` vào `networking-gcp`.
2. Đảm bảo `.vscode/settings.json` local không bị đưa vào commit nếu chỉ là cấu hình máy.
3. Kiểm tra `.gitignore` đã bỏ qua:
   - `*.tfstate`
   - `*.tfstate.*`
   - `*.tfplan`
   - `terraform.tfvars`
   - `*.auto.tfvars`
   - `services/invite-registry/*.log`
4. Chạy `git status --short` và xác nhận không có secret/state bị stage.
5. Tạo thư mục `.github/workflows`.

### Tiêu chí pass

- `networking-gcp` chứa đủ commit từ `dev`.
- `git status` không có Terraform state/vars/log bị track mới.
- Không có secret thật trong workflow hoặc docs.

## 8. Phase 1 - CI validate nhẹ

Workflow: `.github/workflows/ci-validate.yml`

Trigger:

- `pull_request` vào `networking-gcp`
- `push` vào `networking-gcp`
- `workflow_dispatch`

Job đề xuất:

1. Checkout code.
2. Kiểm tra không có file nhạy cảm:
   - `infra/gcp/*.tfstate`
   - `infra/gcp/*.tfvars`
   - `infra/gcp/*.auto.tfvars`
   - `services/invite-registry/*.log`
3. Kiểm tra Terraform format:
   - `terraform -chdir=infra/gcp fmt -check -recursive`
4. Kiểm tra Python registry syntax:
   - `python -m py_compile services/invite-registry/app.py`
5. Kiểm tra shell scripts syntax:
   - `bash -n infra/gcp/scripts/deploy-release.sh`
   - `bash -n infra/gcp/scripts/deploy-web.sh`
   - `bash -n infra/gcp/scripts/startup.sh`

Ghi chú:

- GitHub hosted Linux runner chạy được các kiểm tra này.
- Không cần Unity license.
- Không cần GCP credentials nếu chỉ fmt/syntax.

## 9. Phase 2 - Terraform plan

Workflow: `.github/workflows/terraform-plan.yml`

Trigger:

- `pull_request` vào `networking-gcp`
- `push` vào `networking-gcp`
- `workflow_dispatch`

Job:

1. Checkout code.
2. Auth GCP qua OIDC.
3. Setup Terraform.
4. Chạy:

```bash
terraform -chdir=infra/gcp init
terraform -chdir=infra/gcp fmt -check -recursive
terraform -chdir=infra/gcp validate
terraform -chdir=infra/gcp plan -no-color -out=tfplan
```

Biến Terraform nên truyền bằng environment:

```bash
TF_VAR_project_id
TF_VAR_region
TF_VAR_zone
TF_VAR_server_session_code
TF_VAR_registry_admin_token
TF_VAR_artifact_bucket_name
```

Khuyến nghị backend:

- MVP: có thể dùng local backend trong runner để kiểm tra plan.
- Deploy thật: thêm GCS backend trước khi dùng apply thường xuyên.

Tiêu chí pass:

- `terraform validate` pass.
- `terraform plan` không lỗi.
- Plan output không in secret.

## 10. Phase 3 - Unity build artifact

Workflow: `.github/workflows/unity-build.yml`

Trigger:

- `push` vào `networking-gcp`
- `workflow_dispatch`

Job build server:

```bash
unity-editor \
  -batchmode \
  -nographics \
  -quit \
  -projectPath . \
  -executeMethod BuildServer.BuildLinuxServer \
  -logFile build-server.log
```

Đóng gói artifact:

```bash
tar -czf tank-mapf-server-${GITHUB_SHA}.tar.gz -C Builds/LinuxServer .
```

Job build WebGL:

```bash
unity-editor \
  -batchmode \
  -nographics \
  -quit \
  -projectPath . \
  -executeMethod BuildWebClient.BuildWebGL \
  -logFile build-webgl.log
```

Đóng gói artifact:

```bash
tar -czf tank-mapf-web-${GITHUB_SHA}.tar.gz -C Builds/WebGL .
```

Runner lựa chọn:

- Ưu tiên 1: self-hosted runner có Unity `6000.3.10f1` đã cài sẵn, ít rủi ro nhất với Unity 6.
- Ưu tiên 2: GitHub hosted runner + Unity build action nếu action hỗ trợ đúng version/license.

Artifact names:

- `tank-mapf-server-${{ github.sha }}.tar.gz`
- `tank-mapf-web-${{ github.sha }}.tar.gz`

Tiêu chí pass:

- Server artifact có `TankMapfServer.x86_64`.
- Web artifact có `index.html`, `Build/`, `TemplateData/`.
- Build logs được upload khi fail.

## 11. Phase 4 - Upload artifact lên GCS

Có 2 cách:

### Cách A - Upload trong workflow build

Sau khi build pass:

```bash
gcloud storage cp tank-mapf-server-${GITHUB_SHA}.tar.gz gs://${ARTIFACT_BUCKET}/server/${GITHUB_SHA}/
gcloud storage cp tank-mapf-web-${GITHUB_SHA}.tar.gz gs://${ARTIFACT_BUCKET}/web/${GITHUB_SHA}/
```

Ưu điểm:

- Deploy workflow chỉ cần input SHA.

Nhược điểm:

- Build workflow cần GCP auth và bucket permission.

### Cách B - Upload trong workflow deploy

Build workflow chỉ upload GitHub artifact. Deploy workflow download GitHub artifact rồi copy lên GCS.

Ưu điểm:

- GCP permission chỉ nằm trong deploy workflow.

Nhược điểm:

- Deploy phụ thuộc retention của GitHub artifact.

Khuyến nghị MVP: dùng Cách A nếu bucket đã có sẵn từ Terraform, vì deploy script hiện nhận `ARTIFACT_URI`/`WEB_ARTIFACT_URI` dạng `gs://...`.

## 12. Phase 5 - Deploy manual lên GCP

Workflow: `.github/workflows/deploy-gcp.yml`

Trigger:

```yaml
workflow_dispatch:
  inputs:
    release_sha:
      description: Commit SHA/artifact version cần deploy
      required: true
    session_code:
      description: Invite/session code public
      required: true
    map_file:
      description: MAPF map file
      required: false
      default: random-32-32-10.map
    algorithm:
      description: Pathfinding algorithm
      required: false
      default: AStar
    max_players:
      description: Max players
      required: false
      default: "8"
```

Job deploy:

1. Checkout code.
2. Auth GCP qua OIDC.
3. Setup Terraform.
4. `terraform init`.
5. `terraform apply` nếu hạ tầng chưa có hoặc cần cập nhật.
6. Lấy outputs:
   - `server_static_ip`
   - `artifact_bucket`
   - `registry_base_url`
   - `web_url`
7. Tạo artifact URI:

```bash
ARTIFACT_URI="gs://${ARTIFACT_BUCKET}/server/${RELEASE_SHA}/tank-mapf-server-${RELEASE_SHA}.tar.gz"
WEB_ARTIFACT_URI="gs://${ARTIFACT_BUCKET}/web/${RELEASE_SHA}/tank-mapf-web-${RELEASE_SHA}.tar.gz"
```

8. Gọi deploy script trên VM qua SSH/gcloud:

```bash
gcloud compute ssh tank-mapf-server \
  --zone "${GCP_ZONE}" \
  --command "sudo ARTIFACT_URI='${ARTIFACT_URI}' RELEASE_SHA='${RELEASE_SHA}' PUBLIC_IP='${SERVER_IP}' SESSION_CODE='${SESSION_CODE}' REGISTRY_ADMIN_TOKEN='${REGISTRY_ADMIN_TOKEN}' MAP_FILE='${MAP_FILE}' ALGORITHM='${ALGORITHM}' MAX_PLAYERS='${MAX_PLAYERS}' /opt/tank-mapf/scripts/deploy-release.sh"
```

9. Deploy WebGL:

```bash
gcloud compute ssh tank-mapf-server \
  --zone "${GCP_ZONE}" \
  --command "sudo WEB_ARTIFACT_URI='${WEB_ARTIFACT_URI}' WEB_RELEASE_SHA='${RELEASE_SHA}' /opt/tank-mapf/scripts/deploy-web.sh"
```

10. Smoke test endpoints:

```bash
curl -fsS "${REGISTRY_BASE_URL}/health"
curl -fsS "${WEB_URL}"
```

Tiêu chí pass:

- `tank-mapf-server.service` active.
- `tank-mapf-server-web.service` active.
- `nginx` reload thành công.
- Registry trả health OK.
- Web URL mở được.
- Invite URL tạo được session.

## 13. Phase 6 - Terraform apply policy

Không để apply tự động từ push trong MVP.

Policy đề xuất:

- `terraform-plan.yml` chạy tự động trên PR/push.
- `deploy-gcp.yml` chạy manual.
- Environment `gcp-production` cần reviewer.
- Chỉ deploy từ:
  - `networking-gcp` trong MVP.
  - `main` hoặc tag release sau khi ổn định.

Nếu cần apply riêng:

Tạo `.github/workflows/terraform-apply.yml` với `workflow_dispatch` only, nhưng không bắt buộc nếu `deploy-gcp.yml` đã apply trước deploy.

## 14. Phase 7 - Quan sát và rollback

Sau deploy, workflow nên in các lệnh debug:

```bash
gcloud compute ssh tank-mapf-server --zone "${GCP_ZONE}" --command "systemctl status tank-mapf-server.service --no-pager"
gcloud compute ssh tank-mapf-server --zone "${GCP_ZONE}" --command "systemctl status tank-mapf-server-web.service --no-pager"
gcloud compute ssh tank-mapf-server --zone "${GCP_ZONE}" --command "journalctl -u tank-mapf-server.service -n 100 --no-pager"
gcloud compute ssh tank-mapf-server --zone "${GCP_ZONE}" --command "journalctl -u tank-mapf-server-web.service -n 100 --no-pager"
gcloud compute ssh tank-mapf-server --zone "${GCP_ZONE}" --command "journalctl -u tank-mapf-registry.service -n 100 --no-pager"
```

Rollback:

- Giữ artifact theo SHA trong GCS.
- Deploy lại SHA cũ bằng `workflow_dispatch release_sha=<old_sha>`.
- Không cần rollback Terraform nếu chỉ lỗi app artifact.

## 15. Thứ tự triển khai đề xuất

### Batch 1 - An toàn repo

1. Merge `dev` vào `networking-gcp`.
2. Commit `.gitignore` nếu cần bổ sung rule cho state/secrets.
3. Tạo `.github/workflows/ci-validate.yml`.
4. Push và xác nhận CI validate pass.

### Batch 2 - Terraform plan

1. Tạo GCP Workload Identity Federation.
2. Tạo GitHub variables/secrets.
3. Tạo `.github/workflows/terraform-plan.yml`.
4. Chạy PR/push để xác nhận plan pass.

### Batch 3 - Unity build

1. Chọn runner:
   - self-hosted Unity runner, hoặc
   - GitHub hosted + Unity action đã kiểm chứng.
2. Tạo `.github/workflows/unity-build.yml`.
3. Build server artifact.
4. Build WebGL artifact.
5. Upload artifact lên GitHub artifact và GCS.

### Batch 4 - Manual deploy

1. Tạo `.github/workflows/deploy-gcp.yml`.
2. Chạy deploy manual với release SHA vừa build.
3. Kiểm tra service, registry, WebGL.
4. Ghi lại endpoint và lỗi gặp vào runbook.

### Batch 5 - Promotion

1. Nếu MVP ổn, merge `networking-gcp` về `dev`.
2. Nếu `main` là release branch, tạo PR từ `dev` hoặc `networking-gcp` vào `main`.
3. Đổi production deploy trigger sang `main` hoặc tag.

## 16. Rủi ro chính và cách giảm

### Unity build fail vì license/version

Giảm rủi ro:

- Ưu tiên self-hosted runner đã cài Unity `6000.3.10f1`.
- Upload `build-server.log` và `build-webgl.log` khi fail.
- Chạy build manual local trước khi đưa vào CI.

### Terraform state bị mất hoặc trôi

Giảm rủi ro:

- Không commit local state.
- Chuyển sang GCS backend trước khi apply thường xuyên.
- Chỉ một workflow deploy/apply chạy cùng lúc bằng `concurrency`.

### Secret bị in ra log

Giảm rủi ro:

- Dùng GitHub secrets.
- Không `echo` token/session code.
- Truyền secret qua env.
- Mask secret nếu workflow có bước debug.

### Deploy restart làm gián đoạn demo

Giảm rủi ro:

- Deploy manual.
- Có approval environment.
- Giữ artifact SHA cũ để rollback nhanh.

### Branch `networking-gcp` bị lệch `dev`

Giảm rủi ro:

- Merge `dev` vào `networking-gcp` trước khi tạo workflow.
- Sau đó tạo PR định kỳ để đưa GCP lane về branch integration chính.

## 17. Definition of Done

CI/CD được coi là hoàn thành khi:

- `.github/workflows/ci-validate.yml` pass trên `networking-gcp`.
- `.github/workflows/terraform-plan.yml` pass, không dùng service account JSON key.
- `.github/workflows/unity-build.yml` tạo được server artifact và WebGL artifact.
- Artifact được upload lên GCS theo SHA.
- `.github/workflows/deploy-gcp.yml` deploy manual thành công lên VM.
- Registry health OK.
- Web URL mở được.
- Invite/session code join được vào server.
- Có rollback bằng release SHA cũ.
- Không có secret, tfstate, tfvars thật trong Git.

