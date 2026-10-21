package localtestv2

import "altinn.studio/studioctl/internal/localtestv2"

// The names, ports and values the component manifest gives the stack: the tests state what they expect with these, so a
// manifest change that moves one fails a test that names it.

// Component names: the container is the manifest's containerPrefix + name, and `env logs --component <name>` selects it.
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

const (
	volumeCorrespondenceBlobs = "localtest-v2-correspondence-blobs"

	postgresAlias = "postgres"

	certContainerPath = "/certs/" + localtestv2.CertificateFileName
	certSettingEnv    = "AccessTokenSettings__AccessTokenSigningCertificateFullPath"

	frontendDevServerHostPort = "8080"
	healthPath                = "/health"

	// devSourceRoot is the research checkout's localtest folder the dev-build tests use.
	devSourceRoot = "/src/localtest"
)

// The optional component groups.
const (
	groupDialogporten   = "dialogporten"
	groupReceipt        = "receipt"
	groupCorrespondence = "correspondence"
	groupArbeidsflate   = "arbeidsflate"
	groupEverything     = "everything"
)
