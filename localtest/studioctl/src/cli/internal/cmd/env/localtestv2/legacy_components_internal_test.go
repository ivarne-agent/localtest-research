package localtestv2

import (
	"maps"
	"path/filepath"
	"slices"

	"altinn.studio/devenv/pkg/container/types"
	"altinn.studio/devenv/pkg/resource"
	"altinn.studio/studioctl/internal/config"
	"altinn.studio/studioctl/internal/envtopology"
	"altinn.studio/studioctl/internal/localtestv2"
)

// The hand-written component table the component manifest replaced, kept until the transition test
// (TestTheManifestLoaderReproducesTheHandWrittenTable) has shown that the loader builds the same environment.

func legacyPostgresComponent(opts *Options) component {
	c := legacyNewComponent(opts, componentPostgres, legacyLocaltestV2Image(opts, "postgres"), ".", postgresPort, "")
	c.env = map[string]string{
		"POSTGRES_USER":     postgresUser,
		"POSTGRES_PASSWORD": postgresPassword,
		// UTC, as production's Postgres: upstream column defaults such as Events' and Profile's
		// `now() at time zone 'utc'` are only right when the session time zone is UTC.
		"TZ": "UTC",
	}
	// initdb writes TZ into postgresql.conf of a new data volume only; -c also sets UTC on a volume created under the
	// earlier Europe/Oslo setting. The first arguments repeat the image's CMD (postgres/Dockerfile).
	c.command = []string{
		"postgres",
		"-c", "shared_preload_libraries=pg_stat_statements",
		"-c", "timezone=UTC",
		"-c", "log_timezone=UTC",
	}
	c.volumes = []types.VolumeMount{namedVolume(volumePostgresData, "/var/lib/postgresql")}
	c.aliases = []string{postgresAlias}
	c.extraHosts = nil
	c.healthCheck = &types.HealthCheck{
		Test:        []string{"CMD-SHELL", "pg_isready -h 127.0.0.1 -p " + postgresPort + " -U " + postgresUser},
		Interval:    postgresHealthInterval,
		Timeout:     postgresHealthTimeout,
		Retries:     postgresHealthRetries,
		StartPeriod: postgresHealthStartPeriod,
	}
	return c
}

func legacyRegisterComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentRegister,
		legacyLocaltestV2Image(opts, "register"),
		"components/register",
		"5020",
		hostPortRegister,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv: certContainerPath,
		"GeneralSettings__OpenIdWellKnownEndpoint":   platform + "authentication/api/v1/openid/",
		"PlatformSettings__ApiAuthorizationEndpoint": platform + "authorization/api/v1/",
	}
	c.volumes = []types.VolumeMount{legacyCertMount(opts)}
	appHost := opts.Topology.AppHostName()
	c.routes = []route{{host: appHost, prefix: "/register"}}
	return c
}

func legacyAuthenticationComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentAuthentication,
		legacyLocaltestV2Image(opts, "authentication"),
		"components/authentication",
		"5101",
		hostPortAuthentication,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv:                      certContainerPath,
		"GeneralSettings__PlatformEndpoint": platform,
	}
	c.volumes = []types.VolumeMount{legacyCertMount(opts)}
	// Login resolves the user through Profile's internal/user.
	c.deps = []string{legacyContainerOf(componentRegister), legacyContainerOf(componentProfile)}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/authentication"}}
	return c
}

func legacyAuthorizationComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentAuthorization,
		legacyLocaltestV2Image(opts, "authorization"),
		"components/authorization",
		"5070",
		hostPortAuthorization,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv: certContainerPath,
		"Localtest__Authorization__PlatformEndpoint": platform,
	}
	c.volumes = []types.VolumeMount{legacyCertMount(opts), namedVolume(volumePolicyStore, "/policy-store")}
	// The PDP reads the subject's profile through Profile's internal/user.
	c.deps = []string{legacyContainerOf(componentRegister), legacyContainerOf(componentProfile)}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/authorization"}}
	return c
}

func legacyStorageComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentStorage,
		legacyLocaltestV2Image(opts, "storage"),
		"components/storage",
		"5010",
		hostPortStorage,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv:                      certContainerPath,
		"GeneralSettings__PlatformEndpoint": platform,
		"PostgreSqlSettings__AdminConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_storage_admin;Password={0};Database=storagedb",
		"PostgreSqlSettings__ConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_storage;Password={0};Database=storagedb;Include Error Detail=True",
	}
	c.volumes = []types.VolumeMount{legacyCertMount(opts), namedVolume(volumeStorageBlobs, "/data/blobs")}
	c.deps = []string{legacyContainerOf(componentPostgres)}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/storage"}}
	return c
}

func legacyAccessManagementComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentAccessManagement,
		legacyLocaltestV2Image(opts, "access-management"),
		"components/access-management",
		"5080",
		hostPortAccessManagement,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv: certContainerPath,
		"Localtest__AccessManagement__PlatformEndpoint": platform,
	}
	c.volumes = []types.VolumeMount{legacyCertMount(opts), namedVolume(volumePolicyStore, "/policy-store")}
	// Its startup refresh reads the Register's streams and the Resource Registry's providers, resources and updated feed;
	// its Profile client reads users through internal/user.
	c.deps = []string{
		legacyContainerOf(componentPostgres),
		legacyContainerOf(componentRegister),
		legacyContainerOf(componentResourceRegistry),
		legacyContainerOf(componentProfile),
	}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/accessmanagement"}}
	return c
}

func legacyEventsComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentEvents,
		legacyLocaltestV2Image(opts, "events"),
		"components/events",
		"5080",
		hostPortEvents,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv:                        certContainerPath,
		"Localtest__Events__PlatformEndpoint": platform,
		"PostgreSQLSettings__AdminConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_events_admin;Password={0};Database=eventsdb",
		"PostgreSQLSettings__ConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_events;Password={0};Database=eventsdb;Include Error Detail=True",
	}
	// Events never binds AccessTokenSettings for its outgoing platform tokens, so its production
	// SigningCredentialsResolver reads the default path relative to /app.
	eventsCert := readOnlyBind(
		filepath.Join(opts.CertDir, localtestv2.CertificateFileName),
		"/accesstoken/accesstokencredentials.pfx",
	)
	c.volumes = []types.VolumeMount{legacyCertMount(opts), eventsCert}
	c.extraHosts = append(ingressHostEntries(opts), hostDockerInternalEntry)
	c.deps = []string{
		legacyContainerOf(componentPostgres),
		legacyContainerOf(componentRegister),
		legacyContainerOf(componentAuthorization),
	}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/events"}}
	return c
}

func legacyResourceRegistryComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentResourceRegistry,
		legacyLocaltestV2Image(opts, "resource-registry"),
		"components/resource-registry",
		"5100",
		hostPortResourceRegistry,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv: certContainerPath,
		"Localtest__ResourceRegistry__PlatformEndpoint": platform,
		"PostgreSQLSettings__AdminConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_authorization_admin;Password={0};Database=resourceregistrydb",
		"PostgreSQLSettings__ConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_authorization;Password={0};Database=resourceregistrydb;Include Error Detail=True",
	}
	// App policies are read where the PDP's policy endpoint writes them; resource policies are written there.
	c.volumes = []types.VolumeMount{legacyCertMount(opts), namedVolume(volumePolicyStore, "/policy-store")}
	// App resources come from Storage's application list.
	c.deps = []string{legacyContainerOf(componentPostgres), legacyContainerOf(componentStorage)}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/resourceregistry"}}
	return c
}

func legacyNotificationsComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentNotifications,
		legacyLocaltestV2Image(opts, "notifications"),
		"components/notifications",
		"5090",
		hostPortNotifications,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv: certContainerPath,
		"Localtest__Notifications__PlatformEndpoint": platform,
		"PostgreSQLSettings__AdminConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_notifications_admin;Password={0};Database=notificationsdb",
		"PostgreSQLSettings__ConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_notifications;Password={0};Database=notificationsdb;Include Error Detail=True",
	}
	// The platform-token generator (to Register and the PDP) reads its default path relative to /app, as Events'.
	notificationsCert := readOnlyBind(
		filepath.Join(opts.CertDir, localtestv2.CertificateFileName),
		"/accesstoken/accesstokencredentials.pfx",
	)
	c.volumes = []types.VolumeMount{legacyCertMount(opts), notificationsCert}
	// Orders call Profile, Register, the PDP, and the apps' condition endpoints through the public origin (the default
	// extraHosts map every ingress hostname to the host gateway).
	c.deps = []string{
		legacyContainerOf(componentPostgres),
		legacyContainerOf(componentRegister),
		legacyContainerOf(componentProfile),
		legacyContainerOf(componentAuthorization),
		legacyContainerOf(componentAuthentication),
	}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/notifications"}}
	return c
}

