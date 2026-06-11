# V3 - Deploy GCP, Terraform, CI/CD cho TankMAPF

Ngày lập: 2026-06-11

Project GCP:

- Project name: `TankMAPF`
- Project ID: `tankmapf`
- Project number: `112059749535`

Trạng thái hiện tại:

- Đang ở M9.
- Terraform base đã có trong `infra/gcp`.
- Linux server artifact local đã build được trong `Builds/LinuxServer`.
- Registry local dùng `18080` vì `8080` trên máy local đang bị `mcp-for-unity` chiếm.
- Deploy VM/GCP vẫn dùng registry port `8080`.

## 1. Vì sao `terraform plan` hỏi project_id

`project_id` là variable bắt buộc trong `infra/gcp/variables.tf`. Trước đó repo chỉ có `terraform.tfvars.example`, nên Terraform không tự load giá trị thật.

Đã thêm file local:

```text
infra/gcp/tankmapf.auto.tfvars
```

File này chứa các giá trị không nhạy cảm:

```hcl
project_id   = "tankmapf"
region       = "asia-southeast1"
zone         = "asia-southeast1-b"
machine_type = "e2-medium"
game_port     = 7777
registry_port = 8080
```

File `*.auto.tfvars` đã được ignore trong `.gitignore`, vì về sau có thể chứa biến local. Không đưa session code hoặc admin token thật vào Git.

Terraform vẫn cần hai secret khi plan/apply:

```text
server_session_code
registry_admin_token
```

Truyền qua env var:

```powershell
$env:TF_VAR_server_session_code="TEST123"
$env:TF_VAR_registry_admin_token="<strong-admin-token>"
```

## 2. Local GCP Setup

Đăng nhập gcloud:

```powershell
gcloud auth login
gcloud auth application-default login
gcloud config set project tankmapf
gcloud config set compute/region asia-southeast1
gcloud config set compute/zone asia-southeast1-b
```

Kiểm tra project:

```powershell
gcloud projects describe tankmapf
gcloud config list
```

Kiểm tra billing:

```powershell
gcloud billing projects describe tankmapf
```

Nếu billing chưa enabled, bật trong GCP Console trước khi apply Terraform.

Enable APIs cần cho M9-M13:

```powershell
gcloud services enable compute.googleapis.com `
  iam.googleapis.com `
  iamcredentials.googleapis.com `
  cloudresourcemanager.googleapis.com `
  sts.googleapis.com `
  storage.googleapis.com `
  serviceusage.googleapis.com `
  --project tankmapf
```

Kiểm tra API:

```powershell
gcloud services list --enabled --project tankmapf
```

## 3. M9 - Terraform Local Commands

Đi vào Terraform folder:

```powershell
cd D:\2025.2\DATN\projectY\infra\gcp
```

Set secret env cho phiên shell hiện tại:

```powershell
$env:TF_VAR_server_session_code="TEST123"
$env:TF_VAR_registry_admin_token="<strong-admin-token>"
```

Khuyến nghị tạo token bằng PowerShell:

```powershell
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$env:TF_VAR_registry_admin_token = [Convert]::ToBase64String($bytes)
```

Chạy Terraform:

```powershell
terraform fmt -recursive
terraform init
terraform validate
terraform plan -out=tankmapf.tfplan
terraform apply tankmapf.tfplan
```

Nếu `terraform plan` vẫn hỏi `project_id`, kiểm tra:

```powershell
Get-Location
Get-Content .\tankmapf.auto.tfvars
terraform console
```

Trong `terraform console`, thử:

```text
var.project_id
```

Kỳ vọng trả `tankmapf`.

Sau apply, lấy output:

```powershell
terraform output
terraform output -raw server_static_ip
terraform output -raw game_endpoint
terraform output -raw registry_base_url
terraform output -raw artifact_bucket
terraform output -raw invite_url
```

```log
PS D:\2025.2\DATN\projectY\infra\gcp> terraform output
artifact_bucket = "tankmapf-tank-mapf-artifacts"
game_endpoint = "35.240.203.91:7777"
invite_url = <sensitive>
registry_base_url = "http://35.240.203.91:8080"
server_static_ip = "35.240.203.91"
PS D:\2025.2\DATN\projectY\infra\gcp>
```

`invite_url` là sensitive vì chứa session code, nên chỉ dùng khi cần test.

## 4. Terraform State Cho CI/CD

MVP local có thể dùng local state. Trước khi chạy GitHub Actions apply, tạo remote state bucket riêng:

```powershell
gcloud storage buckets create gs://tankmapf-tfstate `
  --project tankmapf `
  --location asia-southeast1 `
  --uniform-bucket-level-access

