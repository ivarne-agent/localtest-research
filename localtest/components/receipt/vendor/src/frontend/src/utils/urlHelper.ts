import type { IAltinnWindow } from '../types';
import { getReturnUrl } from './instance';

const { org, app } = window as Window as IAltinnWindow;
const origin = window.location.origin;

export const getApplicationMetadataUrl = (): string => {
  return `${origin}/designer/api/v1/${org}/${app}`;
};

const prodStagingRegex = /^(?:\w+\.apps|platform)\.((\w+\.)?altinn\.(no|cloud))$/;
const localRegex = /^local\.altinn\.cloud(:\d+)?$/;
const localhostRegex = /^localhost(:\d+)?$/;

function isLocalEnvironment(host: string): boolean {
  return localRegex.test(host) || localhostRegex.test(host);
}

function extractAltinnHost(host: string): string | undefined {
  const match = host.match(prodStagingRegex);
  return match?.[1];
}

function isProductionEnvironment(altinnHost: string): boolean {
  return altinnHost === 'altinn.no';
}

function buildArbeidsflateUrl(altinnHost: string): string {
  if (isProductionEnvironment(altinnHost)) {
    return 'https://af.altinn.no/';
  }
  return `https://af.${altinnHost}/`;
}

function buildAccessManagementBaseUrl(altinnHost: string): string {
  return `https://am.ui.${altinnHost}/`;
}

export const returnBaseUrlToAltinn = (host: string): string | undefined => {
  const altinnHost = extractAltinnHost(host);
  if (!altinnHost) {
    return undefined;
  }
  return `https://${altinnHost}/`;
};

function buildArbeidsflateRedirectUrl(host: string, partyId?: number, dialogId?: string): string | undefined {
  if (isLocalEnvironment(host)) {
    return `http://${host}/`;
  }

  const altinnHost = extractAltinnHost(host);
  if (!altinnHost) {
    return undefined;
  }

  const arbeidsflateBaseUrl = buildArbeidsflateUrl(altinnHost);
  const targetUrl = dialogId
    ? `${arbeidsflateBaseUrl.replace(/\/$/, '')}/inbox/${dialogId}`
    : arbeidsflateBaseUrl;

  if (partyId === undefined) {
    return targetUrl;
  }

  // Use access management changeandredirect endpoint to switch party and redirect to A3 arbeidsflate
  const amBaseUrl = buildAccessManagementBaseUrl(altinnHost);
  return `${amBaseUrl}accessmanagement/api/v1/reportee/changeandredirect?partyId=${partyId}&goTo=${encodeURIComponent(targetUrl)}`;
}

export function getDialogIdFromDataValues(dataValues: unknown): string | undefined {
  const data = dataValues as Record<string, unknown> | null | undefined;
  const id = data?.['dialog.id'];
  if (typeof id === 'string') {
    return id;
  }
  if (typeof id === 'number') {
    return String(id);
  }
  return undefined;
}

export const returnUrlToMessagebox = (host: string, partyId?: number, dialogId?: string): string | undefined => {
  const customReturnUrl = getReturnUrl();
  if (customReturnUrl) {
    return customReturnUrl;
  }

  return buildArbeidsflateRedirectUrl(host, partyId, dialogId);
};

export const logoutUrlAltinn = (host: string): string | undefined => {
  if (isLocalEnvironment(host)) {
    return `http://${host}/`;
  }

  const baseUrl = returnBaseUrlToAltinn(host);
  if (!baseUrl) {
    return undefined;
  }
  return `${baseUrl}ui/authentication/LogOut`;
};

// Storage is always returning https:// links for attachments.
// on localhost (without https) this is a problem, so we make links
// to the same domain as window.location.host relative.
// "https://domain.com/a/b" => "/a/b"
export const makeUrlRelativeIfSameDomain = (
  url: string,
  location: Location = window.location,
) => {
  try {
    const parsed = new URL(url);
    if (parsed.hostname === location.hostname) {
      return parsed.pathname + parsed.search + parsed.hash;
    }
  } catch (_e) {
    //ignore invalid (or dummy) urls
  }
  return url;
};
