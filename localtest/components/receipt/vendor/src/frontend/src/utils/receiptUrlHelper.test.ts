import { getInstanceId, getInstanceOwnerId } from './instance';

import { setUrl } from 'testConfig/testUtils';

import {
  altinnAt22Url,
  altinnAt22PlatformUrl,
  getAltinnUrl,
  getTextResourceUrl,
  getUserUrl,
  getApplicationMetadataUrl,
  getAltinnCloudUrl,
  getExtendedInstanceUrl,
} from './receiptUrlHelper';

const instancePath =
  '/receipt/mockInstanceOwnerId/6697de17-18c7-4fb9-a428-d6a414a797ae';
const urlFor = (host: string) => `https://${host}${instancePath}`;

describe('receiptUrlHelper', () => {
  beforeEach(() => {
    setUrl(urlFor('localhost'));
  });

  describe('getAltinnUrl', () => {
    it('should return altinnAt22Url when hostname is localhost', () => {
      setUrl(urlFor('localhost'));

      expect(getAltinnUrl()).toEqual(altinnAt22Url);
    });

    it('should return window.location.origin followed by trailing slash when hostname is not localhost', () => {
      setUrl(urlFor('tdd.apps.altinn.no'));

      expect(getAltinnUrl()).toEqual('https://tdd.apps.altinn.no/');
    });
  });

  describe('getTextResourceUrl', () => {
    it('should return correct path with args', () => {
      expect(getTextResourceUrl('org-name', 'app-name', 'nb')).toEqual(
        'https://localhost/storage/api/v1/applications/org-name/app-name/texts/nb',
      );
    });
  });

  describe('getUserUrl', () => {
    it('should return correct path with args', () => {
      expect(getUserUrl()).toEqual(
        'https://localhost/receipt/api/v1/users/current',
      );
    });
  });

  describe('getApplicationMetadataUrl', () => {
    it('should return correct path when running on localhost', () => {
      setUrl(urlFor('localhost'));

      expect(getApplicationMetadataUrl('org-name', 'app-name')).toEqual(
        `${altinnAt22PlatformUrl}storage/api/v1/applications/org-name/app-name`,
      );
    });

    it('should return correct path when not running on localhost', () => {
      setUrl(urlFor('tdd.apps.altinn.no'));

      expect(getApplicationMetadataUrl('org-name', 'app-name')).toEqual(
        `https://tdd.apps.altinn.no/storage/api/v1/applications/org-name/app-name`,
      );
    });
  });

  describe('getAltinnCloudUrl', () => {
    it('should return altinnAt22PlatformUrl when running on localhost', () => {
      setUrl(urlFor('localhost'));

      expect(getAltinnCloudUrl()).toEqual(altinnAt22PlatformUrl);
    });

    it('should return altinnAt22PlatformUrl when running on 127.0.0.1', () => {
      setUrl(urlFor('127.0.0.1'));

      expect(getAltinnCloudUrl()).toEqual(altinnAt22PlatformUrl);
    });

    it('should return altinnAt22PlatformUrl when running on altinn3.no', () => {
      setUrl(urlFor('altinn3.no'));

      expect(getAltinnCloudUrl()).toEqual(altinnAt22PlatformUrl);
    });

    it('should return window.location.origin followed by trailing slash when running on altinn3.no', () => {
      setUrl(urlFor('tdd.apps.altinn.no'));

      expect(getAltinnCloudUrl()).toEqual('https://tdd.apps.altinn.no/');
    });
  });

  describe('getExtendedInstanceUrl', () => {
    it('should return correct path when running on localhost', () => {
      setUrl(urlFor('localhost'));

      expect(getExtendedInstanceUrl()).toEqual(
        `${altinnAt22PlatformUrl}receipt/api/v1/instances/${getInstanceOwnerId()}/${getInstanceId()}?includeParty=true`,
      );
    });

    it('should return correct path when not running on localhost', () => {
      setUrl(urlFor('tdd.apps.altinn.no'));

      expect(getExtendedInstanceUrl()).toEqual(
        `https://tdd.apps.altinn.no/receipt/api/v1/instances/${getInstanceOwnerId()}/${getInstanceId()}?includeParty=true`,
      );
    });
  });
});
