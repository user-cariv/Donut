import { createClient } from "npm:@supabase/supabase-js@2.119.2";

const MAX_FILES = 5;
const MAX_FILE_BYTES = 8 * 1024 * 1024;
const MAX_TOTAL_BYTES = 20 * 1024 * 1024;
const MAX_REQUEST_BYTES = 24 * 1024 * 1024;
const ALLOWED_MIME_TYPES = new Set([
  "image/jpeg",
  "image/png",
  "image/webp",
  "image/gif",
  "application/pdf",
]);
const FILE_EXTENSIONS: Record<string, string> = {
  "image/jpeg": "jpg",
  "image/png": "png",
  "image/webp": "webp",
  "image/gif": "gif",
  "application/pdf": "pdf",
};

const configuredOrigins = (Deno.env.get("ALLOWED_ORIGINS") ?? "")
  .split(",")
  .map((origin) => origin.trim())
  .filter(Boolean);

function corsHeaders(origin: string | null): Record<string, string> {
  const allowedOrigin = origin && configuredOrigins.includes(origin) ? origin : "null";
  return {
    "Access-Control-Allow-Origin": allowedOrigin,
    "Access-Control-Allow-Headers": "authorization, apikey, content-type",
    "Access-Control-Allow-Methods": "POST, OPTIONS",
    "Vary": "Origin",
  };
}

function jsonResponse(
  body: Record<string, unknown>,
  status: number,
  cors: HeadersInit,
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...cors, "Content-Type": "application/json; charset=utf-8" },
  });
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;",
  })[character]!);
}

function getDefaultKey(keysEnvName: string, legacyEnvName: string): string | undefined {
  const configuredKeys = Deno.env.get(keysEnvName);
  if (configuredKeys) {
    try {
      const parsed = JSON.parse(configuredKeys) as Record<string, unknown>;
      if (typeof parsed.default === "string") return parsed.default;
    } catch (error) {
      console.error(`Invalid ${keysEnvName} environment value`, error);
    }
  }
  return Deno.env.get(legacyEnvName) || undefined;
}

function hasValidSignature(type: string, bytes: Uint8Array): boolean {
  if (type === "application/pdf") {
    return new TextDecoder().decode(bytes.slice(0, 5)) === "%PDF-";
  }
  if (type === "image/png") {
    return bytes.length >= 8 && [137, 80, 78, 71, 13, 10, 26, 10]
      .every((value, index) => bytes[index] === value);
  }
  if (type === "image/jpeg") {
    return bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff;
  }
  if (type === "image/gif") {
    const signature = new TextDecoder().decode(bytes.slice(0, 6));
    return signature === "GIF87a" || signature === "GIF89a";
  }
  if (type === "image/webp") {
    return bytes.length >= 12 &&
      new TextDecoder().decode(bytes.slice(0, 4)) === "RIFF" &&
      new TextDecoder().decode(bytes.slice(8, 12)) === "WEBP";
  }
  return false;
}

async function readBoundedFormData(request: Request, maxBytes: number): Promise<FormData | null> {
  if (!request.body) return null;
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let totalBytes = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    totalBytes += value.byteLength;
    if (totalBytes > maxBytes) {
      await reader.cancel();
      return null;
    }
    chunks.push(value);
  }
  const body = new Uint8Array(totalBytes);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  const boundedRequest = new Request(request.url, {
    method: "POST",
    headers: { "Content-Type": request.headers.get("content-type") ?? "" },
    body,
  });
  return await boundedRequest.formData();
}