// profileComponent is production Altinn Profile on its own database: the user profiles apps, Authentication, Authorization
// and Access Management read, the contact point lookups of Notifications and Correspondence, and Arbeidsflate's profile
// endpoints. It migrates its database and seeds itself from the Register's localtest API at start (contact details through
// its KRR sync, organisations' official addresses through its brreg sync, profile settings), so the Register comes first.
// The PDP, Authentication's OpenID metadata and Notifications are request-time dependencies only; they list Profile among
// their deps (Authentication and the PDP call it on every login), which rules out the reverse.
func legacyProfileComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentProfile,
		legacyLocaltestV2Image(opts, "profile"),
		"components/profile",
		"5030",
		hostPortProfile,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		certSettingEnv:                         certContainerPath,
		"Localtest__Profile__PlatformEndpoint": platform,
		"PostgreSqlSettings__AdminConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_profile_admin;Password={0};Database=profiledb",
		"PostgreSqlSettings__ConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Username=platform_profile;Password={0};Database=profiledb;Include Error Detail=True",
	}
	c.volumes = []types.VolumeMount{legacyCertMount(opts)}
	c.deps = []string{legacyContainerOf(componentPostgres), legacyContainerOf(componentRegister)}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/profile"}}
	return c
}

func legacyLocaltestComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentLocaltest,
		legacyLocaltestV2Image(opts, "localtest"),
		"localtest",
		"8000",
		hostPortLocaltest,
	)
	c.healthPath = healthPath
	c.env = map[string]string{
		"Localtest__PlatformEndpoint":           platform,
		"Localtest__AccessTokenCertificatePath": certContainerPath,
		// Its test-user picker and token helpers call the Register and Authentication through the public
		// origin, as everything else does (the app's own proxy is not used here).
		"Proxy__Upstreams__register":       opts.Topology.LocaltestBaseURL(),
		"Proxy__Upstreams__authentication": opts.Topology.LocaltestBaseURL(),
	}
	c.volumes = []types.VolumeMount{legacyCertMount(opts), namedVolume(volumeLocaltestData, "/data")}
	// Everything the prefixes above do not claim: /, /Home, /localtest/api, /app-frontend.
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: ""}}
	return c
}

func legacyPdfComponent(opts *Options) component {
	c := legacyNewComponent(opts, componentPDF, opts.Images.Core.PDF3, "", "5031", hostPortPDF)
	c.healthPath = "/health/ready"
	c.env = map[string]string{
		"TZ":                             "Europe/Oslo",
		"PDF3_ENVIRONMENT":               "localtest",
		"PDF3_QUEUE_SIZE":                "3",
		"PDF3_LOCALTEST_PUBLIC_BASE_URL": opts.Topology.LocaltestBaseURL(),
	}
	c.routes = []route{{host: opts.Topology.PDFHostName(), prefix: ""}}
	return c
}

func legacyWorkflowEngineComponent(opts *Options) component {
	c := legacyNewComponent(
		opts,
		componentWorkflowEngine,
		opts.Images.Core.WorkflowEngine,
		"",
		"9090",
		hostPortWorkflowEngine,
	)
	c.healthPath = "/api/v1/health/ready"
	c.env = map[string]string{
		"ASPNETCORE_ENVIRONMENT": "Development",
		"ASPNETCORE_HTTP_PORTS":  "9090",
		// No collector runs in this environment; the default exporter would fail against localhost:4317.
		"EngineSettings__EnableTelemetry": "false",
		"ConnectionStrings__WorkflowEngine": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Database=workflow_engine;Username=" + postgresUser + ";Password=" + postgresPassword,
		"AppCommandSettings__CommandEndpoint": opts.Topology.LocaltestBaseURL() +
			"/{Org}/{App}/instances/{InstanceOwnerPartyId}/{InstanceGuid}/workflow-engine-callbacks/",
	}
	c.deps = []string{legacyContainerOf(componentPostgres)}
	c.routes = []route{{host: opts.Topology.WorkflowEngineHostName(), prefix: ""}}
	return c
}

// redisComponent is the cache, backplane and subscription transport of Dialogporten's three hosts (see the component
// README for why it stays). No persistence and no published port: only the hosts on the network use it.
func legacyRedisComponent(opts *Options) component {
	c := legacyNewComponent(opts, componentRedis, legacyLocaltestV2Image(opts, "redis"), "", redisPort, "")
	c.group = groupDialogporten
	c.command = []string{"redis-server", "--save", "", "--appendonly", "no"}
	c.aliases = []string{componentRedis}
	c.extraHosts = nil
	c.healthCheck = &types.HealthCheck{
		Test:        []string{"CMD", "redis-cli", "ping"},
		Interval:    redisHealthInterval,
		Timeout:     redisHealthTimeout,
		Retries:     redisHealthRetries,
		StartPeriod: 0,
	}
	return c
}

