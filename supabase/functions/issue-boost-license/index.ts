import { createClient } from "npm:@supabase/supabase-js@2.119.2";
import { LicenseRequestError, readLicenseRequest } from "../_shared/read-license-request.ts";

const ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

function equalSecret(expected: string, provided: string): boolean {
  const encoder = new TextEncoder();
  const left = encoder.encode(expected);
  const right = encoder.encode(provided);
  let difference = left.length ^ right.length;
  const length = Math.max(left.length, right.length);
  for (let index = 0; index < length; index++) {
    difference |= (left[index] ?? 0) ^ (right[index] ?? 0);
  }
  return difference === 0;
}

function randomCode(): string {
  const output: string[] = [];
  const limit = Math.floor(256 / ALPHABET.length) * ALPHABET.length;
  while (output.length < 16) {
    const bytes = crypto.getRandomValues(new Uint8Array(32));
    for (const value of bytes) {
      if (value >= limit) continue;
      output.push(ALPHABET[value % ALPHABET.length]);
      if (output.length === 16) break;
    }
  }
  return output.join("");
}

async function codeHash(code: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(code));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("").toUpperCase();
}

function normalizeCode(value: unknown): string {
  return String(value ?? "").toUpperCase().replace(/[-\s]/g, "");
}

Deno.serve(async (request) => {
  if (request.method !== "POST" || request.headers.has("origin")) {
    return Response.json({ error: "Not found." }, { status: 404 });
  }
  const expectedToken = Deno.env.get("DONUT_LICENSE_ISSUER_TOKEN");
  const providedToken = request.headers.get("x-donut-issuer-token") ?? "";
  if (!expectedToken || !equalSecret(expectedToken, providedToken)) {
    return Response.json({ error: "Unauthorized." }, { status: 401 });
  }
  const url = Deno.env.get("SUPABASE_URL");
  const secretKeys = Deno.env.get("SUPABASE_SECRET_KEYS");
  let secretKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
  if (secretKeys) {
    try { secretKey = (JSON.parse(secretKeys) as Record<string, string>).default ?? secretKey; } catch { /* legacy fallback */ }
  }
  if (!url || !secretKey) return Response.json({ error: "Issuance service is unavailable." }, { status: 503 });

  try {
    const body = await readLicenseRequest(request);
    const action = body?.action === "revoke" ? "revoke" : "issue";
    if (action === "revoke") {
      const code = normalizeCode(body?.code);
      if (code.length !== 16 || [...code].some((character) => !ALPHABET.includes(character))) {
        return Response.json({ error: "Provide a valid activation code." }, { status: 400 });
      }
      const admin = createClient(url, secretKey, { auth: { autoRefreshToken: false, persistSession: false } });
      const { data: revoked, error } = await admin.rpc("revoke_donut_boost_code", { p_code_hash: await codeHash(code) });
      if (error) throw error;
      return Response.json({ revoked: revoked === true }, { headers: { "Cache-Control": "no-store" } });
    }
    const tier = Number(body?.tier);
    if (!Number.isInteger(tier) || ![1, 2, 3, 4].includes(tier)) {
      return Response.json({ error: "Tier must be 1, 2, 3, or 4." }, { status: 400 });
    }
    const admin = createClient(url, secretKey, { auth: { autoRefreshToken: false, persistSession: false } });
    for (let attempt = 0; attempt < 5; attempt++) {
      const code = randomCode();
      const hash = await codeHash(code);
      const { data: inserted, error } = await admin.rpc("issue_donut_boost_code", {
        p_code_hash: hash,
        p_tier: tier,
      });
      if (error) throw error;
      if (inserted === true) {
        const formatted = code.match(/.{4}/g)!.join("-");
        return Response.json({ code: formatted, tier }, { headers: { "Cache-Control": "no-store" } });
      }
    }
    return Response.json({ error: "Could not issue a unique activation code." }, { status: 503 });
  } catch (error) {
    if (error instanceof LicenseRequestError) return Response.json({ error: error.message }, { status: error.status });
    console.error("Boost license issuance failed", error instanceof Error ? error.name : "UnknownError");
    return Response.json({ error: "Issuance service is unavailable." }, { status: 503 });
  }
});
