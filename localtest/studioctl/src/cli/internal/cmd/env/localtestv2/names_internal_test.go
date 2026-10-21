package localtestv2

import (
	"time"

	"altinn.studio/studioctl/internal/localtestv2"
)

// The names, ports and values the component manifest gives the stack, as the hand-written component table had them:
// the tests state what they expect with these, so a manifest change that moves one fails a test that names it.

// Component names: the container is containerPrefix + name, the dev build context is components/<name> in
// the research checkout, and `env logs --component <name>` selects it.
const (
	componentPostgres         = "postgres"
	componentRegister         = "register"
	componentAuthentication   = "authentication"
	componentAuthorization    = "authorization"
	componentAccessManagement = "access-management"
	componentStorage          = "storage"
	componentEvents           = "events"
	componentResourceRegistry = "resource-registry"
	componentNotifications    = "notifications"
	componentProfile          = "profile"
	componentLocaltest        = "localtest"
	componentPDF              = "pdf3"
	componentWorkflowEngine   = "workflow-engine"

	// The optional Dialogporten group (see groups.go): Redis and the three hosts built from one vendored tree, and the
	// adapter that turns Storage's instances into dialogs, built from its own.
	componentRedis               = "redis"
	componentDialogportenWebApi  = "dialogporten-webapi"
	componentDialogportenGraphQL = "dialogporten-graphql"
	componentDialogportenService = "dialogporten-service"
	componentDialogportenAdapter = "dialogporten-adapter"

	// The optional receipt group: the platform receipt for archived instances.
	componentReceipt = "receipt"

	// The optional Correspondence group (see groups.go), which implies the Dialogporten group.
	componentCorrespondence = "correspondence"

	// The optional Arbeidsflate group (see groups.go), which implies the Dialogporten group: the SPA's nginx, which serves
	// the af.local.altinn.cloud host and proxies /api/ to the BFF, both built from one vendored tree.
	componentArbeidsflate    = "arbeidsflate"
	componentArbeidsflateBFF = "arbeidsflate-bff"
)

// Host ports, loopback only. The proxy in studioctl-server forwards to these; 5300 is also what app-lib's
// PlatformSettings default ApiPdf2Endpoint (http://localhost:5300/pdf) names.
const (
	hostPortRegister         = "8020"
	hostPortAuthentication   = "8021"
	hostPortAuthorization    = "8022"
	hostPortAccessManagement = "8023"
	hostPortStorage          = "8024"
	hostPortEvents           = "8025"
	hostPortResourceRegistry = "8027"
	hostPortLocaltest        = "8026"
	hostPortWorkflowEngine   = "8028"
	hostPortDialogportenWeb  = "8030"
	hostPortDialogportenGQL  = "8031"
	hostPortDialogportenSvc  = "8032"
	hostPortDialogportenAdpt = "8035"
	hostPortNotifications    = "8029"
	hostPortReceipt          = "8034"
	hostPortProfile          = "8036"
	hostPortCorrespondence   = "8033"
	hostPortArbeidsflate     = "8037"
	hostPortArbeidsflateBFF  = "8038"
	hostPortPDF              = "5300"
)

// Named volumes. Each is owned by the image user that writes it (blobs uid 1654, the policy store uid 1000,
// localtest data uid 1654), which the runtime arranges on first use.
const (
	volumePostgresData  = "localtest-v2-postgres-data"
	volumeStorageBlobs  = "localtest-v2-storage-blobs"
	volumePolicyStore   = "localtest-v2-policy-store"
	volumeLocaltestData = "localtest-v2-localtest-data"
	// volumeCorrespondenceBlobs is Correspondence's file-backed blob store (uid of the image's dotnet user).
	volumeCorrespondenceBlobs = "localtest-v2-correspondence-blobs"
)

const (
	// networkName is the localtest-v2 container network. Containers find Postgres on it; everything else
	// they reach through the public origin on the host.
	networkName     = "localtest-v2_network"
	containerPrefix = "localtest-v2-"

	postgresAlias    = "postgres"
	postgresUser     = "postgres"
	postgresPassword = "postgres"
	postgresPort     = "5432"

	postgresHealthInterval    = 2 * time.Second
	postgresHealthTimeout     = 5 * time.Second
	postgresHealthRetries     = 30
	postgresHealthStartPeriod = 5 * time.Second

	dialogportenHealthInterval    = 2 * time.Second
	dialogportenHealthTimeout     = 5 * time.Second
	dialogportenHealthRetries     = 60
	dialogportenHealthStartPeriod = 5 * time.Second

	redisHealthInterval = 2 * time.Second
	redisHealthTimeout  = 3 * time.Second
	redisHealthRetries  = 15

	certContainerDir  = "/certs"
	certContainerPath = certContainerDir + "/" + localtestv2.CertificateFileName
	certSettingEnv    = "AccessTokenSettings__AccessTokenSigningCertificateFullPath"

	// Containers reach the public origin, served by studioctl-server on the host, through the runtime's host
	// gateway (hostGateway maps every ingress hostname to it: the PDF generator's browser loads the app's
	// frontend assets from app-frontend.local.altinn.cloud when `studioctl run --dev-frontend` serves them).
	// Events also delivers webhooks to the developer's machine by the conventional name.
	hostDockerInternalEntry = "host.docker.internal:" + hostGateway

	frontendDevServerHostPort = "8080"
	healthPath                = "/health"

	redisPort = "6379"
	// dialogportenPort is the port the .NET images listen on (ASPNETCORE_HTTP_PORTS), all three hosts.
	dialogportenPort = "8080"
	// dialogportenHealthPath is the Altinn health convention Dialogporten's hosts serve: ready once the database is
	// migrated and the hosts' own checks pass. The WebApi serves it under its path base too.
	dialogportenHealthPath = "/health/readiness"

	// arbeidsflatePort is the port both Arbeidsflate images listen on (nginx, and the BFF's PORT).
	arbeidsflatePort = "80"
	// arbeidsflateBFFAlias is the BFF's name on the network, which the SPA's nginx proxies /api/ to (nginx/nginx.conf).
	arbeidsflateBFFAlias = "arbeidsflate-bff"
	// arbeidsflateOIDCClient is the OIDC client Authentication seeds for Arbeidsflate's BFF.
	arbeidsflateOIDCClient = "arbeidsflate-localtest"
	// arbeidsflateRedisDatabase is the BFF's database in the Dialogporten group's Redis: Dialogporten uses 0 and
	// Correspondence 1.
	arbeidsflateRedisDatabase = "2"
)

// The optional component groups.
const (
	groupDialogporten   = "dialogporten"
	groupReceipt        = "receipt"
	groupCorrespondence = "correspondence"
	groupArbeidsflate   = "arbeidsflate"
	groupEverything     = "everything"
)