// dialogportenComponent is what the three hosts share: the platform endpoint the localtest hook derives every other
// setting from (configuration defaults, see the component README), the database and Redis on the network, and a build
// from the component folder with one Dockerfile per host.
func legacyDialogportenComponent(
	opts *Options,
	platform, name, dockerfile string,
	image config.ImageSpec,
	hostPort string,
) component {
	c := legacyNewComponent(opts, name, image, "components/dialogporten", dialogportenPort, hostPort)
	c.group = groupDialogporten
	c.dockerfile = dockerfile
	c.healthPath = dialogportenHealthPath
	c.env = map[string]string{"Localtest__Dialogporten__PlatformEndpoint": platform}
	return c
}

func legacyDialogportenWebApiComponent(opts *Options, platform string) component {
	c := legacyDialogportenComponent(
		opts,
		platform,
		componentDialogportenWebApi,
		"Dockerfile.webapi",
		legacyLocaltestV2Image(opts, "dialogporten-webapi"),
		hostPortDialogportenWeb,
	)
	// It migrates the database at start (production's DevelopmentMigratorHostedService in Development, before the host
	// listens), and so does each of the others, so a second host starting at the same time makes the migrations collide.
	// A container health check, which the runtime waits for before it starts what depends on it, keeps them apart. The
	// aspnet image has no curl or wget, so the check is a GET over bash's /dev/tcp.
	c.healthCheck = &types.HealthCheck{
		// The runtime hands the string to a shell (dash, which has no /dev/tcp), hence the explicit bash.
		Test: []string{
			"CMD-SHELL",
			`bash -c "exec 3<>/dev/tcp/127.0.0.1/` + dialogportenPort + ` || exit 1; ` +
				`printf 'GET ` + dialogportenHealthPath + ` HTTP/1.0\r\nHost: localhost\r\n\r\n' >&3 || exit 1; ` +
				`head -n 1 <&3 | grep -q ' 200 '"`,
		},
		Interval:    dialogportenHealthInterval,
		Timeout:     dialogportenHealthTimeout,
		Retries:     dialogportenHealthRetries,
		StartPeriod: dialogportenHealthStartPeriod,
	}
	// Its first authorization and party lookups need the platform services, which start before it.
	c.deps = []string{
		legacyContainerOf(componentPostgres),
		legacyContainerOf(componentRedis),
		legacyContainerOf(componentRegister),
		legacyContainerOf(componentAuthentication),
		legacyContainerOf(componentAuthorization),
		legacyContainerOf(componentAccessManagement),
		legacyContainerOf(componentEvents),
		legacyContainerOf(componentResourceRegistry),
	}
	// The host serves everything under UsePathBase("/dialogporten"), so the path passes unchanged. The GraphQL route
	// below is longer and wins for its own prefix.
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/dialogporten", rewrite: ""}}
	return c
}

func legacyDialogportenGraphQLComponent(opts *Options, platform string) component {
	c := legacyDialogportenComponent(
		opts,
		platform,
		componentDialogportenGraphQL,
		"Dockerfile.graphql",
		legacyLocaltestV2Image(opts, "dialogporten-graphql"),
		hostPortDialogportenGQL,
	)
	c.deps = []string{legacyContainerOf(componentDialogportenWebApi)}
	// The host serves /graphql with no path base; APIM maps /dialogporten/graphql to it in production, the proxy does here.
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/dialogporten/graphql", rewrite: "/graphql"}}
	return c
}

func legacyDialogportenServiceComponent(opts *Options, platform string) component {
	c := legacyDialogportenComponent(
		opts,
		platform,
		componentDialogportenService,
		"Dockerfile.service",
		legacyLocaltestV2Image(opts, "dialogporten-service"),
		hostPortDialogportenSvc,
	)
	c.deps = []string{legacyContainerOf(componentDialogportenWebApi)}
	// No public route: it consumes the in-process bus and publishes to the other hosts through Redis.
	return c
}

