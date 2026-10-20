import moment from 'moment';

import type { IAltinnOrgs, IApplication, IInstance, ILanguage, IParty, ITextResource } from 'src/types';

import { getCurrentTaskData } from 'src/utils/applicationMetaDataUtils';
import { getAppReceiver, getLanguageFromKey } from 'src/utils/language';

import { getArchiveRef } from './instance';

const formatDate = (date: any): string => moment(date).format('DD.MM.YYYY / HH:mm');

const getDateSubmitted = (instance: IInstance, application: IApplication): string | undefined => {
  if (instance.data && instance.data.length > 0) {
    const currentTaskData = getCurrentTaskData(application, instance);
    if (currentTaskData !== undefined) {
      return instance.process?.ended
        ? formatDate(instance.process.ended)
        : formatDate(currentTaskData.lastChanged);
    }
  }

  if (instance.status.isArchived) {
    return formatDate(instance.status.archived);
  }

  return undefined;
};

const getSender = (party: IParty): string => {
  if (party.ssn) {
    return `${party.ssn}-${party.name}`;
  } else if (party.orgNumber) {
    return `${party.orgNumber}-${party.name}`;
  }
  return party.name ?? '';
};

export const getInstanceMetaDataObject = (
  instance: IInstance,
  party: IParty,
  language: ILanguage,
  organisations: IAltinnOrgs,
  application: IApplication,
  textResources: ITextResource[],
  userLanguage: string,
) => {
  const obj = {} as any;

  if (!instance || !party || !language || !organisations) {
    return obj;
  }

  let dateSubmitted: any = getDateSubmitted(instance, application);

  if (dateSubmitted === undefined && instance.status.isArchived) {
    dateSubmitted = moment(instance.status.archived).format('DD.MM.YYYY / HH:mm');
  }

  if (instance.isA2Lookup){
    obj[getLanguageFromKey('receipt_platform.date_archived', language)] = dateSubmitted;
  } else {
    obj[getLanguageFromKey('receipt_platform.date_sent', language)] = dateSubmitted;
  }

  const sender = getSender(party);

  if (!instance.isA2Lookup) {
    obj[getLanguageFromKey('receipt_platform.sender', language)] = sender;
    obj[getLanguageFromKey('receipt_platform.receiver', language)] = getAppReceiver(textResources, organisations, instance.org, userLanguage);
  }

  obj[getLanguageFromKey('receipt_platform.reference_number', language)] = getArchiveRef();

  return obj;
};
