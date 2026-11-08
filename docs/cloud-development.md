# GitHub merkezli geliştirme ve Azure planı

Tarih: 8 Kasım 2026. Hedef: https://github.com/bigatech/mini-erp . Kullanıcı bu aşamada Azure deploy yapılmamasını istedi. `Deploy` yalnız manuel tetiklenir; push/PR hiçbir Azure kaynağı oluşturmaz.

## Repository ve günlük çalışma

Repository ağacı kök README içindedir. `main` kabul edilmiş kaynak; geliştirmeler `codex/*` dalı + PR ile ilerler. GitHub branch protection/ruleset içinde `Build / build`, `Test / unit`, `Test / integration` kontrollerini zorunlu yapın; doğrudan main push ve force-push kapalı olsun. Ruleset ayarı dosyaların varlığıyla otomatik kurulmuş sayılmaz.

GitHub → Code → Codespaces → Create codespace ile `.devcontainer/devcontainer.json` kullanılır. 4 çekirdek/8 GB önerilir. Post-create komutu dört uygulama/nesne deposu image'ını bulutta derler ve altı servisi başlatır. Port 13000 **private** kalmalıdır; uygulama auth tamamlanana kadar public port açmayın. İlk kaynak build'i birkaç dakika sürer. Günlük geliştirmede yerel Docker/.NET/node gerekmiyor; değişiklikler Codespace içinde commit/push edilir, sonuçlar Actions'ta görülür. Boşta Codespace durdurulmalı; 30 dakika idle timeout ve harcama limiti ayarlanmalıdır.

Cloud-dev overlay PostgreSQL ve MinIO için named volume tutar. Codespace silinirse bu veri kaybolabilir: yalnız sentetik geliştirme verisi kullanın. Kalıcı yedek yerine geçmez. Yerel mevcut `mini-erp-local` projesi/volume'ları değiştirilmez. CI ayrı `minierp-ci` projesidir; her koşuda boş DB başlatır ve yalnız kendi volume'larını temizler.

Public aktarım kapsamı: `src`, `tests`, `deploy` (doldurulmuş env hariç), yeni CI betikleri, yeni bulut kılavuzu, README ve yapılandırmalar. İç iş analizi/proje devir belgeleri, müşteri dosyaları, `sample-data`, `artifacts`, `outputs`, eski yazmalı canlı smoke betikleri aktarılmaz. Bu dosyalar yerelde korunur. `git add .` kullanmayın. Uygulama kaynaklarında mevcut şirket markası korunur; müşteri/personel kaynak satırları aktarım kapsamı değildir.

## Pipeline'lar

| Dosya | Tetikleyici | Davranış |
|---|---|---|
| `.github/workflows/build.yml` | main/develop/codex push, PR, reusable call | .NET 10 restore/build, API/Worker/web Docker build, üç Bicep derlemesi, actionlint |
| `.github/workflows/test.yml` | aynı | Altı test projesi, TRX artifact; ayrı job'da PostgreSQL/Redis/MinIO/API/Worker/web, gerçek API ve UI smoke |
| `.github/workflows/deploy.yml` | yalnız workflow_dispatch, yalnız main | Aynı commit üzerinde Build+Test kapısı; protected Environment; OIDC Azure login; Bicep; ACR image push; iç ingress rollout |

GitHub-hosted Ubuntu runner yeterlidir, self-hosted yerel runner gerekmez. PR işleri cloud secret istemez. Gerçek müşteri kaynak audit testi `MINIERP_SOURCE_AUDIT` olmadan kaynak denetimi yapmaz; 237 unit testin geçmesi gerçek kaynak KPI doğrulaması sayılmaz. Integration job HTTP 200/400/404/409, PostgreSQL yazma/okuma, MinIO PNG yükleme/indirme byte eşitliği, UI veri/hata/iyileşme ve Redis PONG kontrol eder. Mevcut anonim model erişimi smoke çıktısında açık auth boşluğu olarak belirtilir; üretim güvenliği kabulü değildir.

## Container doğrulaması