// dialogportenAdapterComponent is the Dialogporten adapter: it takes Storage's outbox commands on its anonymous
// localtest enqueue endpoint (Storage posts them through the proxy whether or not the group is up) and syncs each
// instance to a dialog with the production code, calling Storage, Register and Dialogporten through the public origin.
func legacyDialogportenAdapterComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentDialogportenAdapter,
		legacyLocaltestV2Image(opts, "dialogporten-adapter"),
		"components/dialogporten-adapter",
		"5011",
		hostPortDialogportenAdpt,
	)
	c.group = groupDialogporten
	c.healthPath = healthPath
	c.env = map[string]string{"Localtest__Adapter__PlatformEndpoint": platform}
	c.deps = []string{
		legacyContainerOf(componentStorage),
		legacyContainerOf(componentRegister),
		legacyContainerOf(componentAuthentication),
		legacyContainerOf(componentDialogportenWebApi),
	}
	// Its routes start with /storage/dialogporten, so the path passes unchanged. The proxy matches the longest prefix,
	// so this route wins over Storage's /storage for the adapter's paths.
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/storage/dialogporten", rewrite: ""}}
	return c
}

// receiptComponent is the platform receipt (`/receipt/{instanceOwnerPartyId}/{instanceGuid}`), the target of archived
// instances' links. It is stateless: it reads the instance from Storage, the owner from Register and the user from
// Profile with the user's cookie, and is only the sign-in target and JWKS user of Authentication. Every endpoint is the
// public origin, because the browser follows the sign-in redirect and the attachment links the receipt hands out.
func legacyReceiptComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentReceipt,
		legacyLocaltestV2Image(opts, "receipt"),
		"components/receipt",
		"5060",
		hostPortReceipt,
	)
	c.group = groupReceipt
	c.healthPath = healthPath
	c.env = map[string]string{
		"GeneralSettings__OpenIdWellKnownEndpoint": platform + "authentication/api/v1/openid/",
		// Storage reports attachment links on https://platform.{this host}/, which the component's fake rewrites to
		// Localtest__Receipt__PlatformEndpoint.
		"GeneralSettings__Hostname":                   opts.Topology.AppHostName(),
		"PlatformSettings__ApiAuthenticationEndpoint": platform + "authentication/api/v1/",
		"PlatformSettings__ApiProfileEndpoint":        platform + "profile/api/v1/",
		"PlatformSettings__ApiRegisterEndpoint":       platform + "register/api/v1/",
		"PlatformSettings__ApiStorageEndpoint":        platform + "storage/api/v1/",
		"Localtest__Receipt__PlatformEndpoint":        platform,
	}
	// It validates no platform tokens; its generator (to Register and Profile) reads the default path relative to
	// /app, as Events' and Notifications' do.
	c.volumes = []types.VolumeMount{readOnlyBind(
		filepath.Join(opts.CertDir, localtestv2.CertificateFileName),
		"/accesstoken/accesstokencredentials.pfx",
	)}
	// Request-time dependencies only: nothing is read at start (the OpenID metadata on the first cookie).
	c.deps = []string{
		legacyContainerOf(componentRegister),
		legacyContainerOf(componentProfile),
		legacyContainerOf(componentAuthentication),
		legacyContainerOf(componentStorage),
	}
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/receipt", rewrite: ""}}
	return c
}

// correspondenceComponent is production Altinn Correspondence (the API host, Hangfire inside it) on its own database. It
// cannot publish without a Dialogporten dialog and connects to Redis eagerly at start, so its group implies Dialogporten's
// and it shares that Redis (database 1; Dialogporten uses 0). Everything but the database and Redis it reaches through the
// public origin; the localtest hook derives those URLs from the platform endpoint (see the component README).
func legacyCorrespondenceComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentCorrespondence,
		legacyLocaltestV2Image(opts, "correspondence"),
		"components/correspondence",
		"2525",
		hostPortCorrespondence,
	)
	c.group = groupCorrespondence
	// /health is the database check (CanConnectAsync); /healthz is liveness only.
	c.healthPath = healthPath
	c.env = map[string]string{
		"Localtest__Correspondence__PlatformEndpoint": platform,
		"DatabaseOptions__ConnectionString": "Host=" + postgresAlias + ";Port=" + postgresPort +
			";Database=correspondence;Username=correspondence;Password=localtest",
		"GeneralSettings__RedisConnectionString": componentRedis + ":" + redisPort + ",defaultDatabase=1",
	}
	c.volumes = []types.VolumeMount{namedVolume(volumeCorrespondenceBlobs, "/mnt/storage")}
	// It validates dialog tokens against Dialogporten's JWKS and creates dialogs from its first job, so the WebApi (healthy,
	// migrated) comes first; the platform services it calls start before it too.
	c.deps = []string{
		legacyContainerOf(componentPostgres),
		legacyContainerOf(componentRegister),
		legacyContainerOf(componentProfile),
		legacyContainerOf(componentAuthorization),
		legacyContainerOf(componentResourceRegistry),
		legacyContainerOf(componentNotifications),
		legacyContainerOf(componentEvents),
		legacyContainerOf(componentRedis),
		legacyContainerOf(componentDialogportenWebApi),
	}
	// The host's own paths start with /correspondence/api/v1, so nothing is rewritten.
	c.routes = []route{{host: opts.Topology.AppHostName(), prefix: "/correspondence", rewrite: ""}}
	return c
}

