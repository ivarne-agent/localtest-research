import 'jest';
import {
  returnUrlToMessagebox,
  returnBaseUrlToAltinn,
  logoutUrlAltinn,
  makeUrlRelativeIfSameDomain,
  getDialogIdFromDataValues,
} from './urlHelper';
import { setUrl } from 'testConfig/testUtils';

describe('Shared urlHelper.ts', () => {
  test('returnUrlToMessagebox() returning production inbox', () => {
    const host = 'tdd.apps.altinn.no';
    expect(returnUrlToMessagebox(host)).toBe('https://af.altinn.no/');
  });

  test('returnUrlToMessagebox() returning AT inbox', () => {
    const host = 'tdd.apps.at22.altinn.cloud';
    expect(returnUrlToMessagebox(host)).toBe('https://af.at22.altinn.cloud/');
  });

  test('returnUrlToMessagebox() returning TT inbox', () => {
    const host = 'tdd.apps.tt02.altinn.no';
    expect(returnUrlToMessagebox(host)).toBe('https://af.tt02.altinn.no/');
  });

  test('returnUrlToMessagebox() returning YT inbox', () => {
    const host = 'tdd.apps.yt01.altinn.cloud';
    expect(returnUrlToMessagebox(host)).toBe('https://af.yt01.altinn.cloud/');
  });

  test('returnUrlToMessagebox() returning production inbox for platform host', () => {
    const host = 'platform.altinn.no';
    expect(returnUrlToMessagebox(host)).toBe('https://af.altinn.no/');
  });

  test('returnUrlToMessagebox() returning AT inbox for platform host', () => {
    const host = 'platform.at23.altinn.cloud';
    expect(returnUrlToMessagebox(host)).toBe('https://af.at23.altinn.cloud/');
  });

  test('returnUrlToMessagebox() returning undefined when unknown host', () => {
    const host = 'www.ikkealtinn.no';
    expect(returnUrlToMessagebox(host)).toBe(undefined);
  });

  test('returnUrlToMessagebox() with partyId uses redirect mechanism', () => {
    const host = 'tdd.apps.altinn.no';
    const result = returnUrlToMessagebox(host, 12345);
    expect(result).toBe('https://am.ui.altinn.no/accessmanagement/api/v1/reportee/changeandredirect?partyId=12345&goTo=https%3A%2F%2Faf.altinn.no%2F');
  });

  test('returnUrlToMessagebox() with partyId uses redirect mechanism across environments', () => {
    const partyId = 12345;
    expect(returnUrlToMessagebox('tdd.apps.tt02.altinn.no', partyId)).toBe(
      'https://am.ui.tt02.altinn.no/accessmanagement/api/v1/reportee/changeandredirect?partyId=12345&goTo=https%3A%2F%2Faf.tt02.altinn.no%2F',
    );
    expect(returnUrlToMessagebox('tdd.apps.at22.altinn.cloud', partyId)).toBe(
      'https://am.ui.at22.altinn.cloud/accessmanagement/api/v1/reportee/changeandredirect?partyId=12345&goTo=https%3A%2F%2Faf.at22.altinn.cloud%2F',
    );
    expect(returnUrlToMessagebox('tdd.apps.yt01.altinn.cloud', partyId)).toBe(
      'https://am.ui.yt01.altinn.cloud/accessmanagement/api/v1/reportee/changeandredirect?partyId=12345&goTo=https%3A%2F%2Faf.yt01.altinn.cloud%2F',
    );
  });

  test('returnUrlToMessagebox() with dialogId returns inbox URL with dialogId', () => {
    const host = 'tdd.apps.altinn.no';
    const result = returnUrlToMessagebox(host, undefined, 'abc-123');
    expect(result).toBe('https://af.altinn.no/inbox/abc-123');
  });

  test('returnUrlToMessagebox() with partyId and dialogId uses redirect with dialogId', () => {
    const host = 'tdd.apps.altinn.no';
    const result = returnUrlToMessagebox(host, 12345, 'abc-123');
    expect(result).toBe('https://am.ui.altinn.no/accessmanagement/api/v1/reportee/changeandredirect?partyId=12345&goTo=https%3A%2F%2Faf.altinn.no%2Finbox%2Fabc-123');
  });

  test('returnUrlToMessagebox() for local environment', () => {
    const host = 'local.altinn.cloud';
    expect(returnUrlToMessagebox(host)).toBe('http://local.altinn.cloud/');
  });

  test('returnBaseUrlToAltinn() returning correct environments', () => {
    const hostTT = 'ttd.apps.tt02.altinn.no';
    const hostAT = 'ttd.apps.at22.altinn.cloud';
    const hostYT = 'ttd.apps.yt01.altinn.cloud';
    const hostProd = 'ttd.apps.altinn.no';
    const hostUnknown = 'www.ikkealtinn.no';
    expect(returnBaseUrlToAltinn(hostTT)).toBe('https://tt02.altinn.no/');
    expect(returnBaseUrlToAltinn(hostAT)).toBe('https://at22.altinn.cloud/');
    expect(returnBaseUrlToAltinn(hostYT)).toBe('https://yt01.altinn.cloud/');
    expect(returnBaseUrlToAltinn(hostProd)).toBe('https://altinn.no/');
    expect(returnBaseUrlToAltinn(hostUnknown)).toBe(undefined);
  });

  test('logoutUrlAltinn() should return correct url for each env.', () => {
    const hostTT = 'ttd.apps.tt02.altinn.no';
    const hostAT = 'ttd.apps.at22.altinn.cloud';
    const hostYT = 'ttd.apps.yt01.altinn.cloud';
    const hostProd = 'ttd.apps.altinn.no';
    expect(logoutUrlAltinn(hostTT)).toBe('https://tt02.altinn.no/ui/authentication/LogOut');
    expect(logoutUrlAltinn(hostAT)).toBe('https://at22.altinn.cloud/ui/authentication/LogOut');
    expect(logoutUrlAltinn(hostYT)).toBe('https://yt01.altinn.cloud/ui/authentication/LogOut');
    expect(logoutUrlAltinn(hostProd)).toBe('https://altinn.no/ui/authentication/LogOut');
  });

  test('logoutUrlAltinn() for local environment', () => {
    const host = 'local.altinn.cloud';
    expect(logoutUrlAltinn(host)).toBe('http://local.altinn.cloud/');
  });

  // ReturnUrl test for altinn3
  test('returnUrlToMessagebox() with custom returnUrl', () => {
    const host = 'tdd.apps.altinn.no';
    const target = 'https://af.altinn.no/custom-path';
    setUrl(`https://localhost/?returnUrl=${encodeURIComponent(target)}`);
    expect(returnUrlToMessagebox(host)).toBe(target);
  });

  test('logoutUrlAltinn() ignores custom returnUrl', () => {
    const host = 'tdd.apps.altinn.no';
    const target = 'https://af.altinn.no/location';
    setUrl(`https://localhost/?returnUrl=${encodeURIComponent(target)}`);
    expect(logoutUrlAltinn(host)).toBe('https://altinn.no/ui/authentication/LogOut');
  });

  test('makeUrlRelativeIfSameDomain()', () => {
    // Simple testcase make relative
    expect(
      makeUrlRelativeIfSameDomain('https://altinn3local.no/asdf', {
        hostname: 'altinn3local.no',
      } as Location),
    ).toBe('/asdf');
    // Simple testcase domains don't match
    expect(
      makeUrlRelativeIfSameDomain('https://altinn3local.no/asdf', {
        hostname: 'altinn3localno',
      } as Location),
    ).toBe('https://altinn3local.no/asdf');
    // Test with dummyurl
    expect(
      makeUrlRelativeIfSameDomain('dummyurl', {
        hostname: 'altinn3local.no',
      } as Location),
    ).toBe('dummyurl');

    // Test with non-standard port
    expect(
      makeUrlRelativeIfSameDomain('http://altinn3local.no:8080/', {
        hostname: 'altinn3local.no',
      } as Location),
    ).toBe('/');
    expect(
      makeUrlRelativeIfSameDomain('http://altinn3local.no:8080/', {
        hostname: 'altinn3local.no',
      } as Location),
    ).toBe('/');
  });

  describe('getDialogIdFromDataValues', () => {
    test('returns dialogId when it exists as string', () => {
      expect(getDialogIdFromDataValues({ 'dialog.id': 'abc-123' })).toBe('abc-123');
    });

    test('returns dialogId when it exists as number', () => {
      expect(getDialogIdFromDataValues({ 'dialog.id': 12345 })).toBe('12345');
    });

    test('returns undefined when dialog.id does not exist', () => {
      expect(getDialogIdFromDataValues({ other: 'value' })).toBe(undefined);
    });

    test('returns undefined when dataValues is null', () => {
      expect(getDialogIdFromDataValues(null)).toBe(undefined);
    });

    test('returns undefined when dataValues is undefined', () => {
      expect(getDialogIdFromDataValues(undefined)).toBe(undefined);
    });
  });
});
