import { getInstanceId, getInstanceOwnerId } from './instance';

export const altinnAt22PlatformUrl = 'https://platform.at22.altinn.cloud/';
export const altinnAt22Url = 'https://at22.altinn.cloud/';
export const altinnOrganisationsUrl =
  'https://altinncdn.no/orgs/altinn-orgs.json';

export function getExtendedInstanceUrl() {
  return `${getAltinnCloudUrl()}receipt/api/v1/instances/${getInstanceOwnerId()}/${getInstanceId()}?includeParty=true`;
}

export function getAltinnCloudUrl() {
  if (
    window.location.hostname === 'localhost' ||
    window.location.hostname === '127.0.0.1' ||
    window.location.hostname === 'altinn3.no'
  ) {
    // if we are developing locally, point to test data in at22
    return altinnAt22PlatformUrl;
  }

  // Point to origin. Can be multiple environments.
  return `${window.location.origin}/`;
}

export function getApplicationMetadataUrl(org: string, app: string) {
  return `${getAltinnCloudUrl()}storage/api/v1/applications/${org}/${app}`;
}

export function getUserUrl() {
  return `${window.location.origin}/receipt/api/v1/users/current`;
}

export function getUserLanguageUrl() {
  return `${window.location.origin}/receipt/api/v1/users/current/language`;
}

export function getTextResourceUrl(org: string, app: string, language: string) {
  return `${window.location.origin}/storage/api/v1/applications/${org}/${app}/texts/${language}`;
}

export function getAltinnUrl() {
  if (window.location.hostname === 'localhost') {
    return altinnAt22Url;
  }
  return `${window.location.origin}/`;
}

export function getAttachmentGroupingsToHide() {
  return `${window.location.origin}/receipt/api/v1/application/attachmentgroupstohide`;
}