// arbeidsflateComponent is the SPA of Arbeidsflate, the Altinn inbox, served by nginx on its own host,
// af.local.altinn.cloud: the SPA and the BFF use absolute paths (/, /assets, /api/...), so they cannot live under a
// prefix of the platform host. nginx proxies /api/ to the BFF with X-Forwarded-Proto https, without which the BFF's
// Secure-only session is never saved (see the component README). The browser opens Dialogporten's subscription stream
// on the platform host directly, as in production.
func legacyArbeidsflateComponent(opts *Options) component {
	c := legacyNewComponent(
		opts,
		componentArbeidsflate,
		legacyLocaltestV2Image(opts, "arbeidsflate"),
		"components/arbeidsflate",
		arbeidsflatePort,
		hostPortArbeidsflate,
	)
	c.group = groupArbeidsflate
	c.dockerfile = "Dockerfile.frontend"
	// The SPA's index.html; /api/ would make the BFF's readiness the SPA's, which is polled on its own.
	c.healthPath = "/"
	// Upstream's start.sh writes these into index.html (envsubst); the two Application Insights values stay empty.
	c.env = map[string]string{
		"DIALOGPORTEN_STREAM_URL": opts.Topology.LocaltestBaseURL() + "/dialogporten/graphql/stream",
	}
	// nginx resolves the BFF's alias when it starts.
	c.deps = []string{legacyContainerOf(componentArbeidsflateBFF)}
	c.routes = []route{{host: opts.Topology.ArbeidsflateHostName(), prefix: "", rewrite: ""}}
	return c
}

// arbeidsflateBFFComponent is Arbeidsflate's backend for frontend: the OIDC client of production Authentication (the
// seeded client arbeidsflate-localtest, whose redirect, post-logout and front-channel URLs are on the Arbeidsflate host),
// the GraphQL proxy to Dialogporten with the user's access token, Profile for the profile page, and its own Postgres
// database and Redis database. It has no public route of its own: the browser reaches it through the SPA's nginx.
func legacyArbeidsflateBFFComponent(opts *Options, platform string) component {
	c := legacyNewComponent(
		opts,
		componentArbeidsflateBFF,
		legacyLocaltestV2Image(opts, "arbeidsflate-bff"),
		"components/arbeidsflate",
		arbeidsflatePort,
		hostPortArbeidsflateBFF,
	)
	c.group = groupArbeidsflate
	c.dockerfile = "Dockerfile.bff"
	// Answers once the server listens, which is after the migrations and the database connection.
	c.healthPath = "/api/readiness"
	c.aliases = []string{arbeidsflateBFFAlias}
	base := opts.Topology.LocaltestBaseURL()
	c.env = map[string]string{
		// redirect_uri is HOSTNAME + /api/cb, which must equal the seeded client's byte for byte.
		"HOSTNAME": opts.Topology.PublicBaseURL(envtopology.ComponentArbeidsflate),
		// With a scheme, which patch 0001 keeps (upstream prefixes https://).
		"OIDC_PLATFORM_URL": platform + "authentication/api/v1/openid",
		// The client Authentication seeds for it (components/authentication, LocaltestOidcClients); its secret is the
		// client id with "-secret".
		"OIDC_CLIENT_ID":     arbeidsflateOIDCClient,
		"OIDC_CLIENT_SECRET": arbeidsflateOIDCClient + "-secret",
		// Profile, Register, Correspondence, Notifications, the Resource Registry and the Maskinporten exchange.
		"PLATFORM_BASEURL": base,
		// /graphql and /health under it; the front door forwards /dialogporten/graphql to the GraphQL host's /graphql.
		"DIALOGPORTEN_URL": base + "/dialogporten",
		"DB_CONNECTION_STRING": "postgres://arbeidsflate:localtest@" + postgresAlias + ":" + postgresPort +
			"/arbeidsflate",
		"REDIS_CONNECTION_STRING": "redis://" + componentRedis + ":" + redisPort + "/" + arbeidsflateRedisDatabase,
		"SESSION_SECRET":          "localtest-arbeidsflate-session-secret-not-a-secret",
		// The domain of the cookies the BFF writes for the apps (the language in altinnPersistentContext).
		"AUTH_CONTEXT_COOKIE_DOMAIN": opts.Topology.AppHostName(),
		"ENVIRONMENT":                "test",
		"NODE_ENV":                   "production",
		// Upstream enables both everywhere but production: GraphiQL at /api/graphiql with the browser's session, and
		// /api/init-session for tests that bring a token instead of signing in.
		"ENABLE_GRAPHIQL":              "true",
		"ENABLE_INIT_SESSION_ENDPOINT": "true",
		// The localtest app's fake Maskinporten (the BFF posts to {issuer}token); a nine-digit client id is the
		// organisation number (Digdir's). The image's start script supplies a client key when MASKINPORTEN_JWK is unset.
		"MASKINPORTEN_ISSUER":    platform + "localtest/api/maskinporten/",
		"MASKINPORTEN_CLIENT_ID": "991825827",
	}
	// Its start reads Authentication's discovery (twice) and connects to Postgres and Redis; the GraphQL proxy, the
	// health check and the profile page need Dialogporten, Profile and the Register.
	c.deps = []string{
		legacyContainerOf(componentPostgres),
		legacyContainerOf(componentRedis),
		legacyContainerOf(componentRegister),
		legacyContainerOf(componentProfile),
		legacyContainerOf(componentAuthentication),
		legacyContainerOf(componentDialogportenGraphQL),
	}
	return c
}

