import { http } from 'msw';
import { setupServer } from 'msw/node';

import { instance, altinnOrgs, currentUser, application, texts } from './apiResponses';

import type { JSDOM } from 'jsdom';

// `jsdom` is exposed on the global by the custom test environment
// (testConfig/jsdomEnvironment.js).
declare const jsdom: JSDOM;

// jsdom 26 (bundled with jest 30) makes `window.location` non-configurable, so
// it can no longer be replaced via jest.spyOn. Navigate the jsdom document
// instead, which is the supported way to change the url.
// Docs: https://github.com/jsdom/jsdom#reconfiguring-the-jsdom-with-reconfiguresettings
export const setUrl = (url: string) => {
  jsdom.reconfigure({ url });
};

export const instanceHandler = (response: any) => {
  return http.get(
    'https://platform.at22.altinn.cloud/receipt/api/v1/instances/mockInstanceOwnerId/6697de17-18c7-4fb9-a428-d6a414a797ae',
    () => new Response(JSON.stringify(response)),
  );
};

export const textsHandler = (response: any) => {
  return http.get(
    'https://localhost/storage/api/v1/applications/ttd/frontend-test/texts/nb',
    () => new Response(JSON.stringify(response)),
  );
};

export const handlers: any = [
  instanceHandler(instance),
  textsHandler(texts),

  http.get('https://altinncdn.no/orgs/altinn-orgs.json', () => new Response(JSON.stringify(altinnOrgs))),
  http.get('https://localhost/receipt/api/v1/users/current', () => new Response(JSON.stringify(currentUser))),
  http.get(
    'https://localhost/receipt/api/v1/users/current/language',
    () => new Response(JSON.stringify({ language: 'nb' })),
  ),
  http.get(
    'https://localhost/receipt/api/v1/application/attachmentgroupstohide',
    () => new Response(JSON.stringify({ attachmentgroupstohide: null })),
  ),
  http.get(
    'https://platform.at22.altinn.cloud/storage/api/v1/applications/ttd/frontend-test',
    () => new Response(JSON.stringify(application)),
  ),
];

export { setupServer };