gcloud storage buckets update gs://tankmapf-tfstate --versioning
```

M11 sẽ thêm backend Terraform:

```hcl
terraform {
  backend "gcs" {
    bucket = "tankmapf-tfstate"
    prefix = "terraform/state"
  }
}
```

Sau khi thêm backend:

```powershell
terraform init -migrate-state
terraform plan
```

Không làm bước migrate nếu local apply chưa ổn.

## 5. M10 - VM Bootstrap Sau Khi Apply

M10 sẽ thêm:

```text
infra/gcp/scripts/startup.sh
infra/gcp/scripts/deploy-release.sh
infra/gcp/systemd/tank-mapf-registry.service.tpl
infra/gcp/systemd/tank-mapf-server.service.tpl
```

Env file trên VM:

```text
/etc/tank-mapf/server.env
```

Biến cần có:

```text
GAME_PORT=7777
REGISTRY_PORT=8080
PUBLIC_IP=<terraform output server_static_ip>
REGISTRY_PUBLIC_BASE_URL=http://<PUBLIC_IP>:8080
MAP_FILE=random-32-32-10.map
ALGORITHM=AStar
MAX_PLAYERS=8
SESSION_CODE=<session-code>
REGISTRY_ADMIN_TOKEN=<admin-token>
```

Smoke test registry trên VM:

```powershell
gcloud compute ssh tank-mapf-server `
  --zone asia-southeast1-b `
  --project tankmapf `
  --command "curl -fsS http://127.0.0.1:8080/healthz && systemctl status tank-mapf-registry --no-pager"
```

## 6. M11 - GitHub OIDC Cho Terraform

Thay `OWNER/REPO` bằng repo GitHub thật.

Tạo deploy service account:

```powershell
gcloud iam service-accounts create github-deploy `
  --project tankmapf `
  --display-name "GitHub Deploy"
```

Gán quyền tối thiểu cho MVP:

```powershell
$sa="github-deploy@tankmapf.iam.gserviceaccount.com"

gcloud projects add-iam-policy-binding tankmapf --member "serviceAccount:$sa" --role "roles/compute.admin"
gcloud projects add-iam-policy-binding tankmapf --member "serviceAccount:$sa" --role "roles/iam.serviceAccountAdmin"
gcloud projects add-iam-policy-binding tankmapf --member "serviceAccount:$sa" --role "roles/iam.serviceAccountUser"
gcloud projects add-iam-policy-binding tankmapf --member "serviceAccount:$sa" --role "roles/storage.admin"
gcloud projects add-iam-policy-binding tankmapf --member "serviceAccount:$sa" --role "roles/serviceusage.serviceUsageAdmin"
```

Tạo Workload Identity Pool:

```powershell
gcloud iam workload-identity-pools create github-pool `
  --project tankmapf `
  --location global `
  --display-name "GitHub Actions Pool"
```

Tạo OIDC provider:

```powershell
gcloud iam workload-identity-pools providers create-oidc github-provider `
  --project tankmapf `
  --location global `
  --workload-identity-pool github-pool `
  --display-name "GitHub Provider" `
  --issuer-uri "https://token.actions.githubusercontent.com" `
  --attribute-mapping "google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref" `
  --attribute-condition "assertion.repository=='OWNER/REPO'"
```

Cho repo impersonate deploy service account:

```powershell
gcloud iam service-accounts add-iam-policy-binding $sa `
  --project tankmapf `
  --role "roles/iam.workloadIdentityUser" `
  --member "principalSet://iam.googleapis.com/projects/112059749535/locations/global/workloadIdentityPools/github-pool/attribute.repository/OWNER/REPO"