| Bileşen | Mevcut/yeni durum | Bulut yaklaşımı |
|---|---|---|
| API | .NET 10 multi-stage, UID 1654, 8080, liveness/readiness | ACA Consumption; 1 CPU/2 GiB, şimdilik tek replica; env Production |
| Worker | .NET 10 multi-stage, UID 1654, process health | Mevcut host yalnız heartbeat iskeleti; gerçek iş/queue yok. ACA tanımı 0 replica ile uyur; dashboard warmup hâlâ API'dedir |
| Frontend | React/TS build + Nginx, SPA route fallback | ACA; aynı origin `/api` proxy; runtime `API_UPSTREAM`; build-time sır yok |
| PostgreSQL | Yerelde/CI'da 18.6; CI gerçek protokol ve DB CRUD | Flexible Server Bicep başlangıcı 17; bölgesel sürüm desteği ve 18→17 dump/restore uyumu ayrıca doğrulanacak; otomatik veri taşıma yok |
| Redis | Yerelde/CI'da 8.10.2, PONG | Uygulama bugün Redis kullanmıyor; maliyet için Azure kaynağı oluşturulmaz. İleride Azure Managed Redis + TLS/kimlik ve uygulama adaptörü gerekir |
| MinIO | Yerel mevcut veri RustFS 1.0.4'te; CI/dev ayrı gerçek MinIO | Resmi image çekme başarısızlığı nedeniyle resmi upstream `RELEASE.2025-10-15T17-29-55Z` kaynak build'i; AGPL lisansı. Üretimde bakımı, güvenliği ve kalıcı blok disk/backup'ı yönetilen S3 hizmeti veya ayrı VM gerekir |

MinIO image'ı üçüncü taraf aynadan alınmaz. Sürüm kaynağı: [upstream release](https://github.com/minio/minio/releases/tag/RELEASE.2025-10-15T17-29-55Z). Mevcut RustFS volume'u MinIO'ya mount edilmez; storage formatları aynı sayılmaz. Production S3 sağlayıcısı `us-east-1`, path-style SigV4 ve bucket create/head/get/put desteğini sağlamalıdır; bugünkü adaptör bu sözleşmeyi kullanır. Azure Blob S3 endpoint değildir; onu kullanmak ayrıca adaptör değişikliği ister. Mevcut MinIO kaynak sürümünün kullanım ve bakım kabulü bir üretim kararıdır; CI uygunluğu üretim onayı değildir.

## Ortam ve değişken sözleşmesi

Yerel dosya: mevcut ignored `local.runtime.env`. Cloud-dev: `development.env` (betik kriptografik rastgele değer üretir). CI: tek koşuluk `ci.env`. Production: GitHub Environment metadata + Key Vault; boş `production.env.example` yalnız sözleşmeyi belgeler. Doldurulmuş dosyalar Git ve Docker build context dışında kalır.

| Değişken | Tüketici | Yer |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` | API / Worker | local/dev Development; CI/production Production |
| `ConnectionStrings__Primary` | API | ACA Key Vault secretRef; TLS `SSL Mode=VerifyFull` ile gerçek DB host/CA ayarları |
| `Media__S3Endpoint` | API | ACA runtime; production HTTPS |
| `Media__AccessKey`, `Media__SecretKey` | API | Key Vault secretRef; frontend'e asla verilmez |
| `API_UPSTREAM` | Nginx | ACA iç API FQDN; compose http://api:8080 |
| `VITE_API_URL` | web build | bulutta boş string = same origin; eski local compose açık loopback adresini korur |
| `VITE_APP_ENVIRONMENT` | web build | cloud release Production; dev overlay Development |

`VITE_*` değerleri herkese açık JS içine gömülür, secret olamaz. Production CORS açmak gerekmez; proxy aynı origin kullanır. Nginx API Host başlığını upstream host'a ayarlar; ACA servis yönlendirmesi bunu gerektirir. `/health` uçları altyapı izleme içindir; readiness bugün dependency check kaydetmiyor, yalnız process başlangıcını kanıtlar. İş/API smoke bunun yerine geçmez.

## GitHub Secrets ve Variables

`development` ve `production` GitHub Environments ayrı oluşturulur. Production required reviewers ve deployment branch=main kısıtı konur. Azure federated credential subject tam olarak `repo:bigatech/mini-erp:environment:production` (dev için development), issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange` olmalıdır.

| Environment Secret | İçerik |
|---|---|
| `AZURE_CLIENT_ID` | OIDC uygulama/managed identity client ID |
| `AZURE_TENANT_ID` | tenant ID |
| `AZURE_SUBSCRIPTION_ID` | subscription ID |
| `PRIMARY_CONNECTION_SECRET_URI` | Key Vault içindeki **sürümlü** PostgreSQL bağlantı secret URI |
| `S3_ACCESS_KEY_SECRET_URI` | Key Vault sürümlü S3 access key URI |
| `S3_SECRET_KEY_SECRET_URI` | Key Vault sürümlü S3 secret key URI |