// allComponents is every component, whether or not its group is enabled; teardown uses it so that nothing a previous
// start left behind is orphaned.
func legacyAllComponents(opts *Options) []component {
	platform := opts.Topology.LocaltestBaseURL() + "/"
	return []component{
		legacyPostgresComponent(opts),
		legacyRegisterComponent(opts, platform),
		legacyProfileComponent(opts, platform),
		legacyAuthenticationComponent(opts, platform),
		legacyAuthorizationComponent(opts, platform),
		legacyStorageComponent(opts, platform),
		legacyAccessManagementComponent(opts, platform),
		legacyEventsComponent(opts, platform),
		legacyResourceRegistryComponent(opts, platform),
		legacyNotificationsComponent(opts, platform),
		legacyLocaltestComponent(opts, platform),
		legacyPdfComponent(opts),
		legacyWorkflowEngineComponent(opts),
		legacyRedisComponent(opts),
		legacyDialogportenWebApiComponent(opts, platform),
		legacyDialogportenGraphQLComponent(opts, platform),
		legacyDialogportenServiceComponent(opts, platform),
		legacyDialogportenAdapterComponent(opts, platform),
		legacyReceiptComponent(opts, platform),
		legacyCorrespondenceComponent(opts, platform),
		legacyArbeidsflateBFFComponent(opts, platform),
		legacyArbeidsflateComponent(opts),
	}
}

// newComponent is a service with its own image user and no host-writable mounts, which every component is.
func legacyNewComponent(
	opts *Options,
	name string,
	image config.ImageSpec,
	devContext, containerPort, hostPort string,
) component {
	return component{
		healthCheck:    nil,
		env:            nil,
		name:           name,
		group:          "",
		image:          image,
		dockerfile:     "",
		command:        nil,
		devContext:     devContext,
		containerPort:  containerPort,
		hostPort:       hostPort,
		healthPath:     "",
		volumes:        nil,
		extraHosts:     ingressHostEntries(opts),
		aliases:        nil,
		deps:           nil,
		routes:         nil,
		useDefaultUser: true,
		devSelector:    "",
	}
}

// localtestV2Image is the named localtest-v2 image from the image configuration.
func legacyLocaltestV2Image(opts *Options, name string) config.ImageSpec {
	spec, _ := opts.Images.LocaltestV2.Image(name)
	return spec
}

func legacyContainerOf(name string) string {
	return containerPrefix + name
}

func legacyCertMount(opts *Options) types.VolumeMount {
	return readOnlyBind(opts.CertDir, certContainerDir)
}

