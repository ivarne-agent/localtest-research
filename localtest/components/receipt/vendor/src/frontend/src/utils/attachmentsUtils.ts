import { IAttachment, IData, ITextResource, IAttachmentGrouping, IDataType, IApplication } from '../types/index';
import { getTextResourceByKey } from './language';

export function filterAppData(appData: IData[], dataTypes: IDataType[]) {
  const dataTypeIdsToExclude = dataTypes
    .filter((dataType) => {
      if (dataType.appLogic) {
        return true;
      }

      if (dataType.allowedContributers?.includes('app:owned') || dataType.allowedContributors?.includes('app:owned')) {
        return true;
      }

      return dataType.id === 'ref-data-as-pdf';
    })
    .map((dataType) => dataType.id);

  return appData.filter((it) => !dataTypeIdsToExclude.includes(it.dataType));
}

export const mapAppDataToAttachments = (data: IData[], platform?: boolean): IAttachment[] => {
  if (!data) {
    return [];
  }

  return data.map<IAttachment>((dataElement: IData) => {
    return {
      name: dataElement.filename,
      url: platform ? dataElement.selfLinks.platform : dataElement.selfLinks.apps,
      iconClass: 'reg reg-attachment',
      dataType: dataElement.dataType,
    };
  });
};

export const getInstancePdf = (data: IData[], platform?: boolean): IAttachment[] => {
  if (!data) {
    return null;
  }

  const pdfElements = data.filter((element) => element.dataType === 'ref-data-as-pdf');

  if (!pdfElements) {
    return null;
  }

  const result = pdfElements.map((element) => {
    const pdfUrl = platform ? element.selfLinks.platform : element.selfLinks.apps;
    return {
      name: element.filename,
      url: pdfUrl,
      iconClass: 'reg reg-attachment',
      dataType: element.dataType,
    };
  });
  return result;
};

/**
 * Gets the attachment groupings from a list of attachments.
 * @param attachments the attachments
 * @param applicationMetadata the application metadata
 * @param textResources the application text resources
 */
export const getAttachmentGroupings = (
  attachments: IAttachment[],
  applicationMetadata: IApplication,
  textResources: ITextResource[],
): IAttachmentGrouping => {
  const attachmentGroupings: IAttachmentGrouping = {};

  if (!attachments || !applicationMetadata || !textResources) {
    return attachmentGroupings;
  }

  attachments.forEach((attachment: IAttachment) => {
    const grouping = getGroupingForAttachment(attachment, applicationMetadata);
    if (
      grouping == null ||
      applicationMetadata.attachmentGroupsToHide == null ||
      !applicationMetadata.attachmentGroupsToHide.includes(grouping)
    ) {
      const title = getTextResourceByKey(grouping, textResources);
      if (!attachmentGroupings[title]) {
        attachmentGroupings[title] = [];
      }
      attachmentGroupings[title].push(attachment);
    }
  });

  return attachmentGroupings;
};

/**
 * Gets the grouping for a specific attachment
 * @param attachment the attachment
 * @param applicationMetadata the application metadata
 */
const getGroupingForAttachment = (attachment: IAttachment, applicationMetadata: IApplication): string => {
  if (!applicationMetadata || !applicationMetadata.dataTypes || !attachment) {
    return null;
  }

  const attachmentType = applicationMetadata.dataTypes.find(
    (dataType: IDataType) => dataType.id === attachment.dataType,
  );

  if (!attachmentType || !attachmentType.grouping) {
    return null;
  }

  return attachmentType.grouping;
};