async function sendEmail(
  apiKey: string,
  from: string,
  to: string,
  subject: string,
  html: string,
  replyTo?: string,
): Promise<boolean> {
  try {
    const response = await fetch("https://api.resend.com/emails", {
      method: "POST",
      headers: {
        "Authorization": `Bearer ${apiKey}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        from,
        to: [to],
        subject,
        html,
        ...(replyTo ? { reply_to: replyTo } : {}),
      }),
    });
    if (!response.ok) console.error("Resend delivery failed", response.status, await response.text());
    return response.ok;
  } catch (error) {
    // Email is a notification side effect. A mail-provider outage must not
    // make a successfully stored support ticket look as if it was lost.
    console.error("Resend request failed", error);
    return false;
  }
}

Deno.serve(async (request) => {
  let failureStage = "request validation";
  const origin = request.headers.get("origin");
  const cors = corsHeaders(origin);
  if (!origin || !configuredOrigins.includes(origin)) {
    return jsonResponse({ error: "This website is not allowed to submit support requests." }, 403, cors);
  }
  if (request.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (request.method !== "POST") return jsonResponse({ error: "Method not allowed." }, 405, cors);

  const expectedApiKey = getDefaultKey("SUPABASE_PUBLISHABLE_KEYS", "SUPABASE_ANON_KEY");
  if (!expectedApiKey || request.headers.get("apikey") !== expectedApiKey) {
    return jsonResponse({ error: "Support configuration is incomplete." }, 503, cors);
  }
  const contentLength = Number(request.headers.get("content-length") ?? 0);
  if (contentLength > MAX_REQUEST_BYTES) {
    return jsonResponse({ error: "The request is too large. Please reduce the attachment sizes." }, 413, cors);
  }

  try {
    if (!request.headers.get("content-type")?.includes("multipart/form-data")) {
      return jsonResponse({ error: "Please submit the support form." }, 415, cors);
    }
    const form = await readBoundedFormData(request, MAX_REQUEST_BYTES);
    if (!form) {
      return jsonResponse({ error: "The request is too large. Please reduce the attachment sizes." }, 413, cors);
    }
    // Quietly accept bot submissions to a honeypot without creating tickets.
    if (String(form.get("company") ?? "").trim()) {
      return jsonResponse({ ticketId: crypto.randomUUID(), received: true }, 201, cors);
    }

    const name = String(form.get("name") ?? "").trim();
    const email = String(form.get("email") ?? "").trim().toLowerCase();
    const issue = String(form.get("issue") ?? "").trim();
    if (!name || name.length > 120 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 254 || !issue || issue.length > 10000) {
      return jsonResponse({ error: "Check your name, email, and message, then try again." }, 400, cors);
    }

    const files = form.getAll("attachments").filter((value): value is File => value instanceof File && value.size > 0);
    if (files.length > MAX_FILES) {
      return jsonResponse({ error: `Attach no more than ${MAX_FILES} files.` }, 400, cors);
    }
    const totalBytes = files.reduce((sum, file) => sum + file.size, 0);
    if (totalBytes > MAX_TOTAL_BYTES || files.some((file) => file.size > MAX_FILE_BYTES || !ALLOWED_MIME_TYPES.has(file.type))) {
      return jsonResponse({ error: "Use up to 5 JPG, PNG, WebP, GIF, or PDF files, each under 8 MB and 20 MB total." }, 400, cors);
    }

    const supabaseUrl = Deno.env.get("SUPABASE_URL");
    const serviceRoleKey = getDefaultKey("SUPABASE_SECRET_KEYS", "SUPABASE_SERVICE_ROLE_KEY");
    if (!supabaseUrl || !serviceRoleKey) {
      return jsonResponse({ error: "Support storage is not configured yet." }, 503, cors);
    }
    const admin = createClient(supabaseUrl, serviceRoleKey, {
      auth: { autoRefreshToken: false, persistSession: false },
    });

    const forwardedFor = request.headers.get("x-forwarded-for")?.split(",", 1)[0]?.trim();
    if (!forwardedFor || forwardedFor.length > 64) {
      return jsonResponse({ error: "We couldn’t verify this request. Please try again later." }, 503, cors);
    }
    failureStage = "rate limit check";
    const { data: withinRateLimit, error: rateError } = await admin
      .rpc("consume_support_submission_rate_limit", { p_email: email, p_ip: forwardedFor });
    if (rateError) throw rateError;
    if (withinRateLimit !== true) {
      return jsonResponse({ error: "For safety, please wait before sending another request." }, 429, cors);
    }

    const ticketId = crypto.randomUUID();
    const uploadedPaths: string[] = [];
    const attachments: Array<{ path: string; filename: string; contentType: string; size: number }> = [];
    failureStage = "ticket insert";
    const { error: ticketError } = await admin.from("support_tickets").insert({
      id: ticketId,
      name,
      email,
      issue,
      attachments: [],
      notification_status: "pending",
    });
    if (ticketError) throw ticketError;

    try {
      failureStage = "attachment validation or upload";
      for (const file of files) {
        const bytes = new Uint8Array(await file.arrayBuffer());
        if (!hasValidSignature(file.type, bytes)) {
          throw new Error(`The file “${file.name}” does not match its declared type.`);
        }
        const path = `${ticketId}/${crypto.randomUUID()}.${FILE_EXTENSIONS[file.type]}`;
        const { error: uploadError } = await admin.storage
          .from("support-attachments")
          .upload(path, bytes, { contentType: file.type, upsert: false });
        if (uploadError) throw uploadError;
        uploadedPaths.push(path);
        attachments.push({
          path,
          filename: file.name.slice(0, 240),
          contentType: file.type,
          size: file.size,
        });
      }
      const { error: updateError } = await admin.from("support_tickets")
        .update({ attachments })
        .eq("id", ticketId);
      if (updateError) throw updateError;
    } catch (error) {
      if (uploadedPaths.length) await admin.storage.from("support-attachments").remove(uploadedPaths);
      await admin.from("support_tickets").delete().eq("id", ticketId);
      throw error;
    }

    const resendKey = Deno.env.get("RESEND_API_KEY");
    const supportInbox = Deno.env.get("SUPPORT_INBOX");
    const mailFrom = Deno.env.get("SUPPORT_FROM");
    let notificationStatus: "sent" | "failed" | "not_configured" = "not_configured";
    if (resendKey && supportInbox && mailFrom) {
      failureStage = "email notification";
      const attachmentLinks = await Promise.all(attachments.map(async (attachment) => {
        const { data } = await admin.storage.from("support-attachments")
          .createSignedUrl(attachment.path, 60 * 60 * 24 * 7);
        return data?.signedUrl
          ? `<li><a href="${escapeHtml(data.signedUrl)}">${escapeHtml(attachment.filename)}</a> (${readableBytes(attachment.size)})</li>`
          : `<li>${escapeHtml(attachment.filename)} (download link unavailable)</li>`;
      }));
      const ticketHtml = `<h2>Donut support request</h2><p><b>Ticket:</b> ${ticketId}</p><p><b>Name:</b> ${escapeHtml(name)}</p><p><b>Email:</b> ${escapeHtml(email)}</p><p><b>Message:</b></p><pre style="white-space:pre-wrap;font:inherit">${escapeHtml(issue)}</pre><p><b>Attachments:</b></p><ul>${attachmentLinks.join("") || "<li>None</li>"}</ul>`;
      const notificationSent = await sendEmail(
        resendKey,
        mailFrom,
        supportInbox,
        `[Donut Support] ${ticketId.slice(0, 8)}`,
        ticketHtml,
        email,
      );
      let confirmationSent = false;
      if (notificationSent) {
        confirmationSent = await sendEmail(
          resendKey,
          mailFrom,
          email,
          `We received your Donut support request ${ticketId.slice(0, 8)}`,
          `<p>Hi ${escapeHtml(name)},</p><p>We received your support request. Your reference is <b>${ticketId}</b>.</p><p>We’ll reply to this email as soon as we can.</p>`,
        );
      }
      notificationStatus = notificationSent && confirmationSent ? "sent" : "failed";
    }
    await admin.from("support_tickets").update({ notification_status: notificationStatus }).eq("id", ticketId);

    return jsonResponse({
      ticketId,
      received: true,
      notificationStatus,
    }, 201, cors);
  } catch (error) {
    console.error(`Support submission failed during ${failureStage}`, error);
    return jsonResponse({ error: "We couldn’t save your request. Please try again later." }, 500, cors);
  }
});

function readableBytes(bytes: number): string {
  return bytes < 1024 * 1024 ? `${Math.ceil(bytes / 1024)} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