```

GitHub repository variables:

```text
GCP_PROJECT_ID=tankmapf
GCP_PROJECT_NUMBER=112059749535
GCP_REGION=asia-southeast1
GCP_ZONE=asia-southeast1-b
GCP_WORKLOAD_IDENTITY_PROVIDER=projects/112059749535/locations/global/workloadIdentityPools/github-pool/providers/github-provider
GCP_DEPLOY_SERVICE_ACCOUNT=github-deploy@tankmapf.iam.gserviceaccount.com
TF_STATE_BUCKET=tankmapf-tfstate
```

GitHub repository secrets:

```text
TF_VAR_server_session_code
TF_VAR_registry_admin_token
```

## 7. M12 - CI Build Unity Server

Khuyến nghị dùng self-hosted runner trên máy đã build thành công Linux server.

Local command đang tương ứng:

```powershell
& "D:\Unity\6000.3.10f1\Editor\Unity.exe" `
  -batchmode `
  -nographics `
  -quit `
  -projectPath "D:\2025.2\DATN\projectY" `
  -executeMethod BuildServer.BuildLinuxServer `
  -buildTarget Linux64 `
  -standaloneBuildSubtarget Server `
  -logFile "Builds\server-build.log"
```

Sau build:

```powershell
tar -czf tank-mapf-server-local.tar.gz -C Builds/LinuxServer .
gcloud storage cp tank-mapf-server-local.tar.gz gs://$(terraform -chdir=infra/gcp output -raw artifact_bucket)/server/local/
```

Trong GitHub Actions, artifact name nên là:

```text
tank-mapf-server-${GITHUB_SHA}.tar.gz
```

## 8. M13 - Deploy Artifact Lên VM

Sau M10 có `deploy-release.sh`, deploy thủ công mẫu:

```powershell
$bucket = terraform -chdir=infra/gcp output -raw artifact_bucket
$sha = git rev-parse --short HEAD
$artifact = "gs://$bucket/server/$sha/tank-mapf-server-$sha.tar.gz"

gcloud compute ssh tank-mapf-server `
  --zone asia-southeast1-b `
  --project tankmapf `
  --command "sudo ARTIFACT_URI='$artifact' RELEASE_SHA='$sha' MAP_FILE='random-32-32-10.map' ALGORITHM='AStar' SESSION_CODE='$env:TF_VAR_server_session_code' MAX_PLAYERS='8' /opt/tank-mapf/scripts/deploy-release.sh"
```

Smoke test public:

```powershell
$ip = terraform -chdir=infra/gcp output -raw server_static_ip
curl "http://$ip:8080/healthz"
curl "http://$ip:8080/api/sessions/$env:TF_VAR_server_session_code"
```

Unity client nhập:

```text
http://<STATIC_IP>:8080/s/<SESSION_CODE>
```

## 9. Thứ Tự Chạy Khuyến Nghị Từ Hiện Tại

1. Set gcloud project và enable APIs.
2. Set `TF_VAR_server_session_code` và `TF_VAR_registry_admin_token`.
3. Chạy `terraform plan -out=tankmapf.tfplan`.
4. Chạy `terraform apply tankmapf.tfplan`.
5. Sang M10 để bootstrap VM/systemd.
6. Sang M11 để tạo GitHub OIDC workflows.
7. Sang M12 để build/upload artifact.
8. Sang M13 để deploy artifact và test invite link public.

## 10. Lệnh Cleanup Khi Cần

Tắt VM để giảm chi phí:

```powershell
gcloud compute instances stop tank-mapf-server --zone asia-southeast1-b --project tankmapf
```

Bật lại:

```powershell
gcloud compute instances start tank-mapf-server --zone asia-southeast1-b --project tankmapf
```

Hủy hạ tầng Terraform:

```powershell
cd D:\2025.2\DATN\projectY\infra\gcp
terraform destroy
```

Chỉ destroy khi không còn cần static IP, VM, bucket artifact.
