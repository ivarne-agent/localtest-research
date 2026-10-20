import { useState, useEffect } from 'react';
import Axios, { AxiosResponse } from 'axios';

import type {
  IInstanceContext,
  IDataSources,
  IApplication,
  IInstance,
  IParty,
  IProfile,
  IUserCookieLanguage,
  IExtendedInstance,
  ITextResource,
  IAltinnOrgs,
  IAttachmentGroupsToHide,
} from 'src/types';

import {
  altinnOrganisationsUrl,
  getApplicationMetadataUrl,
  getUserUrl,
  getUserLanguageUrl,
  getExtendedInstanceUrl,
  getTextResourceUrl,
  getAttachmentGroupingsToHide,
} from 'src/utils/receiptUrlHelper';
import { buildInstanceContext } from 'src/utils/instanceContext';
import { languageLookup, getLanguageFromCode } from 'src/language';
import { replaceTextResourceParams } from 'src/utils/language';

interface IMergeLanguageWithOverrides {
  textResources: ITextResource[];
  instance: IInstance;
  languageCode?: string;
}

const mergeLanguageWithOverrides = ({
  instance,
  textResources,
  languageCode = 'nb',
}: IMergeLanguageWithOverrides) => {
  const originalLanguage = getLanguageFromCode(languageCode);
  const keyPrefix = 'receipt_platform.';
  const instanceContext: IInstanceContext = buildInstanceContext(instance);
  const dataSources: IDataSources = {
    instanceContext,
  };

  const overrides = textResources
    .filter((item) => item.id.startsWith(keyPrefix))
    .map((item) => {
      return {
        ...item,
        unparsedValue: item.value,
      };
    });

  const newTextResources = replaceTextResourceParams(overrides, dataSources);

  const newLanguage = newTextResources.reduce<Record<string, string>>(
    (acc, curr) => {
      const key = curr.id.replace(keyPrefix, '');

      return {
        ...acc,
        [key]: curr.value,
      };
    },
    {},
  );

  return {
    ...originalLanguage.receipt_platform,
    ...newLanguage,
  };
};

interface IUseLanguageWithOverrides {
  textResources?: ITextResource[];
  instance?: IInstance;
  user?: IProfile;
}

export const useLanguageWithOverrides = ({
  textResources,
  instance,
  user,
}: IUseLanguageWithOverrides) => {

  const [language, setLanguage] = useState({
    receipt_platform: getLanguageFromCode(user?.profileSettingPreference.language ?? '').receipt_platform
  });

  useEffect(() => {
    if (user && textResources && instance) {
      try {
        const mergedLanguage = mergeLanguageWithOverrides({
          languageCode: user.profileSettingPreference?.language,
          textResources,
          instance,
        });
        setLanguage({
          receipt_platform: mergedLanguage,
        });
      } catch (error) {
        console.error(error);
      }
    }
  }, [user, instance, textResources]);

  return { language };
};

const cancelSignalMessage = 'canceled';

const logFetchError = (error: any) => {
  if (error?.message !== cancelSignalMessage) {
    console.error(error);
  }
};