func legacyComponentImage(opts *Options, c component) resource.ImageResource {
	if opts.DevComponents[legacyDevName(c)] && c.devContext != "" && opts.DevSourceRoot != "" {
		buildContext := filepath.Join(opts.DevSourceRoot, c.devContext)
		dockerfile := filepath.Join(buildContext, "Dockerfile")
		if c.dockerfile != "" {
			dockerfile = filepath.Join(buildContext, c.dockerfile)
		}
		if c.name == componentPostgres {
			dockerfile = filepath.Join(opts.DevSourceRoot, "postgres", "Dockerfile")
		}
		return &resource.BuiltImage{
			Enabled:     nil,
			ContextPath: filepath.ToSlash(buildContext),
			Dockerfile:  filepath.ToSlash(dockerfile),
			Build:       types.BuildOptions{CacheFrom: nil, CacheTo: nil},
			Tag:         containerPrefix + c.name + ":dev",
		}
	}
	policy := resource.PullIfNotPresent
	if c.image.Floating {
		policy = resource.PullAlwaysAllowStale
	}
	return &resource.PulledImage{Enabled: nil, Ref: c.image.Ref(), PullPolicy: policy}
}

// devName is what `--dev` calls a component: the group's name for one built from the group's own folder together with
// the rest of it (Dialogporten's three hosts), otherwise its own name (the Dialogporten adapter).
func legacyDevName(c component) string {
	if c.group != "" && c.devContext == "components/"+c.group {
		return c.group
	}
	return c.name
}

// legacyResources is resources over the hand-written table: its components and dev builds, the shared container spec.
func legacyResources(opts *Options) []resource.Resource {
	network := &resource.Network{
		Enabled:   nil,
		Name:      networkName,
		Driver:    "bridge",
		Labels:    nil,
		Lifecycle: resource.LifecycleOptions{HandleDestroyError: nil, RetainOnDestroy: false},
	}
	comps := legacyComponents(opts)
	out := make([]resource.Resource, 0, 1+2*len(comps))
	out = append(out, network)
	for _, c := range comps {
		image := legacyComponentImage(opts, c)
		out = append(out, image, componentContainer(opts, c, image, resource.Ref(network)))
	}
	return out
}

func legacyComponents(opts *Options) []component {
	var out []component
	for _, c := range legacyAllComponents(opts) {
		if c.group == "" || opts.Groups[c.group] {
			out = append(out, c)
		}
	}
	return out
}

func legacyBoundTopologyConfig(opts *Options) envtopology.BoundTopologyConfig {
	var routes []envtopology.BoundTopologyRoute
	for _, c := range legacyAllComponents(opts) {
		enabled := c.group == "" || opts.Groups[c.group]
		for _, r := range c.routes {
			routes = append(routes, hostRoute(c.name, r.host, r.prefix, c.hostURL(), r.rewrite, c.group, enabled))
		}
	}
	routes = append(routes, hostRoute(
		"frontendDevServer",
		opts.Topology.FrontendDevServerHostName(),
		"",
		"http://"+loopback+":"+frontendDevServerHostPort,
		"",
		"",
		true,
	))
	return envtopology.BoundTopologyConfig{
		AppRouteTemplate: envtopology.BoundTopologyAppRouteTemplate{
			Host:               opts.Topology.AppHostName(),
			PathPrefixTemplate: "/{org}/{app}",
		},
		Routes:  routes,
		Version: envtopology.BoundTopologyConfigVersion,
	}
}

func legacyHealthTargets(opts *Options) []healthTarget {
	var targets []healthTarget
	for _, c := range legacyComponents(opts) {
		if c.healthPath == "" || c.hostPort == "" {
			continue
		}
		targets = append(targets, healthTarget{name: c.name, url: c.hostURL() + c.healthPath})
	}
	return append(targets, healthTarget{name: "proxy", url: opts.Topology.LocaltestURL() + "/Home/Localtest/Version"})
}

func legacyContainerNames(opts *Options) []string {
	comps := legacyComponents(opts)
	names := make([]string, 0, len(comps))
	for _, c := range comps {
		names = append(names, containerPrefix+c.name)
	}
	return names
}

func legacyDevBuildable() []string {
	var names []string
	for _, c := range legacyAllComponents(defaultOptions(config.DefaultImages(), "")) {
		if c.devContext != "" {
			names = append(names, legacyDevName(c))
		}
	}
	return slices.Compact(names)
}

func legacyGroupTable() map[string]group {
	table := map[string]group{
		groupDialogporten:   {implies: nil, onByDefault: false},
		groupReceipt:        {implies: nil, onByDefault: false},
		groupCorrespondence: {implies: []string{groupDialogporten}, onByDefault: false},
		groupArbeidsflate:   {implies: []string{groupDialogporten}, onByDefault: false},
	}
	table[groupEverything] = group{implies: slices.Sorted(maps.Keys(table)), onByDefault: false}
	return table
}

func legacyVolumes() []string {
	return []string{
		volumePostgresData, volumeStorageBlobs, volumePolicyStore, volumeLocaltestData, volumeCorrespondenceBlobs,
	}
}
