import type {
  IAltinnOrg,
  IAltinnOrgs,
  IApplication,
  IData,
  IInstance,
  ILanguage,
  IParty,
  ITextResource,
} from 'src/types/index';

import { getInstanceMetaDataObject } from './receipt';

describe('utils > receipt', () => {
  const instance = {
    data: [
      {
        dataType: 'default',
        lastChanged: new Date(2018, 11, 24, 10, 33),
      } as IData,
    ],
    org: 'testOrg',
  } as IInstance;

  const partyPerson = {
    name: 'Ola Nordmann',
    ssn: '12345678',
  } as IParty;

  const partyOrg = {
    name: 'FIRMA AS',
    orgNumber: 12345,
  } as IParty;

  const partySelfIdentified = {
    name: 'email@example.com',
    partyTypeName: 3,
  } as IParty;

  const language: ILanguage = {
    receipt_platform: {
      date_sent: 'Dato sendt',
      sender: 'Avsender',
      receiver: 'Mottaker',
      reference_number: 'Referansenummer',
    },
  };

  const organisations: IAltinnOrgs = {
    testOrg: {
      name: {
        nb: 'TEST ORG',
      },
    } as unknown as IAltinnOrg,
  };

  const expectedResult = {
    'Dato sendt': '24.12.2018 / 10:33',
    Avsender: '12345678-Ola Nordmann',
    Mottaker: 'TEST ORG',
    Referansenummer: 'd6a414a797ae',
  };

  const application = {
    dataTypes: [{ appLogic: true, id: 'default' }],
  } as IApplication;

  describe('getInstanceMetaDataObject', () => {
    it('should return instance metadata object with correct values for person', () => {
      const result = getInstanceMetaDataObject(instance, partyPerson, language, organisations, application, [], 'nb');
      expect(result).toEqual(expectedResult);
    });

    it('should return instance metadata object with correct values for org', () => {
      const result = getInstanceMetaDataObject(instance, partyOrg, language, organisations, application, [], 'nb');
      const expectedOrgResult = expectedResult;
      expectedOrgResult.Avsender = '12345-FIRMA AS';
      expect(result).toEqual(expectedOrgResult);
    });

    it('should use the party name as sender for a self-identified party without ssn or orgNumber', () => {
      const result = getInstanceMetaDataObject(
        instance,
        partySelfIdentified,
        language,
        organisations,
        application,
        [],
        'nb',
      );
      expect(result.Avsender).toEqual('email@example.com');
    });

    it('should return an empty sender when the party has no ssn, orgNumber or name', () => {
      const result = getInstanceMetaDataObject(instance, {} as IParty, language, organisations, application, [], 'nb');
      expect(result.Avsender).toEqual('');
    });

    it('should return empty object if no instance is provided', () => {
      const result = getInstanceMetaDataObject(undefined, partyOrg, language, organisations, application, [], 'nb');

      expect(result).toEqual({});
    });

    it('should return empty object if no party is provided', () => {
      const result = getInstanceMetaDataObject(instance, undefined, language, organisations, application, [], 'nb');

      expect(result).toEqual({});
    });

    it('should return empty object if no language is provided', () => {
      const result = getInstanceMetaDataObject(instance, partyOrg, undefined, organisations, application, [], 'nb');

      expect(result).toEqual({});
    });

    it('should return empty object if no organisations is provided', () => {
      const result = getInstanceMetaDataObject(instance, partyOrg, language, undefined, application, [], 'nb');

      expect(result).toEqual({});
    });

    it('should display appReceiver name from text resources if defined', () => {
      const textResourceWithAppOwner: ITextResource[] = [
        {
          id: 'appReceiver',
          value: 'Name from resources',
        },
      ];
      const result = getInstanceMetaDataObject(
        instance,
        partyOrg,
        language,
        organisations,
        application,
        textResourceWithAppOwner,
        'nb',
      );
      expect(result.Mottaker).toEqual('Name from resources');
    });

    it('should return process.ended date if it exists', () => {
      const instanceWithEndedProcess = {
        ...instance,
        process: {
          ended: new Date(2019, 11, 24, 10, 33).toISOString(),
        },
      } as IInstance;

      const result = getInstanceMetaDataObject(
        instanceWithEndedProcess,
        partyPerson,
        language,
        organisations,
        application,
        [],
        'nb',
      );
      expect(result['Dato sendt']).toEqual('24.12.2019 / 10:33');
    });
  });
});