export const useFetchInitialData = () => {
  const [party, setParty] = useState<IParty>();
  const [instance, setInstance] = useState<IInstance>();
  const [organisations, setOrganisations] = useState<IAltinnOrgs>();
  const [application, setApplication] = useState<IApplication>();
  const [user, setUser] = useState<IProfile>();
  const [textResources, setTextResources] = useState<ITextResource[]>();

  useEffect(() => {
    const instanceAbortController = new AbortController();
    const orgAbortController = new AbortController();
    const userAbortController = new AbortController();
    const languageAbortController = new AbortController();
    const appAbortController = new AbortController();
    const textAbortController = new AbortController();
    const attachmentgroupsToHideAbortController = new AbortController();

    const fetchTextResources = (
      org: string,
      app: string,
      languages: string[]
    ): Promise<{ response: AxiosResponse<any>; language: string; }> => {
      return new Promise<{ response: AxiosResponse<any>; language: string; }>((resolve, _reject) => {
        const fetchNextLanguage = async (index: number): Promise<void> => {
          if (index >= languages.length) {
            // If all languages failed, reject the promise
            logFetchError('Text resources not found for any language');
            return;
          }

          const language = languages[index];

          try {
            const response = await Axios.get(
              getTextResourceUrl(org, app, language),
              {
                signal: textAbortController.signal,
              }
            );

            if (response.status === 200 && Array.isArray(response.data.resources)) {
              resolve({ response, language });
            } else {
              // If the response is not successful, try the next language
              await fetchNextLanguage(index + 1);
            }
          } catch (error) {
            logFetchError(error);
            // If an error occurs, try the next language
            await fetchNextLanguage(index + 1);
          }
        };

        fetchNextLanguage(0); // Start with the first language
      });
    };

    const fetchInitialData = async () => {
      try {
        const [instanceResponse, orgResponse, userResponse, userLanguage, attachmentGroupsToHide] = await Promise.all(
          [
            Axios.get<IExtendedInstance>(getExtendedInstanceUrl(), {
              signal: instanceAbortController.signal,
            }),
            Axios.get(altinnOrganisationsUrl, {
              signal: orgAbortController.signal,
            }),
            Axios.get<IProfile>(getUserUrl(), {
              signal: userAbortController.signal,
            }),
            Axios.get<IUserCookieLanguage>(getUserLanguageUrl(), {
              signal: languageAbortController.signal,
            }),
            Axios.get<IAttachmentGroupsToHide>(getAttachmentGroupingsToHide(), {
              signal: attachmentgroupsToHideAbortController.signal,
            }),
          ],
        );

        if (userLanguage.status === 200 && userLanguage.data.language != "") {
          userResponse.data.profileSettingPreference.language = userLanguage.data.language;
        }

        const langs = Object.keys(languageLookup).filter(
          element => element !== userResponse.data.profileSettingPreference.language
        ); // Getting all the laguages except the current language
        langs.unshift(userResponse.data.profileSettingPreference.language); // Putting the current language in the beginning.

        const app = instanceResponse.data.instance.appId.split('/')[1];

        instanceResponse.data.instance.isA2Lookup = false;
        if (instanceResponse.data.instance.dataValues != null) {
          Object.keys(instanceResponse.data.instance.dataValues).forEach((key, _value) => {
            if(key == 'A2ServiceType' && instanceResponse.data.instance.dataValues[key] == 'Lookup')
            {
              instanceResponse.data.instance.isA2Lookup = true;
            }
          });
        }

        const [applicationResponse, appTextResourcesResponse] =
          await Promise.all([
            Axios.get<IApplication>(
              getApplicationMetadataUrl(
                instanceResponse.data.instance.org,
                app,
              ),
              {
                signal: appAbortController.signal,
              },
            ),
            fetchTextResources(instanceResponse.data.instance.org, app, langs),
          ]);

          if (attachmentGroupsToHide.data.attachmentgroupstohide != null)
          {
            applicationResponse.data.attachmentGroupsToHide = attachmentGroupsToHide.data.attachmentgroupstohide.split(';');
          }

        setApplication(applicationResponse.data);
        setTextResources(appTextResourcesResponse.response.data.resources);
        setParty(instanceResponse.data.party);
        setInstance(instanceResponse.data.instance);
        setOrganisations(orgResponse.data.orgs);
        setUser(userResponse.data);
      } catch (error) {
        logFetchError(error);
      }
    };

    fetchInitialData();

    return () => {
      instanceAbortController.abort();
      orgAbortController.abort();
      userAbortController.abort();
    };
  }, []);

  return {
    application,
    textResources,
    party,
    instance,
    organisations,
    user,
  };
};
