export function getInstanceOwnerId(): string {
  if (!window.location.pathname) {
    return '';
  }
  return window.location.pathname.split('/')[2];
}

export function getInstanceId(): string {
  if (!window.location.pathname) {
    return '';
  }
  return window.location.pathname.split('/')[3];
}

export function getArchiveRef(): string {
  try {
    return getInstanceId().split('-')[4];
  } catch {
    return '';
  }
}

export function getReturnUrl(): string {
  if (!globalThis.location.search) {
    return '';
  }

  const params = new URLSearchParams(globalThis.location.search)
  const lowerCaseParams = new URLSearchParams();
  for (const [name, value] of params) {
      lowerCaseParams.append(name.toLowerCase(), value);
  }
  return lowerCaseParams.get('returnurl');
}