ID ve secret URI teknik olarak gizli anahtar değildir; metadata yayılımını sınırlamak için Environment Secret kullanılır. Gerçek DB şifresi/S3 anahtarları GitHub'a veya ARM parametrelerine verilmez, Key Vault'ta tutulur. `GITHUB_TOKEN` Actions tarafından otomatik sağlanır, ayrıca PAT oluşturmayın. `AZURE_CLIENT_SECRET`, ACR admin password, JWT secret veya hardcoded şifre kullanılmaz.

Environment Variables: `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, `AZURE_NAME_PREFIX` (en fazla 18 küçük harf/rakam/tire), `S3_ENDPOINT`. Örnek prefix `minierp-prod` / `minierp-dev`; ayrı resource group ve ayrı Key Vault kullanılır.

OIDC deploy principal için hedef resource group Contributor + ilk rol atamalarında Role Based Access Control Administrator gerekir; ACR push için AcrPush ayrıca atanır. İlk bootstrap yetkili yönetici tarafından yapılır; rol atamaları tamamlanınca gereksiz geniş RBAC kaldırılır. Runtime identity yalnız AcrPull + kendi vault'unda Key Vault Secrets User alır. Pipeline secret yazmaz, Key Vault admin değildir. ACR admin kapalıdır.

## Azure IaC ve gelecekte deployment adımları

Bu bölüm **plan**, bu görevde çalıştırılmaz. `main.bicep`: ACR Basic, Log Analytics günlük 1 GB ingest kotası (tam maliyet limiti değildir), ACA Consumption ortamı, user-assigned identity, Key Vault + purge protection + roller. `apps.bicep`: üç uygulama, Key Vault refs, iç ingress, kaynak/sayım sınırları. `postgres.bicep`: ayrı opsiyonel Flexible Server B1ms/32 GB/7 gün backup; HA kapalı. Küçük başlangıç içindir, üretim kapasite/HA kabulü değildir.

1. Subscription/bölge/bütçe kesinleşince ayrı RG'leri ve OIDC federation'ı yönetici oluşturur. Budget alert ve maliyet owner'ı belirlenir.
2. `az deployment group what-if -g <RG> -f deploy/azure/main.bicep --parameters namePrefix=<prefix> environmentName=<environment>`; inceleme sonrası `create`. Deployment outputs ACR/env/identity/vault adlarını verir.
3. PostgreSQL şifre parametresi güvenli secret kanalıyla sağlanır; `postgres.bicep` what-if/create ayrı uygulanır. Template hiçbir firewall kuralı açmaz. ACA outbound IP allowlist veya private VNet/DNS tasarımı tamamlanmadan bağlantı çalışmaz. `Allow Azure services` / `0.0.0.0` kuralı eklenmez. Üretim tercihi private network + private DNS; bu repo temel template'i henüz o ağı kurmaz.
4. S3 hizmeti veya MinIO/RustFS VM + managed data disk + TLS + private network + backup ayrı sağlanır. ACA ephemeral diske production DB/MinIO konmaz. Redis mevcut kodda kullanılmadığı için provision edilmez. Bu iki veri servisi otomatik kurulmuş sayılmaz.
5. Yetkili secret yöneticisi üç secret'ı yeni vault'a yazar, sürümlü URI'leri Environments'a tanımlar. İlgili RG OIDC principal'ına ACR AcrPush verir; managed identity RBAC yayılımını bekler.
6. Eski PostgreSQL/object store'un geri yüklenebilir yedeğini alın; ham/staging/kaynak satır izi ve object key/metadata ilişkileri korunarak boş staging ortamında restore provası yapın. GitHub CI müşteri verisi kullanmaz. Restore/hash/row-count ve medya round-trip ayrı kabul ister.
7. Application startup bugün `EnsureCreatedAsync` ile şema oluşturur/günceller; ayrık migration runner ve expand/contract rollback garantisi yoktur. Bu yüzden API tek replica, otomatik production rollout kapalı. Şema migration/backup-restore provası ve eşzamanlı eski/yeni revision uyumluluğu production önkoşuludur. Image rollback DB rollback değildir.
8. Main'de yeşil Build/Test sonrası manuel Deploy → environment seçilir; protected environment kapısı çalışır. Commit SHA tag'li üç image ACR'ye gönderilir. İlk başlatma/key vault/registry rol yayılımında gecikme varsa neden giderilip aynı SHA yeniden çalıştırılır.
9. İç ortamdan API data/error/auth + UI smoke, DB/object storage ve log kabulü yapılır. Hosted runner iç FQDN'e erişemez; `Running` revision kontrolü işlevsel smoke değildir. Ağ içi test runner/ACA Job sonraki release işidir.
10. Önceki SHA'yı yeniden deploy ederek uygulama geri alınabilir; şema değiştiyse yalnız doğrulanmış uyumluluk veya geri yükleme prosedürü uygulanır. Kaynak silme/volume silme deploy akışında yoktur.

## Canlı URL oluşturma planı

1. Bugün çalışan yerel URL `http://localhost:3000`; Codespaces private forwarded 13000 bulut geliştirme URL'sini GitHub üretir.
2. Azure deploy sonrası Bicep `internalWebUrl` döndürür. `external:false` yalnız aynı ACA ortamındaki uygulamalardan erişilebilir; kullanıcıya açık canlı URL değildir. Gerekirse `az containerapp exec` içinden kabul yapılır.
3. ERP-10/11/12 kimlik, rol–eylem, firma/lokasyon kapsamı ve anonim ret testleri tamamlandıktan sonra ayrı review ile web ingress public/HTTPS açılır; API içte kalır. ACA built-in authentication/Entra ID dış kapı olarak eklenebilir, uygulama içi şirket yetkilerinin yerine geçmez.
4. Otomatik `*.azurecontainerapps.io` HTTPS alanı doğrulanır; istenen `erp.<domain>` CNAME ve TXT ownership kaydı eklenir, ACA managed certificate bağlanır. DNS/domain sahipliği henüz yoktur. URL ancak gerçek dağıtım ve tarayıcı/API kabulünden sonra canlı diye raporlanır.

