import { getInstanceId, getInstanceOwnerId, getArchiveRef } from './instance';

import { setUrl } from 'testConfig/testUtils';

// matches the url set in package.json under "jest.testEnvironmentOptions.url"
const instanceUrl =
  'https://localhost/receipt/mockInstanceOwnerId/6697de17-18c7-4fb9-a428-d6a414a797ae';

describe('utils/instance', () => {
  beforeEach(() => {
    setUrl(instanceUrl);
  });
  describe('getInstanceId', () => {
    it('should return instanceOwnerId when it exists in url', () => {
      expect(getInstanceId()).toEqual('6697de17-18c7-4fb9-a428-d6a414a797ae');
    });
  });

  describe('getInstanceOwnerId', () => {
    it('should return instanceOwnerId when it exists in url', () => {
      expect(getInstanceOwnerId()).toEqual('mockInstanceOwnerId');
    });
  });

  describe('getArchiveRef', () => {
    it('should return last part of instanceId', () => {
      expect(getArchiveRef()).toEqual('d6a414a797ae');
    });
  });
});
