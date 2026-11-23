import { createClient } from "npm:@supabase/supabase-js@2.119.2";
import { LicenseRequestError, readLicenseRequest } from "../_shared/read-license-request.ts";

const CODE_ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
const PUBLIC_KEY_NAMES = ["SUPABASE_PUBLISHABLE_KEYS", "SUPABASE_ANON_KEY"] as const;
const PRIVATE_KEY_NAMES = ["SUPABASE_SECRET_KEYS", "SUPABASE_SERVICE_ROLE_KEY"] as const;

function configuredKey(names: readonly string[]): string | undefined {
  for (const name of names) {
    const value = Deno.env.get(name);
    if (!value) continue;
    try {
      const parsed = JSON.parse(value) as Record<string, unknown>;
      if (typeof parsed.default === "string") return parsed.default;
    } catch {
      return value;
    }
  }
  return undefined;
}

function base64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function pemBytes(pem: string): Uint8Array {
  const body = pem.replace(/-----BEGIN [^-]+-----|-----END [^-]+-----|\s/g, "");
  return Uint8Array.from(atob(body), (character) => character.charCodeAt(0));
}

async function signEntitlement(claim: Record<string, unknown>): Promise<string> {
  const pem = Deno.env.get("DONUT_LICENSE_SIGNING_PRIVATE_KEY");
  const keyId = Deno.env.get("DONUT_LICENSE_KEY_ID");
  if (!pem || !keyId || !/^[A-Za-z0-9._-]{1,40}$/.test(keyId)) throw new Error("Signing key not configured");
  const key = await crypto.subtle.importKey(
    "pkcs8", pemBytes(pem), { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["sign"],
  );
  const header = base64Url(new TextEncoder().encode(JSON.stringify({ alg: "RS256", typ: "JWT", kid: keyId })));
  const payload = base64Url(new TextEncoder().encode(JSON.stringify(claim)));
  const signingInput = `${header}.${payload}`;
  const signature = await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5", key, new TextEncoder().encode(signingInput),
  );
  return `${signingInput}.${base64Url(new Uint8Array(signature))}`;
}

function normalizeCode(value: unknown): string {
  return String(value ?? "").toUpperCase().replace(/[-\s]/g, "");
}

async function sha256Hex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("").toUpperCase();
}

Deno.serve(async (request) => {
  if (request.method !== "POST") return Response.json({ error: "Method not allowed." }, { status: 405 });
  const publicKey = configuredKey(PUBLIC_KEY_NAMES);
  if (!publicKey || request.headers.get("apikey") !== publicKey) {
    return Response.json({ error: "Activation could not be verified." }, { status: 401 });
  }
  const url = Deno.env.get("SUPABASE_URL");
  const secretKey = configuredKey(PRIVATE_KEY_NAMES);
  if (!url || !secretKey || !Deno.env.get("DONUT_LICENSE_SIGNING_PRIVATE_KEY")) {
    return Response.json({ error: "Licensing service is temporarily unavailable." }, { status: 503 });
  }

  try {
    const body = await readLicenseRequest(request);
    const operation = body?.operation;
    if (operation !== "trial" && operation !== "redeem" && operation !== "restore") {
      return Response.json({ error: "Activation could not be verified." }, { status: 400 });
    }
    const hwid = String(body?.hardwareId ?? "").trim();
    if (!hwid || hwid.length > 128) return Response.json({ error: "Activation could not be verified." }, { status: 400 });
    const admin = createClient(url, secretKey, { auth: { autoRefreshToken: false, persistSession: false } });
    // Supabase's gateway supplies the client address in X-Forwarded-For; use its
    // documented leftmost client address, never a body-supplied value. Apply the
    // same limit to trials so arbitrary hardware IDs cannot grow the trial table.
    const clientIp = request.headers.get("x-forwarded-for")?.split(",", 1)[0]?.trim();
    if (!clientIp || clientIp.length > 64) {
      return Response.json({ error: "Licensing service is temporarily unavailable." }, { status: 503 });
    }
    const { data: allowed, error: limitError } = await admin.rpc("consume_donut_boost_redemption_attempt", {
      p_client_ip: clientIp,
    });
    if (limitError) throw limitError;
    if (allowed !== true) return Response.json({ error: "Too many activation attempts. Try again later." }, { status: 429 });

    let data: Record<string, unknown> | null = null;
    if (operation === "trial") {
      const { data: result, error } = await admin.rpc("claim_donut_boost_trial", { p_hwid: hwid });
      if (error) throw error;
      data = result as Record<string, unknown>;
    } else {
      const code = normalizeCode(body?.code);
      if (code.length !== 16 || [...code].some((character) => !CODE_ALPHABET.includes(character))) {
        return Response.json({ error: "Activation could not be verified." }, { status: 400 });
      }
      const codeHash = await sha256Hex(code);
      const { data: result, error } = await admin.rpc("claim_donut_boost_code_v3", {
        p_code_hash: codeHash,
        p_hwid: hwid,
        p_allow_restore: operation === "restore",
      });
      if (error) throw error;
      data = result as Record<string, unknown>;
    }

    if (!data || !["SUCCESS", "RESTORE_SAME_DEVICE"].includes(String(data.status))) {
      if (operation === "redeem" && data?.status === "ALREADY_CLAIMED_THIS_DEVICE") {
        return Response.json({
          code: "ACTIVATION_ALREADY_USED",
          error: "This activation code has already been used. It cannot be redeemed again.",
        }, { status: 409, headers: { "Cache-Control": "no-store" } });
      }
      return Response.json({ error: operation === "trial" ? "A free trial has already been used on this device." : "This activation code could not be verified." }, { status: 400 });
    }
    const activatedAt = String(data.activated_at ?? "");
    const expiresAt = String(data.expires_at ?? "");
    const tier = Number(data.tier);
    const activatedMs = Date.parse(activatedAt);
    const expiresMs = Date.parse(expiresAt);
    if (!Number.isInteger(tier) || ![1, 2, 3, 4, 5].includes(tier) || !Number.isFinite(activatedMs) || !Number.isFinite(expiresMs) || expiresMs <= activatedMs || expiresMs <= Date.now()) {
      return Response.json({ error: "Licensing service returned an invalid entitlement." }, { status: 503 });
    }
    const codeHash = operation === "trial" ? "TRIAL" : await sha256Hex(normalizeCode(body?.code));
    const entitlement = await signEntitlement({
      iss: "donut-boost",
      sub: codeHash,
      hwid,
      tier,
      activated_at: new Date(activatedMs).toISOString(),
      expires_at: new Date(expiresMs).toISOString(),
      iat: Math.floor(Date.now() / 1000),
    });
    return Response.json({ entitlement, tier, activatedAt: new Date(activatedMs).toISOString(), expiresAt: new Date(expiresMs).toISOString() });
  } catch (error) {
    if (error instanceof LicenseRequestError) return Response.json({ error: error.message }, { status: error.status });
    // Log only a stage label, not client data, codes, hardware IDs, or database details.
    console.error("Boost license redemption failed", error instanceof Error ? error.name : "UnknownError");
    return Response.json({ error: "Licensing service is temporarily unavailable." }, { status: 503 });
  }
});
