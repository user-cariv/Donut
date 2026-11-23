const MAX_LICENSE_BODY_BYTES = 4096;

export class LicenseRequestError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export async function readLicenseRequest(request: Request): Promise<Record<string, unknown>> {
  if (Number(request.headers.get("content-length") ?? 0) > MAX_LICENSE_BODY_BYTES) {
    throw new LicenseRequestError(413, "Request is too large.");
  }
  if (request.headers.get("content-type")?.split(";", 1)[0]?.trim().toLowerCase() !== "application/json") {
    throw new LicenseRequestError(415, "Send a JSON request.");
  }

  const reader = request.body?.getReader();
  if (!reader) throw new LicenseRequestError(400, "Invalid JSON request.");
  const bytes = new Uint8Array(MAX_LICENSE_BODY_BYTES);
  let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      if (size + value.length > bytes.length) {
        await reader.cancel().catch(() => {});
        throw new LicenseRequestError(413, "Request is too large.");
      }
      bytes.set(value, size);
      size += value.length;
    }
  } finally {
    reader.releaseLock();
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes.subarray(0, size)));
  } catch {
    throw new LicenseRequestError(400, "Invalid JSON request.");
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new LicenseRequestError(400, "Invalid JSON request.");
  }
  return parsed as Record<string, unknown>;
}
