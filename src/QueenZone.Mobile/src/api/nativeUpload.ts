/**
 * React Native's `fetch` cannot send FormData file parts on some iOS
 * TestFlight builds (`TypeError: Network request failed`). XMLHttpRequest
 * still can, and it understands the `{ uri, name, type }` file object.
 *
 * Tests and the Node contract suite keep using `fetch` + Blob.
 */
export function shouldUseNativeMultipartUpload(
  env: {
    xhr?: unknown;
    navigatorProduct?: string;
    nodeEnv?: string;
  } = defaultNativeUploadEnvironment(),
): boolean {
  return typeof env.xhr === 'function' && env.navigatorProduct === 'ReactNative' && env.nodeEnv !== 'test';
}

function defaultNativeUploadEnvironment(): { xhr?: unknown; navigatorProduct?: string; nodeEnv?: string } {
  return {
    xhr: typeof XMLHttpRequest === 'undefined' ? undefined : XMLHttpRequest,
    navigatorProduct:
      typeof navigator === 'undefined' ? undefined : (navigator as { product?: string }).product,
    nodeEnv: process.env.NODE_ENV,
  };
}