## Maliyet tahmini

Planlama varsayımı: West Europe, USD, 730 saat/ay, düşük trafik, tek ortam, vergi/kur/indirim yok. Aşağıdaki aralıklar bütçe tahminidir, bağlayıcı Azure teklifi değildir; bölge/SKU/gerçek tüketim Azure Calculator ve Retail Prices API ile deployment öncesinde kesinleşmelidir.

| Kalem | Aylık yaklaşık USD | Varsayım |
|---|---:|---|
| GitHub Actions | 0–20 | Public repo standard hosted runner kullanımı; private/plana geçilirse dakika kotası değişir |
| Codespaces | 15–60 | 4-core, ayda yaklaşık 20–60 aktif saat + storage; kişisel dahil kota düşülmeden bütçe |
| ACA API + web | 65–100 | API 1 vCPU/2 GiB sürekli; web 0–1 replica, düşük trafik; worker 0 |
| ACR Basic | 5–8 | Tek registry, küçük image saklama |
| PostgreSQL | 20–45 | B1ms, 32 GB, HA yok; gerçek kapasite ölçülmeli |
| S3 / ayrı MinIO VM + disk | 15–50 | Küçük hizmet/disk; HA ve yoğun çıkış dahil değil |
| Key Vault / logs / egress | 5–25 | Az secret çağrısı ve sınırlı log/çıkış; günlük ingest kotası harcama garantisi değil |
| Azure toplamı | **110–230** | Küçük tek ortam, Redis kapalı; private networking/HA/backup kopyaları ayrıca |
| Geliştirme dahil toplam | **125–310** | İkinci sürekli Azure ortamı ayrıca faturalandırılır |

ACA abonelik başına aylık ilk 180.000 vCPU-s / 360.000 GiB-s ve 2 milyon request ücretsiz kotası toplam kullanımda bir kez uygulanır; app başına çoğaltılmaz. Worker iskeletinin 7/24 boş çalışması engellenir. API warmup ve startup DDL nedeniyle bugün scale-to-zero yapılarak maliyet tasarrufu varsayılmaz. Ekonomik ilk aşama yalnız Codespaces+Actions, Azure kaynak maliyeti **0**; Codespaces/Actions hesabı kendi kullanımıyla faturalandırılır.

Kaynaklar: [ACA billing](https://learn.microsoft.com/azure/container-apps/billing), [ACA pricing](https://azure.microsoft.com/pricing/details/container-apps/), [PostgreSQL pricing](https://azure.microsoft.com/pricing/details/postgresql/flexible-server/), [Codespaces billing](https://docs.github.com/en/billing/concepts/product-billing/github-codespaces), [GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions), [managed identity image pull](https://learn.microsoft.com/azure/container-apps/managed-identity-image-pull), [iç uygulama erişimi](https://learn.microsoft.com/azure/container-apps/connect-apps).

## Açık kabul kapıları

GitHub aktarım ve Actions koşu sonuçları devir kaydına ayrıca yazılır. Azure subscription sağlanmadı; deployment, gerçek Key Vault/RBAC, migration/restore, production auth/firma kapsamı, private ağ, S3 production ve public canlı URL bu görevde yapılmış sayılmaz. Sonraki küçük iş: yeşil GitHub CI üstünden kullanıcı değerlendirmesi; ayrı onay gelmeden yeni ERP modülü/Azure deployment başlatılmaz.
